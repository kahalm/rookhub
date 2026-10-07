using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Tactics;

namespace RookHub.Api.Tests;

/// <summary>
/// Die feste Ligapaarung einer Vereinspartie überlebt jedes Aktualisieren (0.716.1, gemeldet 2026-10-07: „gefühlt zum dritten
/// Mal zugewiesen — die Zuordnung verschwindet immer wieder", Landesliga Runde 2, Bretter 2/4/5). Vorher legte
/// <see cref="LeagueRefresh.ReplaceAsync(AppDbContext, LeagueRefresh.Pages, DateTime, CancellationToken)"/> alle Brettpaarungen
/// einer Liga neu an, jede Zuordnung zeigte ins Leere, und die Partie verschwand ganz aus den Paarungen der Runde.
/// </summary>
public class LeagueGameLinksTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    private const int Tnr = 7;

    /// <summary>Landesliga 2026/27 Runde 2 (04.10.): Begegnung 1 Andere–Fremde, Begegnung 2 Absam–Testdorf (Bretter 1–3).
    /// <paramref name="board3Result"/> ändert ein Ergebnis, <paramref name="withBoard3"/> lässt ein Brett verschwinden.</summary>
    private static LeagueRefresh.Pages Pages(string board3Result = "½ - ½", bool withBoard3 = true)
    {
        var games = new List<LeagueRefresh.GameRow>
        {
            new(2, 1, 1, "Andere", "Fremde", "Xaver, A", "Yvonne, B", null, null, "w", "1 - 0", 1, 0, 0, null),
            new(2, 1, 2, "Andere", "Fremde", "Xaver, C", "Yvonne, D", null, null, "s", "0 - 1", 0, 1, 0, null),
            new(2, 2, 1, "Absam", "Testdorf", "Hengl, Philip", "Ranner, Stefan", null, null, "w", "1 - 0", 1, 0, 0, null),
            new(2, 2, 2, "Absam", "Testdorf", "Haselbeck, Franz", "Gruber, Michael", null, null, "w", "½ - ½", .5, .5, 0, null),
        };
        if (withBoard3)
            games.Add(new(2, 2, 3, "Absam", "Testdorf", "Schnabl, Andreas", "Kostic, Mira", null, null, "w", board3Result, .5, .5, 0, null));
        return new(Tnr,
            new() { new(2, 1, "Andere", "Fremde", 1, 1, "04.10.2026", null, null), new(2, 2, "Absam", "Testdorf", 1.5, 1.5, "04.10.2026", null, null) },
            games,
            new() { [2] = "04.10.2026" },
            new()
            {
                new(1, null, "Hengl, Philip", "222", 2100, null, "AUT", "Absam", 1),
                new(2, null, "Haselbeck, Franz", "1270356", 1900, null, "AUT", "Absam", 2),
                new(3, null, "Schnabl, Andreas", "333", 1800, null, "AUT", "Absam", 3),
                new(4, null, "Ranner, Stefan", "1615130", 2000, null, "AUT", "Testdorf", 1),
                new(5, null, "Gruber, Michael", "1611143", 1950, null, "AUT", "Testdorf", 2),
                new(6, null, "Kostic, Mira", "444", 1700, null, "AUT", "Testdorf", 3),
                new(7, null, "Xaver, A", null, null, null, "AUT", "Andere", 1),
            },
            new());
    }

    private async Task SeedAsync()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = Tnr, Season = "2026/27", League = "Landesliga", Stage = "Liga" });
        await _db.SaveChangesAsync();
        await LeagueRefresh.ReplaceAsync(_db, Pages(), Now, default);
        _db.ChangeTracker.Clear();
    }

    private Task<LeagueGame> BoardAsync(int match, int board) =>
        _db.LeagueGames.AsNoTracking().SingleAsync(g => g.Tnr == Tnr && g.Round == 2 && g.MatchNo == match && g.Board == board);

    /// <summary>Haselbeck (Weiß, Absam) gegen „Testdorf" hinter dem Ranner steckt — so liegt Partie 167 auf Prod.</summary>
    private static LeagueClubGame Haselbeck(int? leagueGameId = null) => new()
    {
        ClubId = TestClubs.HomeId, Year = 2026, White = "Haselbeck, Franz", WhiteFide = "1270356", Black = "Testdorf",
        BlackRealFide = "1611143", Result = "1/2-1/2", Pgn = "[White \"Haselbeck, Franz\"]\n[Black \"Testdorf\"]\n\n1. d4 d5 1/2-1/2",
        MovesHash = "h", LeagueGameId = leagueGameId,
    };

    [Fact]
    public async Task Replace_keepsTheIdPerRoundMatchBoard_updatesFields_dropsVanished()
    {
        await SeedAsync();
        var before = (await _db.LeagueGames.AsNoTracking().Where(g => g.Tnr == Tnr).ToListAsync()).ToDictionary(g => (g.MatchNo, g.Board), g => g.Id);

        await LeagueRefresh.ReplaceAsync(_db, Pages(board3Result: "1 - 0"), Now, default);
        _db.ChangeTracker.Clear();
        var after = (await _db.LeagueGames.AsNoTracking().Where(g => g.Tnr == Tnr).ToListAsync()).ToDictionary(g => (g.MatchNo, g.Board), g => g);

        Assert.Equal(before.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k));
        foreach (var (key, id) in before) Assert.Equal(id, after[key].Id);
        Assert.Equal("1 - 0", after[(2, 3)].Result);                 // Werte kommen trotzdem neu
        Assert.Equal("1270356", after[(2, 2)].HomeFide);

        await LeagueRefresh.ReplaceAsync(_db, Pages(withBoard3: false), Now, default);
        _db.ChangeTracker.Clear();
        var goneId = before[(2, 3)];
        Assert.False(await _db.LeagueGames.AnyAsync(g => g.Id == goneId));
        Assert.Equal(before[(2, 2)], (await BoardAsync(2, 2)).Id);
    }

    [Fact]
    public async Task Assignment_survivesARefresh_andTheGameStaysInThePairings()
    {
        await SeedAsync();
        var board2 = await BoardAsync(2, 2);
        var game = Haselbeck();
        _db.LeagueClubGames.Add(game);
        await new LeaguePairingFinder(_db).ApplyAsync(game, board2.Id, default);
        await _db.SaveChangesAsync();
        Assert.Equal((Tnr, 2, 2, 2), (game.LeagueTnr, game.LeagueRound, game.LeagueMatchNo, game.LeagueBoard));
        _db.ChangeTracker.Clear();

        await LeagueRefresh.ReplaceAsync(_db, Pages(), Now, default);   // „Daten aktualisieren" mit denselben Paarungen
        _db.ChangeTracker.Clear();

        Assert.Equal(board2.Id, (await _db.LeagueClubGames.SingleAsync()).LeagueGameId);
        var list = await new LeagueFixtureGames(_db).ForFixtureAsync(TestClubs.Home, Tnr, 2, "Testdorf", default);
        var p = list.Single(x => x.Board == 2);
        Assert.Equal(("club", game.Id), (p.Source, p.ClubGameId!.Value));
    }

    [Fact]
    public async Task DeadIdWithKey_isResolvedOverTheKey_andRelinkRepairsIt()
    {
        await SeedAsync();
        var board2 = await BoardAsync(2, 2);
        var game = Haselbeck(leagueGameId: 999_999);
        (game.LeagueTnr, game.LeagueRound, game.LeagueMatchNo, game.LeagueBoard) = (Tnr, 2, 2, 2);
        _db.LeagueClubGames.Add(game);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var p = (await new LeagueFixtureGames(_db).ForFixtureAsync(TestClubs.Home, Tnr, 2, "Testdorf", default)).Single(x => x.Board == 2);
        Assert.Equal(game.Id, p.ClubGameId);

        Assert.Equal(1, await LeagueGameLinks.RelinkAsync(_db, Tnr, default));
        Assert.Equal(board2.Id, (await _db.LeagueClubGames.AsNoTracking().SingleAsync()).LeagueGameId);
    }

    [Fact]
    public async Task DeadIdWithoutKey_isTreatedAsUnassigned_theGameIsGuessedInsteadOfVanishing()
    {
        await SeedAsync();
        _db.LeagueClubGames.Add(Haselbeck(leagueGameId: 18_723));   // so lag Partie 167 auf Prod
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var log = new CapturingLogger<LeagueFixtureGames>();

        var p = (await new LeagueFixtureGames(_db, log).ForFixtureAsync(TestClubs.Home, Tnr, 2, "Testdorf", default)).Single(x => x.Board == 2);

        Assert.Equal("club", p.Source);
        Assert.Contains(log.Events, e => e.Message.Contains("18723"));
    }

    [Fact]
    public async Task VanishedPairing_idRests_keyStays_comesBackWithThePairing()
    {
        await SeedAsync();
        var board3 = await BoardAsync(2, 3);
        var game = new LeagueClubGame { ClubId = TestClubs.HomeId, Year = 2026, White = "Schnabl, Andreas", WhiteFide = "333",
            Black = "Kostic, Mira", BlackFide = "444", Pgn = "x", MovesHash = "k" };
        LeagueGameLinks.Set(game, board3);
        _db.LeagueClubGames.Add(game);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await LeagueRefresh.ReplaceAsync(_db, Pages(withBoard3: false), Now, default);
        _db.ChangeTracker.Clear();
        var rested = await _db.LeagueClubGames.AsNoTracking().SingleAsync();
        Assert.Null(rested.LeagueGameId);
        Assert.Equal(3, rested.LeagueBoard);

        await LeagueRefresh.ReplaceAsync(_db, Pages(), Now, default);
        _db.ChangeTracker.Clear();
        var back = await BoardAsync(2, 3);
        Assert.Equal(back.Id, (await _db.LeagueClubGames.AsNoTracking().SingleAsync()).LeagueGameId);
    }

    [Fact]
    public async Task Heal_fillsTheKey_repairsAUniqueDeadId_clearsAnAmbiguousOne()
    {
        await TestClubs.SeedAsync(_db);
        await SeedAsync();
        var board1 = await BoardAsync(2, 1);
        var board2 = await BoardAsync(2, 2);
        var valid = Haselbeck(leagueGameId: board1.Id);                  // (b) gültig, ohne Schlüssel (vor 0.716.1)
        valid.White = "Hengl, Philip"; valid.WhiteFide = "222"; valid.BlackRealFide = "1615130"; valid.MovesHash = "v";
        var dead = Haselbeck(leagueGameId: 18_723);                       // (a) tot, genau ein genauer Treffer: Brett 2
        var unclear = new LeagueClubGame                                  // (a) tot, ohne passende Paarung → leeren
        {
            ClubId = TestClubs.HomeId, Year = 2026, White = "Niemand, N", WhiteFide = "555", Black = "Testdorf", Pgn = "u",
            MovesHash = "u", LeagueGameId = 18_724,
        };
        _db.LeagueClubGames.AddRange(valid, dead, unclear);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var r = await LeagueGameLinks.HealAsync(_db, default, NullLogger.Instance);

        Assert.Equal((1, 1, 1), (r.Keyed, r.Repaired, r.Cleared));
        var rows = await _db.LeagueClubGames.AsNoTracking().ToDictionaryAsync(g => g.MovesHash);
        Assert.Equal((board1.Id, 2, 1), (rows["v"].LeagueGameId!.Value, rows["v"].LeagueMatchNo!.Value, rows["v"].LeagueBoard!.Value));
        Assert.Equal((board2.Id, 2, 2), (rows["h"].LeagueGameId!.Value, rows["h"].LeagueMatchNo!.Value, rows["h"].LeagueBoard!.Value));
        Assert.Null(rows["u"].LeagueGameId);
        Assert.Null(rows["u"].LeagueTnr);

        var again = await LeagueGameLinks.HealAsync(_db, default);       // idempotent
        Assert.Equal((0, 0, 0, 0), (again.Keyed, again.Relinked, again.Repaired, again.Cleared));
    }

    [Fact]
    public async Task TacticChapter_withADeadLink_resolvesOverTheKey_orFallsBackToGuessing()
    {
        await SeedAsync();
        var keyed = Haselbeck(leagueGameId: 999_999);
        (keyed.LeagueTnr, keyed.LeagueRound, keyed.LeagueMatchNo, keyed.LeagueBoard) = (Tnr, 2, 2, 2);
        Assert.Equal("2026/27 · Landesliga · Runde 2", await TacticHarvestService.LeagueRoundChapterAsync(_db, keyed, TestClubs.Home, default));

        var dead = Haselbeck(leagueGameId: 999_999);                      // ohne Schlüssel: geraten über Spieler + Saison
        Assert.Equal("2026/27 · Landesliga · Runde 2", await TacticHarvestService.LeagueRoundChapterAsync(_db, dead, TestClubs.Home, default));
    }

    [Fact]
    public async Task BundleImport_keepsTheIds_andRelinks()
    {
        await SeedAsync();
        var board2 = await BoardAsync(2, 2);
        var game = Haselbeck(leagueGameId: board2.Id);
        LeagueGameLinks.Set(game, board2);
        _db.LeagueClubGames.Add(game);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var games = await _db.LeagueGames.AsNoTracking().ToListAsync();
        var bundle = new LeagueImportService.Bundle(
            new() { new(Tnr, "LL", "2026/27", 1, "Landesliga", null, "Liga", false, null, null, 9) },
            new() { new(Tnr, 2, "04.10.2026") }, new(),
            games.Select(g => new LeagueImportService.GameIn(g.Tnr, g.Round, g.MatchNo, g.Board, g.HomeTeam, g.AwayTeam, g.HomePlayer,
                g.AwayPlayer, null, null, g.HomeColor, g.Result, g.HomeScore, g.AwayScore, g.Forfeit, g.HomeFide, g.AwayFide, null, null,
                null, null, null)).ToList(),
            new(), null, null);
        await new LeagueImportService(_db).ImportAsync(bundle, default);
        _db.ChangeTracker.Clear();

        Assert.Equal(board2.Id, (await BoardAsync(2, 2)).Id);
        Assert.Equal(board2.Id, (await _db.LeagueClubGames.AsNoTracking().SingleAsync()).LeagueGameId);
    }
}
