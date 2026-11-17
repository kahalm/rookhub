using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Puzzles;

/// <summary>
/// Zieht EIN zufälliges Puzzle je Rating-Fenster — für <c>GET /api/puzzles/random</c> und je Fenster von
/// <c>POST /api/puzzles/random-batch</c>. Jede Abfrage ist ein Index-Seek mit <c>LIMIT 1</c>, ein auf
/// <see cref="ProbeSpan"/> Zeilen begrenzter Primärschlüssel-Bereich oder (Themen-Rückfall) ein Index-Bereich unter
/// einem Zeilenbudget je Request; keine wächst unbegrenzt mit der Breite des Fensters oder der Größe der Tabelle.
///
/// <para><b>Vorher (gemessen auf Prod am 2026-11-17, 5,13 Mio. Puzzles, 40 Fenster 800–2400):</b> ohne Themen je
/// Fenster <c>EXISTS</c> + <c>MIN(Id)</c> + <c>MAX(Id)</c> über den ganzen Rating-Bereich (zwei Index-Scans à ~45 ms bei
/// 40 Punkten Breite, ×5 bei 200) → ~3,9 s je Stapel; mit UND-Themen ein <c>LIKE '%…%'</c> über die Gesamtspanne mit
/// allen Treffern im Speicher → bis in den 30-s-Timeout; der Tag-Pfad nahm je (Tag, Zufalls-Rating) immer die
/// ERSTE Zeile ab dem Rating — je Fenster höchstens (Breite+1)×Tags verschiedene Puzzles.</para>
///
/// <para><b>Ablauf je Fenster.</b> (1) <i>Probe</i>: Zufalls-Id im globalen Id-Bereich, dann das erste passende Puzzle
/// in <c>[rid, rid + ProbeSpan)</c> nach Primärschlüssel — alle Filter (Rating, Themen per <c>LIKE</c> wie bisher,
/// gelöst, schon vergeben) prüft MariaDB auf höchstens <see cref="ProbeSpan"/> Zeilen. Trägt das Fenster mehr als
/// ~0,1 % des Bestands (jedes Endless-Fenster ohne Themen, die meisten Themen), trifft die Probe praktisch immer und
/// zieht nahezu gleichverteilt über die PUZZLES. (2) Bleibt die Probe leer (dünne Filter), ohne Themen ein
/// <i>Rating-Seek</i>: Zufalls-Rating r und Zufalls-Id, dann über (Rating, Id) das nächste Puzzle mit genau diesem
/// Rating ab der Id (sonst das kleinste mit diesem Rating, sonst das nächste vorhandene Rating) — gleichverteilt über
/// die RATINGS des Fensters, innerhalb eines Ratings zufällig. Mit Themen liest <see cref="SeekTaggedAsync"/> die
/// Tag-Zeilen in wachsenden Rating-Abschnitten und zieht gleichverteilt unter den Treffern des ersten Abschnitts mit
/// Treffern.</para>
///
/// <para><b>Themen-Semantik</b> (wie bisher): <c>themes</c> = UND über Teilstrings des <c>Themes</c>-Texts („mate“ trifft
/// auch „mateIn2“); im Themen-Rückfall wird jeder Begriff zur Menge der Tags, deren Name ihn enthält (Groß/klein egal wie
/// die Kollation). <c>themesAny</c> = ODER über EXAKTE Tag-Namen (wie der bisherige Tag-Pfad), in der Probe als
/// Token-Vergleich <c>' ' + Themes + ' ' LIKE '% name %'</c>. <c>PuzzleTags</c> bildet <c>Themes</c> vollständig ab
/// (Stichprobe Prod 17 135 Puzzles: 77 310 Token = 77 310 Zeilen, Namen und Rating deckungsgleich); ist die Tabelle
/// leer, nimmt <see cref="PuzzleService"/> für Themen den alten LIKE-Weg.</para>
/// </summary>
internal sealed class PuzzleRandomPicker
{
    /// <summary>Höchstzahl Primärschlüssel-Zeilen, die eine Probe prüft (~1 MB Zeilen, gemessen ≤ 3 ms auf Prod).</summary>
    internal const int ProbeSpan = 4096;

    private const string TagNamesCacheKey = "PuzzleTagNames";

    private readonly AppDbContext _db;
    private readonly IQueryable<Puzzle> _probeBase;      // Themen (LIKE) + gelöst, OHNE Rating/Id
    private readonly List<int>? _anyTags;                // ODER-Tags (exakt), null = kein ODER-Filter
    private readonly List<List<int>> _allTagSets;        // je UND-Begriff die Tags, deren Name ihn enthält
    private readonly int? _solvedUserId;
    private readonly (int Min, int Max) _ids;
    private readonly (int Min, int Max) _ratings;
    private readonly HashSet<int>? _anySet;
    private readonly List<int> _readTags;                // alle beteiligten Tags (Rückfall liest deren Zeilen)
    private HashSet<int>? _solvedIds;
    private int _rowsRead;

    private PuzzleRandomPicker(AppDbContext db, IQueryable<Puzzle> probeBase, List<int>? anyTags,
        List<List<int>> allTagSets, int? solvedUserId, (int, int) ids, (int, int) ratings)
    {
        _db = db;
        _probeBase = probeBase;
        _anyTags = anyTags;
        _allTagSets = allTagSets;
        _solvedUserId = solvedUserId;
        _ids = ids;
        _ratings = ratings;
        _anySet = anyTags?.ToHashSet();
        _readTags = (anyTags ?? new List<int>()).Concat(allTagSets.SelectMany(x => x)).Distinct().ToList();
    }

    private bool HasThemes => _anyTags != null || _allTagSets.Count > 0;

    /// <summary>
    /// Bereitet die Auswahl für einen Request vor. <c>null</c>, wenn nichts passen KANN (leerer Bestand,
    /// ein UND-Begriff ohne passenden Tag, nur unbekannte ODER-Namen). Mit Themen setzt das eine befüllte
    /// <c>PuzzleTags</c>-Tabelle voraus (prüft der Aufrufer).
    /// </summary>
    public static async Task<PuzzleRandomPicker?> CreateAsync(AppDbContext db, IMemoryCache cache,
        string? themes, string? themesAny, int? userId, bool excludeSolved)
    {
        var ids = await CachedRangeAsync(db, cache, "PuzzleIdRange", q => q.Select(p => (int?)p.Id));
        var ratings = await CachedRangeAsync(db, cache, "PuzzleRatingRange", q => q.Select(p => (int?)p.Rating));
        if (ids.Min == null || ids.Max == null || ratings.Min == null || ratings.Max == null) return null;

        var andTerms = SplitTerms(themes);
        var anyNames = SplitTerms(themesAny);
        List<int>? anyTags = null;
        var allTagSets = new List<List<int>>();
        if (andTerms.Count > 0 || anyNames.Count > 0)
        {
            var tags = await TagNamesAsync(db, cache);
            foreach (var term in andTerms)
            {
                var set = tags.Where(t => t.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).Select(t => t.Id).ToList();
                if (set.Count == 0) return null;   // kein Tag enthält den Begriff → auch kein Themes-Text
                allTagSets.Add(set);
            }
            if (anyNames.Count > 0)
            {
                anyTags = tags.Where(t => anyNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).Select(t => t.Id).ToList();
                if (anyTags.Count == 0) return null;   // wie der bisherige Tag-Pfad: nur unbekannte Namen → nichts
                // Nur bekannte Namen in die Probe — dieselbe Menge wie im Themen-Rückfall.
                anyNames = tags.Where(t => anyTags.Contains(t.Id)).Select(t => t.Name).ToList();
            }
        }

        var probe = db.Puzzles.AsQueryable();
        foreach (var term in andTerms)
        {
            var pattern = $"%{EscapeLike(term)}%";
            probe = probe.Where(p => p.Themes != null && EF.Functions.Like(p.Themes, pattern));
        }
        if (anyNames.Count > 0)
            probe = probe.Where(AnyTokenLike(anyNames));

        int? solvedUserId = excludeSolved && userId.HasValue ? userId.Value : null;
        if (solvedUserId is int uid)
        {
            var solved = db.PuzzleAttempts.Where(a => a.UserId == uid && a.Solved).Select(a => a.PuzzleId);
            probe = probe.Where(p => !solved.Contains(p.Id));
        }
        return new PuzzleRandomPicker(db, probe, anyTags, allTagSets, solvedUserId,
            (ids.Min.Value, ids.Max.Value), (ratings.Min.Value, ratings.Max.Value));
    }

    /// <summary>
    /// Ein zufälliges Puzzle mit Rating in [<paramref name="minRating"/>, <paramref name="maxRating"/>] (offene Grenze =
    /// Rand des Bestands), das nicht in <paramref name="exclude"/> steht. <c>null</c> = kein Treffer.
    /// </summary>
    public async Task<int?> PickAsync(int? minRating, int? maxRating, ISet<int> exclude)
    {
        var lo = Math.Max(minRating ?? _ratings.Min, _ratings.Min);
        var hi = Math.Min(maxRating ?? _ratings.Max, _ratings.Max);
        if (hi < lo) return null;   // Fenster außerhalb des Bestands (oder verdreht): nichts zu holen

        var hit = await ProbeAsync(lo, hi, exclude);
        if (hit != null) return hit;
        if (!HasThemes) return await SeekByRatingAsync(PuzzleRows(exclude), lo, hi);
        return await SeekTaggedAsync(lo, hi, exclude);
    }

    /// <summary>Schritt 1: erstes passendes Puzzle in einem zufälligen, begrenzten Primärschlüssel-Bereich.</summary>
    private Task<int?> ProbeAsync(int lo, int hi, ISet<int> exclude)
    {
        var rid = Random.Shared.Next(_ids.Min, _ids.Max + 1);
        var end = rid + ProbeSpan;   // _ids.Max ≤ int.MaxValue - ProbeSpan in jeder realistischen Tabelle
        var q = _probeBase.Where(p => p.Id >= rid && p.Id < end && p.Rating >= lo && p.Rating <= hi);
        if (exclude.Count > 0)
            q = q.Where(p => !exclude.Contains(p.Id));
        return q.OrderBy(p => p.Id).Select(p => (int?)p.Id).FirstOrDefaultAsync();
    }

    /// <summary>Puzzles (ohne Themen) als (Id, Rating)-Zeilen für den Rating-Seek über IX_Puzzles_Rating.</summary>
    private IQueryable<RatedRow> PuzzleRows(ISet<int> exclude)
    {
        var q = _probeBase;   // ohne Themen: nur der gelöst-Filter
        if (exclude.Count > 0)
            q = q.Where(p => !exclude.Contains(p.Id));
        return q.Select(p => new RatedRow { Id = p.Id, Rating = p.Rating });
    }

    /// <summary>
    /// Schritt 2 mit Themen: die Tag-Zeilen aller beteiligten Tags in wachsenden Rating-Abschnitten ab einem
    /// Zufalls-Rating lesen (1, 4, 16, … Punkte; erst aufwärts bis zum Fensterende, dann abwärts) und im Speicher
    /// prüfen: mindestens ein ODER-Tag und je UND-Begriff mindestens ein Tag der Menge. Jede Abfrage ist ein reiner
    /// Bereich über den Index (TagId, Rating) — sequentiell, ohne Zeilen- oder Primärschlüssel-Zugriffe. EXISTS-Proben je Kandidat
    /// über (PuzzleId, TagId) sind auf Prod plattengebunden (gemessen: 13 000 Proben = 3,2 s). Unter den Treffern
    /// des ersten Abschnitts, der welche hat, wird gleichverteilt gezogen.
    ///
    /// <para>Weil die Probe vorher verfehlt hat, ist die Treffermenge dünn: bei ODER sind dann auch die gelesenen Zeilen
    /// wenige. Teuer wird nur eine (fast) leere UND-Kombination dichter Begriffe („mateIn1 mateIn2“: ~50 000 Zeilen je
    /// 40er-Fenster). Dafür gilt ein Zeilenbudget je Request (<see cref="RowBudget"/>); ist es aufgebraucht, bleibt für
    /// die übrigen Fenster nur die Probe — ein Fenster mit sehr seltenen Treffern kann dann entfallen.</para>
    /// </summary>
    private async Task<int?> SeekTaggedAsync(int lo, int hi, ISet<int> exclude)
    {
        if (_rowsRead >= RowBudget) return null;
        var solved = await SolvedIdsAsync();
        var r = Random.Shared.Next(lo, hi + 1);
        foreach (var (a, b) in Chunks(r, lo, hi))
        {
            var take = RowBudget - _rowsRead;
            if (take <= 0) return null;
            var rows = await _db.PuzzleTags
                .Where(pt => _readTags.Contains(pt.TagId) && pt.Rating >= a && pt.Rating <= b)
                .Select(pt => new { pt.PuzzleId, pt.TagId })
                .Take(take)
                .ToListAsync();
            _rowsRead += rows.Count;
            var hits = rows.GroupBy(x => x.PuzzleId)
                .Where(g => !exclude.Contains(g.Key) && !solved.Contains(g.Key) && Matches(g.Select(x => x.TagId)))
                .Select(g => g.Key)
                .ToList();
            if (hits.Count > 0) return hits[Random.Shared.Next(hits.Count)];
        }
        return null;
    }

    /// <summary>Lese-Budget (Tag-Zeilen) je Request für den Themen-Rückfall — gemessen ~1–2 µs je Zeile auf Prod.</summary>
    internal const int RowBudget = 250_000;

    /// <summary>Rating-Abschnitte ab r: aufwärts [r, r], [r+1, r+4], [r+5, r+20] …, danach abwärts ab r−1 genauso.</summary>
    internal static IEnumerable<(int A, int B)> Chunks(int r, int lo, int hi)
    {
        var size = 1;
        for (var a = r; a <= hi; a += size, size *= 4)
            yield return (a, (int)Math.Min((long)a + size - 1, hi));
        size = 1;
        for (var b = r - 1; b >= lo; b -= size, size *= 4)
            yield return ((int)Math.Max((long)b - size + 1, lo), b);
    }

    private bool Matches(IEnumerable<int> tagIdsOfPuzzle)
    {
        var tags = tagIdsOfPuzzle as ICollection<int> ?? tagIdsOfPuzzle.ToList();
        if (_anyTags != null && !tags.Any(_anySet!.Contains)) return false;
        foreach (var set in _allTagSets)
            if (!tags.Any(set.Contains)) return false;
        return true;
    }

    /// <summary>Gelöste Puzzles des Nutzers (bei excludeSolved) — einmal je Request in den Speicher.</summary>
    private async Task<HashSet<int>> SolvedIdsAsync()
    {
        if (_solvedIds != null) return _solvedIds;
        _solvedIds = _solvedUserId is int uid
            ? (await _db.PuzzleAttempts.Where(a => a.UserId == uid && a.Solved).Select(a => a.PuzzleId).Distinct().ToListAsync()).ToHashSet()
            : new HashSet<int>();
        return _solvedIds;
    }

    /// <summary>
    /// Zufalls-Rating r im Fenster, Zufalls-Id: das kleinste Puzzle mit Rating r ab der Id, sonst das kleinste mit
    /// Rating r (Umlauf), sonst dasselbe am nächsten vorhandenen Rating (erst aufwärts, dann abwärts). Jede Abfrage
    /// ist ein Seek auf (Rating, Id) mit LIMIT 1.
    /// </summary>
    private async Task<int?> SeekByRatingAsync(IQueryable<RatedRow> rows, int lo, int hi)
    {
        if (hi < lo) return null;
        var r = Random.Shared.Next(lo, hi + 1);
        var rid = Random.Shared.Next(_ids.Min, _ids.Max + 1);
        for (var pass = 0; pass < 2; pass++)
        {
            var at = r;
            var id = await rows.Where(x => x.Rating == at && x.Id >= rid).OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync()
                ?? await rows.Where(x => x.Rating == at && x.Id < rid).OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync();
            if (id != null || pass == 1) return id;
            var next = await rows.Where(x => x.Rating > at && x.Rating <= hi).OrderBy(x => x.Rating).Select(x => (int?)x.Rating).FirstOrDefaultAsync()
                ?? await rows.Where(x => x.Rating >= lo && x.Rating < at).OrderByDescending(x => x.Rating).Select(x => (int?)x.Rating).FirstOrDefaultAsync();
            if (next == null) return null;   // nichts Passendes im Fenster
            r = next.Value;
        }
        return null;
    }

    /// <summary>(Id, Rating)-Zeile für den Rating-Seek (EF übersetzt die Projektion mit).</summary>
    private sealed class RatedRow
    {
        public int Id { get; init; }
        public int Rating { get; init; }
    }

    private static List<string> SplitTerms(string? s) =>
        string.IsNullOrWhiteSpace(s)
            ? new List<string>()
            : s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    internal static string EscapeLike(string input) => input.Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>
    /// <c>Themes</c> enthält mindestens EINEN der Namen als ganzes Token: <c>CONCAT(' ', Themes, ' ') LIKE '% name %'</c>,
    /// ODER-verknüpft — dieselbe Treffermenge wie der exakte Tag-Vergleich. Handgebauter Ausdruck, weil EF ein
    /// OrElse über eine variable Anzahl LIKEs nicht aus einer Schleife von Lambdas zusammensetzt.
    /// </summary>
    internal static Expression<Func<Puzzle, bool>> AnyTokenLike(IReadOnlyList<string> names)
    {
        var p = Expression.Parameter(typeof(Puzzle), "p");
        var themesProp = Expression.Property(p, nameof(Puzzle.Themes));
        var padded = Expression.Call(
            typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string), typeof(string) })!,
            Expression.Constant(" "), themesProp, Expression.Constant(" "));
        // EF.Functions als STATISCHER Property-Zugriff (nicht als Konstante!) — nur so erkennt der
        // MySQL-Übersetzer das Like-Muster (siehe PuzzleService.WhereAnyThemeLike).
        var efFns = Expression.Property(null, typeof(EF).GetProperty(nameof(EF.Functions))!);
        var likeMethod = typeof(DbFunctionsExtensions).GetMethod(nameof(DbFunctionsExtensions.Like),
            new[] { typeof(DbFunctions), typeof(string), typeof(string) })!;
        Expression? any = null;
        foreach (var name in names)
        {
            Expression like = Expression.Call(likeMethod, efFns, padded,
                Expression.Constant($"% {EscapeLike(name)} %", typeof(string)));
            any = any == null ? like : Expression.OrElse(any, like);
        }
        var body = Expression.AndAlso(Expression.NotEqual(themesProp, Expression.Constant(null, typeof(string))), any!);
        return Expression.Lambda<Func<Puzzle, bool>>(body, p);
    }

    /// <summary>Alle Tags (74 Zeilen auf Prod) — gecacht, der Import legt selten neue an.</summary>
    private static async Task<List<(int Id, string Name)>> TagNamesAsync(AppDbContext db, IMemoryCache cache)
    {
        if (cache.TryGetValue<List<(int, string)>>(TagNamesCacheKey, out var cached) && cached != null)
            return cached;
        var list = (await db.Tags.Select(t => new { t.Id, t.Name }).ToListAsync()).Select(t => (t.Id, t.Name)).ToList();
        cache.Set(TagNamesCacheKey, list, TimeSpan.FromMinutes(5));
        return list;
    }

    /// <summary>MIN/MAX einer Puzzle-Spalte (PK bzw. IX_Puzzles_Rating → je ein Index-Ende), 5 min gecacht.</summary>
    private static async Task<(int? Min, int? Max)> CachedRangeAsync(AppDbContext db, IMemoryCache cache, string key,
        Func<IQueryable<Puzzle>, IQueryable<int?>> column)
    {
        if (cache.TryGetValue<(int?, int?)>(key, out var cached))
            return cached;
        var result = (await column(db.Puzzles).MinAsync(), await column(db.Puzzles).MaxAsync());
        cache.Set(key, result, TimeSpan.FromMinutes(5));
        return result;
    }
}
