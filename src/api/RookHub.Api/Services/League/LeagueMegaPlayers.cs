using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Das Spielerverzeichnis der ganzen ChessBase-Megabase (<see cref="LeagueMegaPlayer"/>): einspielen und durchsuchen.
/// Gebraucht beim Korrigieren eines Spielernamens in der Vereins-Datenbank, wenn der Gegner KEIN Ligaspieler ist —
/// dann bekommt die Partie wenigstens seinen richtigen Namen und seine FIDE-ID.
/// </summary>
public sealed class LeagueMegaPlayers
{
    public const int BatchSize = 5000;
    private readonly AppDbContext _db;

    public LeagueMegaPlayers(AppDbContext db) => _db = db;

    public static string KeyOf(string name) => LeagueRosterIndex.Fold(LeagueNames.Clean(name), false);

    /// <summary>Ein Spieler aus dem Verzeichnis, der zu einer Partie-Seite passt.</summary>
    public sealed record Hit(string Name, string? Fide);

    /// <summary>
    /// Die Schlüssel, unter denen ein Name aus einer Partie im Verzeichnis stehen kann — das Verzeichnis schreibt
    /// „Nachname, Vorname" (ChessBase). Ohne Komma ist die Reihenfolge offen: „Helmut Angerer" und „Angerer Helmut"
    /// werden beide als „angerer, helmut" bzw. „helmut, angerer" gefragt.
    /// Umlaute in beiden Formen („Höcher" → „hocher", „hoecher"; ChessBase schreibt „Hoecher, Michael").
    /// </summary>
    public static IEnumerable<string> LookupKeys(string? name)
    {
        var key = LeagueNames.NameKey(LeagueNames.StripTitles(name));
        var plain = LeagueRosterIndex.Fold(key, false);
        if (plain.Length == 0) yield break;
        foreach (var folded in new[] { plain, LeagueRosterIndex.Fold(key, true) }.Distinct())
            foreach (var k in KeysOf(folded))
                yield return k.Length > 120 ? k[..120] : k;
    }

    private static IEnumerable<string> KeysOf(string folded)
    {
        yield return folded;
        var comma = folded.IndexOf(',');
        if (comma >= 0)
        {
            var last = folded[..comma].Trim();
            var first = folded[(comma + 1)..].Trim();
            yield return first.Length == 0 ? last : $"{last}, {first}";
            yield break;
        }
        var t = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (t.Length < 2) yield break;
        yield return $"{t[^1]}, {string.Join(' ', t[..^1])}";
        yield return $"{t[0]}, {string.Join(' ', t[1..])}";
    }

    /// <summary>
    /// Nachschlagen für EINEN Abgleich (Übersicht, Import, Formular): die gebrauchten Zeilen einmal geladen. Ein Name
    /// passt nur EINDEUTIG — tragen die Treffer verschiedene FIDE-IDs, ist es keiner (lieber „nicht erkannt" als der
    /// falsche Namensvetter); mit genau einer FIDE-ID gilt der Spieler mit ihr, ganz ohne nur, wenn alle denselben
    /// Namen tragen.
    /// </summary>
    public sealed class Lookup
    {
        public static readonly Lookup Empty = new(Array.Empty<LeagueMegaPlayer>());
        private readonly Dictionary<string, List<LeagueMegaPlayer>> _byKey;
        private readonly Dictionary<string, LeagueMegaPlayer> _byFide;

        public Lookup(IEnumerable<LeagueMegaPlayer> rows)
        {
            var list = rows.ToList();
            _byKey = list.GroupBy(r => r.NameKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            _byFide = list.Where(r => r.FideId != null).GroupBy(r => r.FideId!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.MaxBy(r => r.Games)!, StringComparer.Ordinal);
        }

        public Hit? ByFide(string? fide) =>
            !string.IsNullOrWhiteSpace(fide) && _byFide.TryGetValue(fide.Trim(), out var r) ? new Hit(r.Name, r.FideId) : null;

        public Hit? ByName(string? name)
        {
            var hits = LookupKeys(name).Distinct(StringComparer.Ordinal)
                .SelectMany(k => _byKey.TryGetValue(k, out var l) ? l : Enumerable.Empty<LeagueMegaPlayer>()).Distinct().ToList();
            if (hits.Count == 0) return null;
            var fides = hits.Where(h => h.FideId != null).Select(h => h.FideId!).Distinct(StringComparer.Ordinal).ToList();
            if (fides.Count > 1) return null;
            if (fides.Count == 0 && hits.Select(h => h.NameKey).Distinct(StringComparer.Ordinal).Count() > 1) return null;
            var best = hits.Where(h => fides.Count == 0 || h.FideId == fides[0]).MaxBy(h => h.Games)!;
            return new Hit(best.Name, best.FideId);
        }
    }

    /// <summary>Die Zeilen zu diesen Namen (<see cref="LookupKeys"/>) und FIDE-IDs laden — in Portionen, eine Übersicht
    /// hat bis zu 1000 Namen.</summary>
    public async Task<Lookup> LookupAsync(IEnumerable<string?> names, IEnumerable<string?> fides, CancellationToken ct)
    {
        var keys = names.Where(n => !string.IsNullOrWhiteSpace(n)).SelectMany(LookupKeys).Distinct(StringComparer.Ordinal).ToList();
        var ids = fides.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0 && ids.Count == 0) return Lookup.Empty;
        var rows = new List<LeagueMegaPlayer>();
        foreach (var chunk in keys.Chunk(500))
            rows.AddRange(await _db.LeagueMegaPlayers.AsNoTracking().Where(p => chunk.Contains(p.NameKey)).ToListAsync(ct));
        foreach (var chunk in ids.Chunk(500))
            rows.AddRange(await _db.LeagueMegaPlayers.AsNoTracking().Where(p => p.FideId != null && chunk.Contains(p.FideId)).ToListAsync(ct));
        return new Lookup(rows.DistinctBy(r => r.Id));
    }

    /// <summary>
    /// Alles ersetzen: TSV-Zeilen <c>name \t fide \t games \t last_year \t max_elo</c>. Liefert die Zeilenzahl.
    /// <para>Relational liegen Löschen und alle Portionen in EINER Transaktion (Execution-Strategy-Muster wie <c>KidsPuzzleService.ReplaceAsync</c>):
    /// bricht der Upload ab (<paramref name="ct"/> = RequestAborted, Proxy-Zeitlimit, zu großer Rumpf, Datenbankfehler),
    /// bleibt das alte Verzeichnis vollständig stehen statt eines halben, und ein paralleler Abgleich liest bis zum
    /// Commit den alten Stand — nie ein Teilverzeichnis, in dem ein Name fälschlich eindeutig wäre.</para>
    /// </summary>
    public async Task<int> ReplaceAsync(TextReader tsv, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            _db.LeagueMegaPlayers.RemoveRange(_db.LeagueMegaPlayers);
            await _db.SaveChangesAsync(ct);
            return await FillAsync(tsv, ct);
        }
        // Die Transaktion muss in der Execution-Strategy laufen (EnableRetryOnFailure verweigert sonst eine selbst
        // geöffnete). Der Rumpf ist aber ein Strom und lässt sich nicht zurückspulen: ein Wiederholversuch bricht ab —
        // die Transaktion ist dann zurückgerollt, das alte Verzeichnis steht — statt mit dem Rest der Datei neu zu füllen.
        var strategy = _db.Database.CreateExecutionStrategy();
        var started = false;
        return await strategy.ExecuteAsync(async () =>
        {
            if (started) throw new InvalidOperationException("Megabase-Verzeichnis: Einspielen nicht wiederholbar — Datei neu hochladen.");
            started = true;
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await _db.LeagueMegaPlayers.ExecuteDeleteAsync(ct);
            var n = await FillAsync(tsv, ct);
            await tx.CommitAsync(ct);
            return n;
        });
    }

    private async Task<int> FillAsync(TextReader tsv, CancellationToken ct)
    {
        var auto = _db.ChangeTracker.AutoDetectChangesEnabled;
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        var n = 0;
        var batch = new List<LeagueMegaPlayer>(BatchSize);
        try
        {
            string? line;
            while ((line = await tsv.ReadLineAsync(ct)) != null)
            {
                var f = line.Split('\t');
                if (f.Length < 3 || string.IsNullOrWhiteSpace(f[0])) continue;
                var name = LeagueNames.Clean(f[0]);
                if (name.Length > 120) name = name[..120];
                batch.Add(new LeagueMegaPlayer
                {
                    Name = name,
                    NameKey = KeyOf(name) is { Length: > 120 } k ? k[..120] : KeyOf(name),
                    FideId = f[1].Trim() is { Length: > 0 and <= 16 } fide ? fide : null,
                    Games = int.TryParse(f[2], out var g) ? g : 0,
                    LastYear = f.Length > 3 && int.TryParse(f[3], out var y) ? y : null,
                    MaxElo = f.Length > 4 && int.TryParse(f[4], out var e) ? e : null,
                });
                if (batch.Count >= BatchSize) { n += await FlushAsync(batch, ct); }
            }
            n += await FlushAsync(batch, ct);
        }
        finally { _db.ChangeTracker.AutoDetectChangesEnabled = auto; }
        return n;
    }

    private async Task<int> FlushAsync(List<LeagueMegaPlayer> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return 0;
        _db.LeagueMegaPlayers.AddRange(batch);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        var n = batch.Count;
        batch.Clear();
        return n;
    }

    /// <summary>
    /// Suche („like", Wunsch 2026-09-28): jedes getippte Wort muss IRGENDWO im Namen stehen (ohne Groß/klein, Akzente,
    /// Umlaute in allen Schreibweisen — <see cref="LeagueRosterIndex.Spellings"/>) — in der Datenbank als
    /// <c>NameKey LIKE '%wort%'</c> je Wort und Schreibweise (bei 432 000 Zeilen ein Durchlauf von Zehntelsekunden).
    /// Wortanfänge zuerst, dann meistgespielte. Eine reine Zahl sucht die FIDE-ID.
    /// </summary>
    public async Task<List<LeagueMegaPlayer>> SearchAsync(string query, int take, CancellationToken ct)
    {
        if (LeagueRosterIndex.IsFideQuery(query))
        {
            var id = query.Trim();
            return await _db.LeagueMegaPlayers.AsNoTracking().Where(p => p.FideId == id)
                .OrderByDescending(p => p.Games).Take(take).ToListAsync(ct);
        }
        var words = LeagueRosterIndex.QueryWords(LeagueNames.StripTitles(query).Replace('.', ' '))
            .Where(w => w[0].Length >= 2).DistinctBy(w => w[0]).Take(4).ToList();
        if (words.Count == 0) return new();
        var q = _db.LeagueMegaPlayers.AsNoTracking().AsQueryable();
        foreach (var w in words)
        {
            // Höchstens zwei Schreibweisen — ausgeschrieben, damit der Anbieter ein einfaches OR aus LIKEs übersetzt.
            var (a, b) = (w[0], w.Count > 1 ? w[1] : w[0]);
            q = q.Where(p => p.NameKey.Contains(a) || p.NameKey.Contains(b));
        }
        var hits = await q.OrderByDescending(p => p.Games).Take(300).ToListAsync(ct);
        bool Prefixes(LeagueMegaPlayer p)
        {
            var own = p.NameKey.Split(new[] { ' ', ',', '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
            return words.All(w => own.Any(o => w.Any(v => o.StartsWith(v, StringComparison.Ordinal))));
        }
        return hits.OrderBy(p => Prefixes(p) ? 0 : 1).ThenByDescending(p => p.Games).Take(take).ToList();
    }
}
