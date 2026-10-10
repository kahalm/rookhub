using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Welche Brettpaarung einer Liga (<see cref="LeagueGame"/>) ist diese Vereinspartie? (0.678.0, Wunsch 2026-10-05: „wenn ich
/// eine Partie von meinen Spielen in die Vereins-DB kopiere, kann ich sie keiner Ligarunde zuweisen — überleg dir da was".)
/// <list type="bullet">
/// <item>Vorgeschlagen werden Paarungen, an denen mindestens einer der beiden Spieler (FIDE-ID — auch die intern hinter
/// „Schwaz") saß; die anonymisierte Seite (<see cref="LeagueClub.AnonName"/> des Vereins der Anfrage) passt zu jedem Spieler
/// des eigenen Vereins (<see cref="LeagueClub.OwnsTeam"/>).</item>
/// <item><see cref="Option.Exact"/>: beide Seiten passen in ihren Farben, und der Tag liegt höchstens <see cref="DayTolerance"/>
/// Tage neben dem Rundentermin (ohne Tag: die Saison passt zum Jahr). Genau EIN solcher Vorschlag wird vorgewählt.</item>
/// <item>Reihenfolge: genaue zuerst, dann die mit mehr passenden Seiten, dann die zeitlich nächste (ohne Tag: die jüngste).</item>
/// </list>
/// Gespeichert wird die Wahl als <see cref="LeagueClubGame.LeagueGameId"/> samt Schlüssel (<see cref="LeagueGameLinks.Set"/>); sie
/// schlägt danach jede Raterei (Paarungen der Runde, Taktik-Kapitel). Gelesen wird sie nur über <see cref="LeagueGameLinks.ResolveAsync"/>.
/// </summary>
public sealed class LeaguePairingFinder(AppDbContext db)
{
    public const int DayTolerance = 3;
    public const int MaxOptions = 8;

    /// <summary>Was über die Partie bekannt ist: je Seite Name + FIDE-ID (bei „Schwaz" die interne), dazu Tag bzw. Jahr.</summary>
    public sealed record Query(string White, string? WhiteFide, string Black, string? BlackFide, DateOnly? Date, int? Year);

    /// <param name="Open">0.740.0: ein noch leeres Brett der eigenen Begegnung (laufende Runde, chess-results hat die Aufstellung
    /// noch nicht) — <see cref="White"/>/<see cref="Black"/> sind dann die MANNSCHAFTEN, die Spieler der Partie bleiben.</param>
    public sealed record Option(int Id, string Label, string? Date, string White, string? WhiteFide, string Black, string? BlackFide,
        string Result, bool WhiteOwnClub, bool BlackOwnClub, bool Exact, string League, string Season, bool Open = false);

    private sealed record Row(LeagueGame G, string Season, string League, DateOnly? Date, string? Source = null);

    /// <summary>Heute (für die laufende Runde) — in Tests überschreibbar.</summary>
    internal Func<DateOnly> Today { get; init; } = () => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Wie weit eine „laufende" Runde zurückliegen darf, deren Bretter noch leer sind.</summary>
    public const int OpenRoundDays = 21;

    /// <summary>Vorschläge für viele Partien auf einmal (Übersicht eines PGN-Imports) — EINE Abfrage für alle.</summary>
    /// <param name="club">Der Verein der Anfrage — wer „der eigene" ist (ohne: keiner).</param>
    public async Task<List<List<Option>>> ForManyAsync(IReadOnlyList<Query> queries, CancellationToken ct, LeagueClub? club = null)
    {
        var fides = queries.SelectMany(q => new[] { q.WhiteFide, q.BlackFide }).Where(f => !string.IsNullOrEmpty(f))
            .Select(f => f!).Distinct().ToList();
        var rows = new List<Row>();
        foreach (var chunk in fides.Chunk(200))
        {
            var part = await (from g in db.LeagueGames.AsNoTracking()
                              join t in db.LeagueTournaments.AsNoTracking() on g.Tnr equals t.Tnr
                              join r in db.LeagueRounds.AsNoTracking() on new { g.Tnr, g.Round } equals new { r.Tnr, r.Round } into rs
                              from r in rs.DefaultIfEmpty()
                              where g.Forfeit == 0 && (chunk.Contains(g.HomeFide!) || chunk.Contains(g.AwayFide!))
                              select new { g, t.Season, t.League, t.Grp, Date = r == null ? null : r.Date }).ToListAsync(ct);
            rows.AddRange(part.Select(x => new Row(x.g, x.Season, string.IsNullOrEmpty(x.Grp) ? x.League : $"{x.League} {x.Grp}", x.Date)));
        }
        rows = rows.DistinctBy(r => r.G.Id).ToList();
        var (open, ownFides) = await OpenBoardsAsync(club, ct);
        return queries.Select(q => Options(q, rows, club).Concat(OpenOptions(q, open, ownFides, club)).Take(MaxOptions).ToList()).ToList();
    }

    /// <summary>
    /// Noch leere Bretter der eigenen Begegnungen in laufenden Runden (0.740.0, Wunsch 2026-10-10: „wie kann ich eine Partie der
    /// aktuellen Runde zuweisen?" — die Bretter haben dort noch keine Spieler, über die FIDE-ID fand die Suche sie nie):
    /// Rundentermin höchstens <see cref="OpenRoundDays"/> Tage her und höchstens <see cref="DayTolerance"/> Tage voraus.
    /// </summary>
    private async Task<(List<Row> Rows, HashSet<string> OwnFides)> OpenBoardsAsync(LeagueClub? club, CancellationToken ct)
    {
        if (club is null) return (new(), new());
        var today = Today();
        var earliest = today.AddDays(-OpenRoundDays);
        var latest = today.AddDays(DayTolerance);
        var part = await (from g in db.LeagueGames.AsNoTracking()
                          join t in db.LeagueTournaments.AsNoTracking() on g.Tnr equals t.Tnr
                          join r in db.LeagueRounds.AsNoTracking() on new { g.Tnr, g.Round } equals new { r.Tnr, r.Round }
                          where g.Forfeit == 0 && g.HomePlayer == null && g.AwayPlayer == null
                                && r.Date != null && r.Date >= earliest && r.Date <= latest
                          select new { g, t.Season, t.League, t.Grp, t.Source, r.Date }).ToListAsync(ct);
        var rows = part.Where(x => club.OwnsTeam(x.g.HomeTeam) || club.OwnsTeam(x.g.AwayTeam))
            .Select(x => new Row(x.g, x.Season, string.IsNullOrEmpty(x.Grp) ? x.League : $"{x.League} {x.Grp}", x.Date, x.Source))
            .DistinctBy(r => r.G.Id).ToList();
        // FIDE-IDs der eigenen Spieler dieser Ligen — so ist die eigene Seite auch erkennbar, solange sie noch nicht „Schwaz" heißt
        var tnrs = rows.Select(r => r.G.Tnr).Distinct().ToList();
        var roster = tnrs.Count == 0 ? new() : await db.LeaguePlayers.AsNoTracking()
            .Where(p => tnrs.Contains(p.Tnr) && p.FideId != null).Select(p => new { p.Team, p.FideId }).ToListAsync(ct);
        return (rows, roster.Where(p => club.OwnsTeam(p.Team)).Select(p => p.FideId!).ToHashSet());
    }

    /// <summary>Heim hat Weiß an ungeraden Brettern (chess-results/Österreich), in Bayern an geraden (Ligamanager-Regel).</summary>
    internal static bool HomeWhiteByRule(string? source, int board) =>
        LeagueRegions.Of(source) == LeagueRegions.Bayern ? board % 2 == 0 : board % 2 == 1;

    private static IEnumerable<Option> OpenOptions(Query q, List<Row> open, HashSet<string> ownFides, LeagueClub? club)
    {
        if (club is null || open.Count == 0) return Enumerable.Empty<Option>();
        // Welche Farbe hatte der eigene Verein in der Partie? Die anonymisierte Seite ist die eigene, sonst die mit der FIDE-ID
        // eines eigenen Spielers (nur wenn genau eine Seite passt).
        bool Own(string? fide) => !string.IsNullOrEmpty(fide) && ownFides.Contains(fide);
        bool? ownWhite = club.IsAnon(q.White) ? true : club.IsAnon(q.Black) ? false
            : Own(q.WhiteFide) && !Own(q.BlackFide) ? true : Own(q.BlackFide) && !Own(q.WhiteFide) ? false : null;
        var list = new List<(Option O, int Distance)>();
        foreach (var r in open)
        {
            var homeOwn = club.OwnsTeam(r.G.HomeTeam);
            var homeWhite = HomeWhiteByRule(r.Source, r.G.Board);
            var ownIsWhite = homeOwn ? homeWhite : !homeWhite;
            if (ownWhite is { } ow && ow != ownIsWhite) continue;   // andere Farbe an diesem Brett
            var inTime = q.Date is { } d && r.Date is { } rd ? Math.Abs(rd.DayNumber - d.DayNumber) <= DayTolerance
                : q.Year is { } y && InSeason(r.Season, y);
            var (wTeam, bTeam) = homeWhite ? (r.G.HomeTeam, r.G.AwayTeam) : (r.G.AwayTeam, r.G.HomeTeam);
            var date = r.Date?.ToString("dd.MM.yyyy");
            var label = $"{r.Season} · {r.League} · Runde {r.G.Round} · Brett {r.G.Board}" + (date is null ? "" : $" ({date})");
            var distance = q.Date is { } qd && r.Date is { } rdd ? Math.Abs(rdd.DayNumber - qd.DayNumber) : 0;
            list.Add((new Option(r.G.Id, label, date, wTeam, null, bTeam, null, "", club.OwnsTeam(wTeam), club.OwnsTeam(bTeam),
                ownWhite != null && inTime, r.League, r.Season, Open: true), distance));
        }
        return list.OrderByDescending(x => x.O.Exact).ThenBy(x => x.Distance).ThenBy(x => x.O.Label).Select(x => x.O);
    }

    public async Task<List<Option>> ForAsync(Query q, LeagueClub? club, CancellationToken ct) => (await ForManyAsync(new[] { q }, ct, club))[0];

    /// <summary>Eine Paarung nachschlagen (beim Speichern einer Wahl) — <c>null</c>, wenn es sie nicht (mehr) gibt.</summary>
    public async Task<Option?> ByIdAsync(int id, CancellationToken ct, LeagueClub? club = null)
    {
        var x = await (from g in db.LeagueGames.AsNoTracking()
                       join t in db.LeagueTournaments.AsNoTracking() on g.Tnr equals t.Tnr
                       join r in db.LeagueRounds.AsNoTracking() on new { g.Tnr, g.Round } equals new { r.Tnr, r.Round } into rs
                       from r in rs.DefaultIfEmpty()
                       where g.Id == id
                       select new { g, t.Season, t.League, t.Grp, Date = r == null ? null : r.Date }).FirstOrDefaultAsync(ct);
        return x is null ? null
            : ToOption(new Row(x.g, x.Season, string.IsNullOrEmpty(x.Grp) ? x.League : $"{x.League} {x.Grp}", x.Date), false, club);
    }

    /// <summary>Die vorgewählte Paarung: genau EIN genauer Vorschlag, sonst keine.</summary>
    public static int? AutoPick(IReadOnlyList<Option> options) =>
        options.Count(o => o.Exact) == 1 ? options.First(o => o.Exact).Id : null;

    /// <summary>
    /// Die Wahl an der Partie festhalten: <paramref name="id"/> <c>0</c> = keine Ligapartie, sonst die Paarung — dann gilt das
    /// Jahr des Rundentermins, und Liga + Saison stehen als Klassifizierer an der Partie (wie der nächtliche Abgleich,
    /// <see cref="LeagueClubGame.Classifier1"/>: gehen nur in die Kopie nach „Meine Partien"). Eine unbekannte Paarung
    /// ändert nichts.
    /// </summary>
    public async Task ApplyAsync(LeagueClubGame game, int id, CancellationToken ct)
    {
        if (id <= 0) { LeagueGameLinks.Set(game, null); return; }
        if (await ByIdAsync(id, ct) is not { } o
            || await db.LeagueGames.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct) is not { } row) return;
        LeagueGameLinks.Set(game, row);   // Id + Schlüssel (Tnr, Runde, Begegnung, Brett) — überlebt jedes Aktualisieren
        if (o.Date is { } d && DateOnly.TryParseExact(d, "dd.MM.yyyy", out var day)) game.Year = day.Year;
        game.Classifier1 = o.League;
        game.Classifier2 = o.Season;
    }

    /// <summary>Was über eine (gebaute) Vereinspartie bekannt ist — „Schwaz"-Seiten mit der internen FIDE-ID.</summary>
    public static Query QueryOf(LeagueClubGame g, DateOnly? date) =>
        new(g.White, g.WhiteFide ?? g.WhiteRealFide, g.Black, g.BlackFide ?? g.BlackRealFide, date, g.Year);

    private static List<Option> Options(Query q, List<Row> rows, LeagueClub? club)
    {
        var scored = new List<(Option O, int Sides, int Distance, Row R)>();
        foreach (var r in rows)
        {
            var (wName, wFide, wTeam, bName, bFide, bTeam) = Colors(r.G);
            var ws = SideScore(q.White, q.WhiteFide, wFide, wTeam, club);
            var bs = SideScore(q.Black, q.BlackFide, bFide, bTeam, club);
            // mindestens eine Seite über die FIDE-ID — in den richtigen Farben oder vertauscht (dann nie genau)
            var crossed = Same(q.WhiteFide, bFide) || Same(q.BlackFide, wFide);
            if (ws < 2 && bs < 2 && !crossed) continue;
            var inTime = q.Date is { } d && r.Date is { } rd ? Math.Abs(rd.DayNumber - d.DayNumber) <= DayTolerance
                : q.Year is { } y && InSeason(r.Season, y);
            var exact = ws > 0 && bs > 0 && inTime;
            var distance = q.Date is { } qd && r.Date is { } rdd ? Math.Abs(rdd.DayNumber - qd.DayNumber) : int.MaxValue;
            scored.Add((ToOption(r, exact, club), (ws > 0 ? 1 : 0) + (bs > 0 ? 1 : 0), distance, r));
        }
        return scored
            .OrderByDescending(s => s.O.Exact)
            .ThenByDescending(s => s.Sides)
            .ThenBy(s => s.Distance)
            .ThenByDescending(s => s.R.Date ?? DateOnly.MinValue)
            .ThenByDescending(s => s.R.G.Round)
            .Take(MaxOptions).Select(s => s.O).ToList();
    }

    /// <summary>2 = FIDE-ID gleich, 1 = anonymisierte Seite des Vereins gegen einen seiner Spieler, 0 = passt nicht.</summary>
    internal static int SideScore(string name, string? fide, string? pairingFide, string pairingTeam, LeagueClub? club)
    {
        if (!string.IsNullOrEmpty(fide)) return fide == pairingFide ? 2 : 0;
        return club is not null && club.IsAnon(name) && club.OwnsTeam(pairingTeam) ? 1 : 0;
    }

    private static bool Same(string? a, string? b) => !string.IsNullOrEmpty(a) && a == b;

    /// <summary>Saison „2026/27" umfasst die Jahre 2026 und 2027.</summary>
    internal static bool InSeason(string season, int year) =>
        int.TryParse(season.Split('/')[0], out var s) && (s == year || s + 1 == year);

    private static (string? WName, string? WFide, string WTeam, string? BName, string? BFide, string BTeam) Colors(LeagueGame g) =>
        g.HomeColor != "s"
            ? (g.HomePlayer, g.HomeFide, g.HomeTeam, g.AwayPlayer, g.AwayFide, g.AwayTeam)
            : (g.AwayPlayer, g.AwayFide, g.AwayTeam, g.HomePlayer, g.HomeFide, g.HomeTeam);

    private static Option ToOption(Row r, bool exact, LeagueClub? club)
    {
        var (wName, wFide, wTeam, bName, bFide, bTeam) = Colors(r.G);
        var date = r.Date?.ToString("dd.MM.yyyy");
        var label = $"{r.Season} · {r.League} · Runde {r.G.Round} · Brett {r.G.Board}" + (date is null ? "" : $" ({date})");
        return new Option(r.G.Id, label, date, wName ?? "?", wFide, bName ?? "?", bFide, LeagueFixtureGames.WhiteBlackResult(r.G.Result, r.G.HomeColor != "s"),
            club?.OwnsTeam(wTeam) == true, club?.OwnsTeam(bTeam) == true, exact, r.League, r.Season);
    }
}
