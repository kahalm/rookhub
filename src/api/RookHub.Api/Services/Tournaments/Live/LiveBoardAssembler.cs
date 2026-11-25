using System.Globalization;
using System.Text;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>Ein Spieler der chess-results-Startliste (nur, was die Zuordnung braucht).</summary>
public sealed record CrPlayer(string Name, string? FideId, int? Elo, string? Team, int? Board);
/// <summary>Einzelpaarung einer Runde (chess-results-Brett, Namen wie in der Startliste).</summary>
public sealed record CrPairing(int Board, string White, string Black, string? Result);
/// <summary>Mannschaftspaarung einer Runde.</summary>
public sealed record CrMatch(int MatchNumber, string Home, string Away, decimal? HomeScore, decimal? AwayScore);

/// <summary>Was der Crawler zu Turnier + Runde weiß.</summary>
public sealed record CrRound(bool Team, IReadOnlyList<CrPlayer> Players, IReadOnlyList<CrPairing> Pairings, IReadOnlyList<CrMatch> Matches);

/// <summary>Ein Brett der Ansicht: chess-results-Brett + (falls übertragen) die Lichess-Partie.</summary>
public sealed record LiveBoardSlot(int Board, string White, string Black, string? Result, string? GameId, bool? HomeIsWhite = null);

/// <summary>Eine Begegnung im Mannschaftsturnier mit ihren Brettern (Reihenfolge = chess-results-Aufstellung).</summary>
public sealed record LiveMatchSlot(int MatchNumber, string Home, string Away, decimal? HomeScore, decimal? AwayScore,
    decimal LiveHome, decimal LiveAway, IReadOnlyList<LiveBoardSlot> Boards);

/// <summary>Die Runde als Bretter: je Begegnung (Mannschaft) bzw. je Brett (Einzel); <see cref="Unassigned"/> = Partien
/// der Übertragung, die keinem chess-results-Brett zuzuordnen waren.</summary>
public sealed record LiveRoundLayout(bool Team, IReadOnlyList<LiveMatchSlot> Matches, IReadOnlyList<LiveBoardSlot> Boards,
    IReadOnlyList<string> Unassigned);

/// <summary>
/// Ordnet die Lichess-Partien einer Runde den chess-results-Brettern zu (rein, getestet). Zuerst über die FIDE-ID
/// (Startliste ↔ <c>WhiteFideId</c>/<c>BlackFideId</c>), nur wenn sie fehlt über den Namen („Mittal, Aditya" =
/// „Aditya Mittal": Wörter ohne Akzente, sortiert). Brettreihenfolge = chess-results, nie die Lichess-Reihenfolge.
/// <list type="bullet">
/// <item>Einzelturnier: die Paarung der Runde nennt Brett, Weiß, Schwarz (Namen) → Startliste → FIDE-ID → Partie.</item>
/// <item>Mannschaftsturnier: der Crawler kennt je Runde nur die Begegnungen, nicht die Brettpaarungen. Die Partie gehört
///   zu der Begegnung, deren beide Mannschaften die zwei Spieler stellen (Mannschaft aus der Startliste, sonst
///   <c>WhiteTeam</c>/<c>BlackTeam</c>); die Bretter sind nach der Aufstellungsnummer (Brett in der Meldeliste) geordnet —
///   die Ligaordnung verlangt, dass eine Mannschaft in Reihenfolge der Meldeliste aufstellt.</item>
/// </list>
/// </summary>
public static class LiveBoardAssembler
{
    public static LiveRoundLayout Assemble(CrRound round, IReadOnlyCollection<LiveGame> games)
    {
        var byFide = new Dictionary<string, CrPlayer>(StringComparer.Ordinal);
        var byName = new Dictionary<string, CrPlayer>(StringComparer.Ordinal);
        foreach (var p in round.Players)
        {
            if (p.FideId is { Length: > 0 } f) byFide.TryAdd(f, p);
            byName.TryAdd(NameKey(p.Name), p);
        }
        CrPlayer? Find(LivePlayer lp) =>
            lp.FideId is { } f && byFide.TryGetValue(f, out var p) ? p : byName.GetValueOrDefault(NameKey(lp.Name));

        return round.Team ? AssembleTeam(round, games, Find) : AssembleSingle(round, games, byName, Find);
    }

    private static LiveRoundLayout AssembleSingle(CrRound round, IReadOnlyCollection<LiveGame> games,
        Dictionary<string, CrPlayer> byName, Func<LivePlayer, CrPlayer?> find)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var boards = new List<LiveBoardSlot>();
        foreach (var pr in round.Pairings.OrderBy(x => x.Board))
        {
            var w = byName.GetValueOrDefault(NameKey(pr.White));
            var b = byName.GetValueOrDefault(NameKey(pr.Black));
            var game = games.FirstOrDefault(g => !used.Contains(g.Id) && Same(find(g.White), w, g.White, pr.White) && Same(find(g.Black), b, g.Black, pr.Black))
                       // Farben vertauscht übertragen (kommt bei Handeingaben vor) — trotzdem dieses Brett.
                       ?? games.FirstOrDefault(g => !used.Contains(g.Id) && Same(find(g.White), b, g.White, pr.Black) && Same(find(g.Black), w, g.Black, pr.White));
            if (game is not null) used.Add(game.Id);
            boards.Add(new LiveBoardSlot(pr.Board, pr.White, pr.Black, pr.Result, game?.Id));
        }
        var rest = games.Where(g => !used.Contains(g.Id)).OrderBy(g => BoardTagOrder(g.BoardTag)).Select(g => g.Id).ToList();
        return new LiveRoundLayout(false, [], boards, rest);
    }

    /// <summary>Dieselbe Person: über die Startliste (FIDE-ID → Spieler), sonst Name gegen Name.</summary>
    private static bool Same(CrPlayer? fromGame, CrPlayer? fromPairing, LivePlayer lp, string pairingName) =>
        fromGame is not null && fromPairing is not null ? ReferenceEquals(fromGame, fromPairing) : NameKey(lp.Name) == NameKey(pairingName);

    private static LiveRoundLayout AssembleTeam(CrRound round, IReadOnlyCollection<LiveGame> games, Func<LivePlayer, CrPlayer?> find)
    {
        var teamKeys = round.Matches.SelectMany(m => new[] { m.Home, m.Away }).Distinct().ToDictionary(t => t, TeamKey);
        string? TeamOf(CrPlayer? p, string? header)
        {
            if (p?.Team is { } t && teamKeys.ContainsKey(t)) return t;
            if (string.IsNullOrWhiteSpace(header)) return null;
            var hk = TeamKey(header);
            // Lichess kürzt oft („Pinggau-Friedberg" für „SK Pinggau-Friedberg"): enthält der eine den anderen, zählt es.
            var hits = teamKeys.Where(x => x.Value == hk || x.Value.Contains(hk) || hk.Contains(x.Value)).Select(x => x.Key).ToList();
            return hits.Count == 1 ? hits[0] : null;
        }

        var perMatch = round.Matches.ToDictionary(m => m.MatchNumber, _ => new List<(LiveGame G, bool HomeWhite, int Order)>());
        var unassigned = new List<LiveGame>();
        foreach (var g in games)
        {
            var wp = find(g.White);
            var bp = find(g.Black);
            var wt = TeamOf(wp, g.White.Team);
            var bt = TeamOf(bp, g.Black.Team);
            var match = round.Matches.FirstOrDefault(m => (m.Home == wt && m.Away == bt) || (m.Home == bt && m.Away == wt))
                        ?? (wt is null || bt is null
                            ? round.Matches.SingleOrDefaultSafe(m => wt is not null && (m.Home == wt || m.Away == wt) || bt is not null && (m.Home == bt || m.Away == bt))
                            : null);
            if (match is null) { unassigned.Add(g); continue; }
            var homeWhite = wt == match.Home || (wt is null && bt == match.Away);
            var homePlayer = homeWhite ? wp : bp;
            var awayPlayer = homeWhite ? bp : wp;
            var order = homePlayer?.Board ?? awayPlayer?.Board ?? 1000 + BoardTagOrder(g.BoardTag);
            perMatch[match.MatchNumber].Add((g, homeWhite, order));
        }

        var matches = new List<LiveMatchSlot>();
        foreach (var m in round.Matches.OrderBy(x => x.MatchNumber))
        {
            var list = perMatch[m.MatchNumber].OrderBy(x => x.Order).ThenBy(x => BoardTagOrder(x.G.BoardTag)).ToList();
            decimal home = 0, away = 0;
            var boards = new List<LiveBoardSlot>();
            for (var i = 0; i < list.Count; i++)
            {
                var (g, homeWhite, _) = list[i];
                var (wPts, bPts) = Points(g.Result);
                home += homeWhite ? wPts : bPts;
                away += homeWhite ? bPts : wPts;
                boards.Add(new LiveBoardSlot(i + 1, g.White.Name, g.Black.Name, g.Result, g.Id, homeWhite));
            }
            matches.Add(new LiveMatchSlot(m.MatchNumber, m.Home, m.Away, m.HomeScore, m.AwayScore, home, away, boards));
        }
        var rest = unassigned.OrderBy(g => BoardTagOrder(g.BoardTag)).Select(g => g.Id).ToList();
        return new LiveRoundLayout(true, matches, [], rest);
    }

    private static T? SingleOrDefaultSafe<T>(this IEnumerable<T> source, Func<T, bool> pred) where T : class
    {
        var hits = source.Where(pred).Take(2).ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    private static (decimal W, decimal B) Points(string result) => result switch
    {
        "1-0" => (1m, 0m),
        "0-1" => (0m, 1m),
        "1/2-1/2" => (0.5m, 0.5m),
        _ => (0m, 0m),
    };

    /// <summary>„1.12" → 1012 (Runde.Partie), für eine stabile Ordnung der Rückfälle.</summary>
    private static int BoardTagOrder(string? tag)
    {
        var parts = (tag ?? "").Split('.');
        return parts.Length == 2 && int.TryParse(parts[1], out var b) ? b : int.TryParse(tag, out var n) ? n : 999;
    }

    /// <summary>Name als Vergleichsschlüssel: Kleinbuchstaben ohne Akzente, Satzzeichen weg, Wörter sortiert.</summary>
    public static string NameKey(string? name)
    {
        var words = Fold(name).Split([' ', ',', '.', '-', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("dr" or "mag" or "ing" or "di" or "prof" or "dipl"))
            .Order(StringComparer.Ordinal);
        return string.Join(' ', words);
    }

    private static string TeamKey(string? team) =>
        string.Join(' ', Fold(team).Split([' ', ',', '.', '-', '/', '\''], StringSplitOptions.RemoveEmptyEntries));

    private static string Fold(string? s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Replace("ß", "ss");
    }
}
