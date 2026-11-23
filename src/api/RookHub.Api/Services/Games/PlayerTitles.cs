using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.Services.League;

namespace RookHub.Api.Services.Games;

/// <summary>
/// FIDE-Titel (GM, IM, FM …) der Spieler einer gespeicherten Partie (1.009.0, Wunsch 2026-11-23: „IM Grötz, Harald“ im Kopf
/// der Partie). Nichts wird gespeichert — die Seite bekommt den Titel beim LESEN, eine aktualisierte Meldeliste wirkt sofort.
/// Je Seite der Reihe nach, die erste Stufe mit einem Ergebnis entscheidet:
/// <list type="number">
/// <item>PGN-Kopf <c>[WhiteTitle]</c>/<c>[BlackTitle]</c> mit einem FIDE-Titel;</item>
/// <item>bekannte FIDE-ID — PGN-Kopf <c>[WhiteFideId]</c>, sonst die der verbundenen Vereinspartie — und der Titel, den die
/// Liga-Meldelisten (<c>LeaguePlayers.Title</c>) dieser ID geben. Ist eine FIDE-ID bekannt, gilt NUR sie: ohne Titel dazu
/// bleibt die Seite ohne, auch wenn ein Namensvetter einen trägt;</item>
/// <item>sonst der Name: genau EIN Ligaspieler mit FIDE-ID trägt alle Namensteile (<see cref="LeagueRosterIndex.MatchFullName"/>:
/// Reihenfolge, Komma, akademische Titel, Akzente, Umlaut-Schreibweisen egal), UND das Megabase-Verzeichnis kennt unter diesem
/// Namen keine ANDERE FIDE-ID, UND eine Elo im PGN liegt höchstens <see cref="EloTolerance"/> neben den Elo der Meldelisten.</item>
/// </list>
/// Nur FIDE-Titel (<see cref="FideTitles"/>); nationale („MK“, „NM“), Arena- („AGM“) und Plattform-Titel („LM“, „BOT“) nie.
/// Mehrere Titel einer ID über die Saisonen (FM, später IM) → der höchste. Online-Partien (chess.com/lichess) bekommen keinen:
/// dort stehen Kontonamen.
/// </summary>
public sealed class PlayerTitles
{
    /// <summary>Die angezeigten Titel, höchster zuerst (Reihenfolge = Rang).</summary>
    public static readonly IReadOnlyList<string> FideTitles = ["GM", "IM", "WGM", "FM", "WIM", "CM", "WFM", "WCM"];

    /// <summary>So weit darf eine Elo im PGN neben der Spanne der Meldelisten liegen, damit ein Namenstreffer zählt.</summary>
    public const int EloTolerance = 300;

    /// <summary>Eine Seite einer Partie. <see cref="Fide"/> = bekannte FIDE-ID (PGN-Kopf oder Vereinspartie).</summary>
    public sealed record Side(string? Name, string? Fide, string? PgnTitle, int? Elo);

    private sealed record Known(int? MinElo, int? MaxElo, string Title);

    private sealed record Index(LeagueRosterIndex Roster, Dictionary<string, Known> ByFide);

    private sealed record CacheKey(int Players, int? MaxPlayerId);

    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _db;
    private readonly IMemoryCache? _cache;

    /// <param name="cache">Hält den Index der Meldelisten (Schlüssel: Anzahl + höchste Id der Zeilen, wie
    /// <see cref="ClubGameReader.RosterAsync"/>); ohne (Tests) wird er je Aufruf gebaut.</param>
    public PlayerTitles(AppDbContext db, IMemoryCache? cache = null) { _db = db; _cache = cache; }

    /// <summary>Normalisierter FIDE-Titel oder <c>null</c> („im" → „IM", „MK" → <c>null</c>).</summary>
    public static string? Normalize(string? title)
    {
        var t = title?.Trim().ToUpperInvariant();
        return t != null && FideTitles.Contains(t) ? t : null;
    }

    /// <summary>„IM Grötz, Harald“ — der Name mit vorangestelltem Titel (Link-Vorschau); ohne Titel der Name.</summary>
    public static string WithTitle(string? title, string name) => string.IsNullOrEmpty(title) ? name : $"{title} {name}";

    /// <summary>Titel je Seite, in derselben Reihenfolge; <c>null</c> = keiner sicher bekannt.</summary>
    public async Task<IReadOnlyList<string?>> ResolveAsync(IReadOnlyList<Side> sides, CancellationToken ct = default)
    {
        var result = new string?[sides.Count];
        if (sides.Count == 0) return result;
        Index? index = null;
        var nameHits = new List<(int I, string Fide, string Title)>();
        for (var i = 0; i < sides.Count; i++)
        {
            var s = sides[i];
            if (Normalize(s.PgnTitle) is { } tag) { result[i] = tag; continue; }
            index ??= await IndexAsync(ct);
            var fide = Clean(s.Fide);
            if (fide != null)
            {
                result[i] = TitleOf(index, fide)?.Title;
                continue;
            }
            if (index.Roster.MatchFullName(s.Name) is not { Fide: { } pf }) continue;
            if (TitleOf(index, pf) is not { } known) continue;
            if (s.Elo is int elo && known.MinElo is int lo && known.MaxElo is int hi
                && (elo < lo - EloTolerance || elo > hi + EloTolerance)) continue;
            nameHits.Add((i, pf, known.Title));
        }
        if (nameHits.Count > 0)
        {
            // Megabase als Gegenprobe: trägt dort derselbe Name eine ANDERE FIDE-ID, ist der Treffer nicht eindeutig.
            var keys = nameHits.SelectMany(h => MegaKeys(sides[h.I].Name)).Distinct(StringComparer.Ordinal).ToList();
            var mega = await _db.LeagueMegaPlayers.AsNoTracking().Where(m => keys.Contains(m.NameKey) && m.FideId != null)
                .Select(m => new { m.NameKey, m.FideId }).ToListAsync(ct);
            foreach (var (i, fide, title) in nameHits)
            {
                var own = MegaKeys(sides[i].Name).ToHashSet(StringComparer.Ordinal);
                var others = mega.Where(m => own.Contains(m.NameKey) && m.FideId!.Trim() != fide
                    && index!.Roster.ByFide(m.FideId)?.Fide != fide);
                if (!others.Any()) result[i] = title;
            }
        }
        // Steht der Titel schon im Namen („FM Humer, Wolfgang“), nicht doppelt.
        for (var i = 0; i < sides.Count; i++)
            if (result[i] is { } t && NameCarries(sides[i].Name, t)) result[i] = null;
        return result;
    }

    /// <summary>Titel der beiden Seiten einer gespeicherten Partie. <paramref name="pgnHead"/> = Anfang des PGN (Kopf),
    /// <paramref name="clubFides"/> = FIDE-IDs der verbundenen Vereinspartie.</summary>
    public static (Side White, Side Black) SidesOf(string? white, string? black, int? whiteElo, int? blackElo,
        string? pgnHead, (string? White, string? Black) clubFides)
    {
        var head = HeadOf(pgnHead);
        return (new Side(white, Clean(Header(head, "WhiteFideId")) ?? Clean(clubFides.White), Header(head, "WhiteTitle"), whiteElo),
            new Side(black, Clean(Header(head, "BlackFideId")) ?? Clean(clubFides.Black), Header(head, "BlackTitle"), blackElo));
    }

    /// <summary>Nur der Kopf des PGN: bis zur ersten Zeile, die kein Tag ist (ein „[White …]“ in einem Kommentar zählt nicht).</summary>
    internal static string HeadOf(string? pgn)
    {
        if (string.IsNullOrEmpty(pgn)) return string.Empty;
        var text = pgn.Length > SavedGameEventLabel.HeadChars ? pgn[..SavedGameEventLabel.HeadChars] : pgn;
        var lines = text.Split('\n').Select(l => l.Trim()).SkipWhile(l => l.Length == 0)
            .TakeWhile(l => l.StartsWith('['));
        return string.Join('\n', lines);
    }

    private static string? Header(string head, string tag)
    {
        var m = Regex.Match(head, $"^\\[{tag}\\s+\"([^\"\\n]*)\"\\]", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? Clean(string? fide) =>
        fide?.Trim() is { Length: >= 3 and <= 16 } f && f.All(char.IsAsciiDigit) && f.Any(c => c != '0') ? f : null;

    private static Known? TitleOf(Index index, string fide) =>
        index.ByFide.GetValueOrDefault(fide) ?? (index.Roster.ByFide(fide)?.Fide is { } canon ? index.ByFide.GetValueOrDefault(canon) : null);

    private static bool NameCarries(string? name, string title) =>
        Regex.IsMatch(name ?? "", $@"(^|[\s,(]){title}($|[\s,)])");

    /// <summary>Schlüssel des Megabase-Verzeichnisses („groetz, harald“: klein, ohne Akzente, Umlaute ausgeschrieben, Nachname
    /// vorn mit Komma) für einen Namen; ohne Komma beide Reihenfolgen.</summary>
    internal static IEnumerable<string> MegaKeys(string? name)
    {
        var clean = LeagueNames.NameKey(LeagueNames.StripTitles(name ?? ""));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tr in new[] { true, false })
        {
            var folded = LeagueRosterIndex.Fold(clean, tr);
            var comma = folded.IndexOf(',');
            if (comma >= 0)
            {
                keys.Add($"{Words(folded[..comma])}, {Words(folded[(comma + 1)..])}");
                continue;
            }
            var w = Words(folded).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (w.Length < 2) continue;
            keys.Add($"{w[0]}, {string.Join(' ', w[1..])}");
            keys.Add($"{w[^1]}, {string.Join(' ', w[..^1])}");
        }
        return keys;
    }

    private static string Words(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

    private async Task<Index> IndexAsync(CancellationToken ct)
    {
        CacheKey? key = null;
        if (_cache != null)
        {
            key = new CacheKey(await _db.LeaguePlayers.CountAsync(ct), await _db.LeaguePlayers.MaxAsync(p => (int?)p.Id, ct));
            if (_cache.TryGetValue(key, out Index? hit) && hit != null) return hit;
        }
        var rows = await _db.LeaguePlayers.AsNoTracking()
            .Select(p => new { p.Tnr, p.Team, p.Name, p.NameKey, p.FideId, p.Title, Elo = p.EloI ?? p.EloN }).ToListAsync(ct);
        var roster = new LeagueRosterIndex(rows.Select(r => new LeagueRosterIndex.Row(r.Tnr, r.Team, r.Name, r.NameKey, r.FideId)));
        var byFide = new Dictionary<string, Known>(StringComparer.Ordinal);
        foreach (var g in rows.Where(r => Clean(r.FideId) != null).GroupBy(r => r.FideId!.Trim()))
        {
            var title = g.Select(r => Normalize(r.Title)).OfType<string>()
                .OrderBy(t => FideTitles.ToList().IndexOf(t)).FirstOrDefault();
            if (title == null) continue;
            var elos = g.Select(r => r.Elo).Where(e => e > 0).ToList();
            byFide[g.Key] = new Known(elos.Count > 0 ? elos.Min() : null, elos.Count > 0 ? elos.Max() : null, title);
        }
        var index = new Index(roster, byFide);
        if (_cache != null && key != null) _cache.Set(key, index, CacheTime);
        return index;
    }
}
