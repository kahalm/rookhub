using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Aufstellungen je Runde + erste Züge je Partie (2026-10-08, Wunsch: „für jede Runde einen Knopf, der mir alle Aufstellungen
/// dieser Runde anzeigt — dann bei jeder Partie die Möglichkeit, die ersten paar Züge einzugeben").
/// <list type="bullet">
/// <item><see cref="LineupsAsync"/>: alle Begegnungen einer Runde samt Brettpaarungen aus <see cref="LeagueGame"/> (öffentliche
/// Ligadaten) und den hinterlegten Zügen; eine Begegnung ohne Brettpaarungen = „noch keine Aufstellung".</item>
/// <item>Je Brett zusätzlich die PARTIE, wo es eine gibt (0.724.0, Wunsch 2026-10-08: „wenn ich das hab, dann sollt er nicht Züge
/// eingeben lassen, sondern die Partie ausweisen") — dieselbe Regel wie die Paarungen gespielter Runden
/// (<see cref="LeagueFixtureGames.ForGamesAsync"/>: feste Zuordnung, Raten, Spielerkarten), EIN Aufruf für die ganze Runde. Die
/// Aufstellung trägt davon nur Quelle, Halbzüge, Ergebnis, Namen und die ersten <see cref="FirstPlies"/> Halbzüge — das PGN holt
/// die Oberfläche über <c>…/round/{r}/games</c> bzw. <c>club/games/{id}</c>. Mit Partie keine Zug-Eingabe mehr
/// (<c>canEditMoves = false</c>); ein alter Eintrag kommt weiter mit (<c>moves</c>), darf gelöscht, aber nicht geändert werden.</item>
/// <item><see cref="SaveAsync"/>: Züge einer Paarung speichern/löschen, Schlüssel (Tnr, Runde, Begegnung, Brett).</item>
/// </list>
/// Schreiben (Regel wie bei den Vereinspartien — beitragen darf, wer <c>league.contribute</c> hat; ändern/löschen der Verwalter
/// oder wer sie eingetragen hat): ohne <c>league.manage</c> nur an Begegnungen mit einer Mannschaft des eigenen Vereins
/// (<see cref="LeagueClub.OwnsTeam"/>) und nur, solange kein anderer die Züge eingetragen hat.
/// </summary>
public sealed partial class LeagueGameMoves(AppDbContext db, Func<DateTime>? now = null, LeagueFixtureGames? fixtures = null)
{
    /// <summary>So viele Halbzüge der vorhandenen Partie zeigt die Aufstellung („1.e4 c5 2.Sf3 …").</summary>
    public const int FirstPlies = 10;

    private readonly LeagueFixtureGames _fixtures = fixtures ?? new LeagueFixtureGames(db);

    /// <summary>Höchstens so viele Halbzüge — es geht um „die ersten paar Züge", nicht um die Partie.</summary>
    public const int MaxPlies = 60;

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);

    public sealed record LineupBoard(int Board, string? HomePlayer, string? HomeTitle, int? HomeElo, string? AwayPlayer,
        string? AwayTitle, int? AwayElo, string? HomeColor, string Result, int Forfeit, string? Moves, bool CanEditMoves,
        LineupGame? Game = null, bool CanDeleteMoves = false);

    /// <summary>Die vorhandene Partie eines Bretts (ohne PGN): <c>source</c> <c>club</c> (Vereinspartie, <c>clubGameId</c>) oder
    /// <c>profile</c> (Spielerkarte), Halbzüge, Ergebnis und Namen wie im PGN, die ersten <see cref="FirstPlies"/> Halbzüge
    /// (englische SAN), <c>canEdit</c> = darf die Vereinspartie bearbeiten (Verwalter oder Hochladender).</summary>
    public sealed record LineupGame(string Source, int? ClubGameId, int Plies, string Result, string? White, string? Black,
        List<string> FirstMoves, bool CanEdit);

    public sealed record LineupMatch(int? MatchNo, string Home, string Away, double? HomePts, double? AwayPts, bool Own,
        List<LineupBoard> Boards);

    public sealed record Lineups(int Tnr, int Round, string? Date, bool CanEdit, List<LineupMatch> Matches);

    /// <summary>Ergebnis der Zugprüfung: <see cref="Sans"/> (englische SAN) oder ein Grund (<c>tooLong</c>, <c>illegal</c> samt
    /// erstem falschen Zug wie geschrieben und seinem Halbzug, ab 1).</summary>
    public sealed record Parsed(List<string>? Sans, string? Reason = null, string? Move = null, int? Ply = null);

    /// <summary>Ausgang von <see cref="SaveAsync"/>: <c>null</c> = gespeichert (<see cref="Moves"/> leer = gelöscht), sonst
    /// <c>notFound</c>, <c>foreignMatch</c>/<c>notYours</c> (403), <c>noGame</c>, <c>tooLong</c>, <c>illegal</c> (400).</summary>
    public sealed record SaveResult(string? Reason, string? Moves = null, string? Move = null, int? Ply = null);

    /// <summary>Alle Begegnungen der Runde samt Aufstellung und Zügen. <c>null</c> = die Runde gibt es in der Liga nicht.</summary>
    /// <param name="canContribute"><c>league.contribute</c> (live) — ohne darf er nur lesen.</param>
    /// <param name="canManage"><c>league.manage</c> (live) — Verwalter dürfen jede Paarung und fremde Einträge ändern.</param>
    public async Task<Lineups?> LineupsAsync(LeagueClub club, int tnr, int round, int userId, bool canContribute, bool canManage,
        CancellationToken ct = default)
    {
        var matches = await db.LeagueMatches.AsNoTracking().Where(m => m.Tnr == tnr && m.Round == round)
            .OrderBy(m => m.MatchNo).ThenBy(m => m.Id).ToListAsync(ct);
        var games = await db.LeagueGames.AsNoTracking().Where(g => g.Tnr == tnr && g.Round == round)
            .OrderBy(g => g.MatchNo).ThenBy(g => g.Board).ThenBy(g => g.Id).ToListAsync(ct);
        var roundRow = await db.LeagueRounds.AsNoTracking().Where(r => r.Tnr == tnr && r.Round == round).FirstOrDefaultAsync(ct);
        if (matches.Count == 0 && games.Count == 0 && roundRow is null) return null;
        var moves = await db.LeagueGameMoves.AsNoTracking().Where(m => m.Tnr == tnr && m.Round == round).ToListAsync(ct);
        var byKey = moves.GroupBy(m => (m.MatchNo, m.Board)).ToDictionary(x => x.Key, x => x.First());
        // Partien der ganzen Runde in EINEM Durchgang; doppelte Zeilen desselben Bretts zählen zusammen (die Zuordnung einer
        // Vereinspartie kann auf jede von ihnen zeigen).
        var found = await _fixtures.ForGamesAsync(club, tnr, round, games, ct, userId, canManage);
        var gameByKey = games.Where(g => found.TryGetValue(g.Id, out var p) && p.Source is not null && p.Pgn is not null)
            .GroupBy(g => (g.MatchNo, g.Board))
            .ToDictionary(x => x.Key, x => ToGame(found[x.OrderBy(g => g.Id).First().Id]));

        // Brettpaarungen je Begegnung: zuerst über die Nummer der Begegnung, sonst über die Mannschaftsnamen (eine Quelle
        // ohne Nummer im Spielplan). Eine doppelte Zeile (gleiches Brett zweimal) zählt einmal — die kleinste Id.
        var groups = games.GroupBy(g => g.MatchNo)
            .ToDictionary(x => x.Key, x => x.GroupBy(g => g.Board).Select(b => b.First()).ToList());
        var used = new HashSet<int>();
        var result = new List<LineupMatch>();
        foreach (var m in matches)
        {
            List<LeagueGame>? boards = null;
            if (m.MatchNo is { } no && groups.TryGetValue(no, out var byNo) && !used.Contains(no)
                && byNo.Any(g => g.HomeTeam == m.Home && g.AwayTeam == m.Away || g.HomeTeam == m.Away && g.AwayTeam == m.Home))
                boards = byNo;
            boards ??= groups.Where(x => !used.Contains(x.Key))
                .Select(x => x.Value).FirstOrDefault(x => x.Any(g => g.HomeTeam == m.Home && g.AwayTeam == m.Away));
            if (boards is not null) used.Add(boards[0].MatchNo);
            result.Add(Match(m.MatchNo ?? boards?[0].MatchNo, m.Home, m.Away, m.HomePts, m.AwayPts, boards ?? new()));
        }
        // Brettpaarungen ohne Begegnung im Spielplan: trotzdem zeigen.
        foreach (var (no, boards) in groups.Where(x => !used.Contains(x.Key)).OrderBy(x => x.Key))
            result.Add(Match(no, boards[0].HomeTeam, boards[0].AwayTeam, null, null, boards));
        return new Lineups(tnr, round, roundRow?.Date?.ToString("yyyy-MM-dd"), canContribute, result);

        LineupMatch Match(int? matchNo, string home, string away, double? hp, double? ap, List<LeagueGame> boards)
        {
            var own = club.OwnsTeam(home) || club.OwnsTeam(away);
            var mayWrite = canContribute && (canManage || own);
            return new LineupMatch(matchNo, home, away, hp, ap, own, boards.Select(g =>
            {
                var entry = byKey.GetValueOrDefault((g.MatchNo, g.Board));
                var game = gameByKey.GetValueOrDefault((g.MatchNo, g.Board));
                var editable = game is null && mayWrite && Playable(g) && (entry is null || canManage || entry.UpdatedByUserId == userId);
                // Löschen bleibt auch neben einer Partie möglich (ein alter, nun ersetzter Handeintrag) — Regel wie Speichern.
                var deletable = entry is not null && mayWrite && (canManage || entry.UpdatedByUserId == userId);
                return new LineupBoard(g.Board, g.HomePlayer, g.HomeTitle, g.HomeElo, g.AwayPlayer, g.AwayTitle, g.AwayElo,
                    g.HomeColor, g.Result, g.Forfeit, entry?.Moves, editable, game, deletable);
            }).ToList());
        }
    }

    /// <summary>Die Partie eines Bretts ohne PGN: Kopfzeilen und Hauptvariante (bereinigt wie überall).</summary>
    internal static LineupGame ToGame(LeagueFixtureGames.Pairing p)
    {
        var (headers, moveText) = PgnParser.SplitGames(p.Pgn!).FirstOrDefault();
        headers ??= new();
        var sans = PgnParser.ExtractMainlineSans(moveText ?? "");
        var result = headers.GetValueOrDefault("Result") is { Length: > 0 } r && r != "*" ? r : p.Result.Replace(" ", "");
        return new LineupGame(p.Source!, p.ClubGameId, sans.Count, result, headers.GetValueOrDefault("White") ?? p.White,
            headers.GetValueOrDefault("Black") ?? p.Black, sans.Take(FirstPlies).ToList(), p.CanEdit);
    }

    /// <summary>Beide Bretter besetzt und nicht kampflos — sonst gibt es keine Partie, deren Züge man eintragen könnte.</summary>
    private static bool Playable(LeagueGame g) => g.Forfeit == 0 && g.HomePlayer is not null && g.AwayPlayer is not null;

    /// <summary>Züge einer Paarung speichern; leere Züge löschen den Eintrag.</summary>
    public async Task<SaveResult> SaveAsync(LeagueClub club, int userId, bool canManage, int tnr, int round, int matchNo, int board,
        string? text, CancellationToken ct = default)
    {
        var game = await db.LeagueGames.AsNoTracking()
            .Where(g => g.Tnr == tnr && g.Round == round && g.MatchNo == matchNo && g.Board == board)
            .OrderBy(g => g.Id).FirstOrDefaultAsync(ct);
        if (game is null) return new("notFound");
        if (!canManage && !club.OwnsTeam(game.HomeTeam) && !club.OwnsTeam(game.AwayTeam)) return new("foreignMatch");
        var entry = await db.LeagueGameMoves
            .FirstOrDefaultAsync(m => m.Tnr == tnr && m.Round == round && m.MatchNo == matchNo && m.Board == board, ct);
        if (entry is not null && !canManage && entry.UpdatedByUserId != userId) return new("notYours");

        var parsed = Parse(text);
        if (parsed.Sans is null) return new(parsed.Reason, null, parsed.Move, parsed.Ply);
        if (parsed.Sans.Count == 0)
        {
            if (entry is not null)
            {
                db.LeagueGameMoves.Remove(entry);
                await db.SaveChangesAsync(ct);
                await RefreshCardsAsync(game, ct);
            }
            return new(null, null);
        }
        if (!Playable(game)) return new("noGame");

        var joined = string.Join(' ', parsed.Sans);
        if (entry is null)
        {
            entry = new LeagueGameMove { Tnr = tnr, Round = round, MatchNo = matchNo, Board = board };
            db.LeagueGameMoves.Add(entry);
        }
        entry.Moves = joined;
        entry.ClubId = club.Id;
        entry.UpdatedByUserId = userId;
        entry.UpdatedAt = _now();
        await db.SaveChangesAsync(ct);
        await RefreshCardsAsync(game, ct);
        return new(null, joined);
    }

    /// <summary>
    /// Die Züge sind eine Teilpartie beider Spieler (<see cref="LeaguePartialGames"/>, 0.725.0): nach jedem Speichern/Löschen ihre
    /// Karten neu rechnen und die Partienzahl <c>g</c> in den fertigen Ansichten nachziehen — derselbe Weg wie nach einem
    /// Vereinspartien-Upload (<c>LeagueClubService.RefreshCardsAsync</c>), synchron wie dort (zwei Karten).
    /// </summary>
    private async Task RefreshCardsAsync(LeagueGame game, CancellationToken ct)
    {
        var ids = new[] { game.HomeFide, game.AwayFide }.Where(f => !string.IsNullOrEmpty(f)).Select(f => f!)
            .Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return;
        var store = new LeagueProfileStore(db);
        var now = _now();
        foreach (var f in ids) await store.RebuildAsync(f, ct, now: now);
        await db.SaveChangesAsync(ct);
        await store.PatchViewCountsAsync(ids, ct);
    }

    [GeneratedRegex(@"\{[^}]*\}|\([^)]*\)|\$\d+")]
    private static partial Regex Annotations();

    [GeneratedRegex(@"\b\d+\s*(?:\.+|…)")]
    private static partial Regex MoveNumbers();

    [GeneratedRegex(@"^(?:1-0|0-1|1/2-1/2|½-½|\*|\.+|…)$")]
    private static partial Regex NotAMove();

    /// <summary>
    /// Getippte oder eingefügte Züge → englische SAN ab der Grundstellung. Nimmt Zugnummern („1.e4 c5 2.Nf3", „1…c5"),
    /// Kommentare/Varianten in Klammern (fallen weg), ein Ergebnis am Ende, deutsche Figurenbuchstaben (S, L, T, D — auch bei
    /// der Umwandlung) und „0-0". Geprüft mit derselben Zugprüfung wie die Vereinspartien (<see cref="SavedGameService.LegalSans"/>).
    /// </summary>
    public static Parsed Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(new());
        var cleaned = MoveNumbers().Replace(Annotations().Replace(text, " "), " ");
        var tokens = cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !NotAMove().IsMatch(t)).ToList();
        if (tokens.Count == 0) return new(new());
        if (tokens.Count > MaxPlies) return new(null, "tooLong");
        var english = tokens.Select(English).ToList();
        try
        {
            return new(SavedGameService.LegalSans(english));
        }
        catch (ArgumentException)
        {
            // Den ersten falschen Zug finden — höchstens MaxPlies kurze Durchläufe.
            for (var i = 1; i <= english.Count; i++)
            {
                try { SavedGameService.LegalSans(english.Take(i).ToList()); }
                catch (ArgumentException) { return new(null, "illegal", tokens[i - 1], i); }
            }
            return new(null, "illegal", tokens[^1], tokens.Count);
        }
    }

    private static readonly Dictionary<char, char> GermanPieces = new() { ['S'] = 'N', ['L'] = 'B', ['T'] = 'R', ['D'] = 'Q' };

    /// <summary>Deutsche Figurenbuchstaben → englische. Englische SAN beginnt nie mit S/L/T/D, ein „B" bleibt also Läufer.</summary>
    internal static string English(string token)
    {
        if (token.Length == 0) return token;
        var chars = token.ToCharArray();
        if (GermanPieces.TryGetValue(chars[0], out var first)) chars[0] = first;
        // Umwandlung „e8=D", „e8D", „exd8=S+" — der Figurenbuchstabe hinter der Grundreihe
        for (var i = 1; i < chars.Length; i++)
        {
            if (GermanPieces.TryGetValue(chars[i], out var p) && (chars[i - 1] == '=' || chars[i - 1] is '1' or '8'))
                chars[i] = p;
        }
        return new string(chars);
    }
}
