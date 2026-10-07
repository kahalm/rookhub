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
/// Gespeichert wird die Wahl als <see cref="LeagueClubGame.LeagueGameId"/>; sie schlägt danach jede Raterei (Paarungen der Runde,
/// Taktik-Kapitel).
/// </summary>
public sealed class LeaguePairingFinder(AppDbContext db)
{
    public const int DayTolerance = 3;
    public const int MaxOptions = 8;

    /// <summary>Was über die Partie bekannt ist: je Seite Name + FIDE-ID (bei „Schwaz" die interne), dazu Tag bzw. Jahr.</summary>
    public sealed record Query(string White, string? WhiteFide, string Black, string? BlackFide, DateOnly? Date, int? Year);

    public sealed record Option(int Id, string Label, string? Date, string White, string? WhiteFide, string Black, string? BlackFide,
        string Result, bool WhiteOwnClub, bool BlackOwnClub, bool Exact, string League, string Season);

    private sealed record Row(LeagueGame G, string Season, string League, DateOnly? Date);

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
        return queries.Select(q => Options(q, rows, club)).ToList();
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
        if (id <= 0) { game.LeagueGameId = null; return; }
        if (await ByIdAsync(id, ct) is not { } o) return;
        game.LeagueGameId = o.Id;
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
