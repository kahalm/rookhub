using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Club;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Scoresheet;
using RookHub.Api.Services.Social;
using RookHub.Api.Tests.Games;

namespace RookHub.Api.Tests.Club;

/// <summary>Signal-Königszug beim PGN-Import der Vereins-Datenbank und Reparatur gespeicherter Partien (1.022.0).</summary>
public class ClubSignalMoveRepairTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    private LeagueClubService Club() => new(_db, NullLogger<LeagueClubService>.Instance, () => Now);

    private ClubSignalMoveRepair Repair() => new(_db,
        new ClubGameCorrectionService(_db, Club(), new ScoresheetScanService(_db, null!, TestServices.SavedGames(_db), new NotificationService(_db),
            NullLogger<ScoresheetScanService>.Instance), new LeagueClubResolver(_db), NullLogger<ClubGameCorrectionService>.Instance),
        new LeagueClubResolver(_db), new ClubGameSummaries(_db), NullLogger<ClubSignalMoveRepair>.Instance);

    /// <summary>Partie 268 mit Uhren je Halbzug, wie sie aus der Übertragung hochgeladen wurde.</summary>
    private static string Pgn268WithClocks()
    {
        var sans = SignalKingMoveTests.Sans268;
        var moves = string.Join(' ', sans.Select((s, i) => (i % 2 == 0 ? $"{i / 2 + 1}. " : $"{i / 2 + 1}... ") + s
            + $" {{ [%clk 0:{30 - i / 4 % 30:00}:{i % 60:00}] }}"));
        return "[Event \"Liga\"]\n[Date \"2026.11.22\"]\n[White \"Weber, Willi\"]\n[Black \"Huber, Sepp\"]\n[Result \"1-0\"]\n\n" + moves + " 1-0\n";
    }

    [Fact]
    public async Task ImportPgn_Game268_StoresItWithoutTheSignal_AndKeepsTheClocks()
    {
        await TestClubs.SeedAsync(_db);
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = "Testdorf", Name = "Weber, Willi", NameKey = LeagueNames.NameKey("Weber, Willi"), FideId = "900" });
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = "Absam", Name = "Huber, Sepp", NameKey = LeagueNames.NameKey("Huber, Sepp"), FideId = "901" });
        await _db.SaveChangesAsync();
        var r = await Club().ImportPgnAsync(TestClubs.Home, null, Pgn268WithClocks(), null);
        var id = Assert.Single(r.Ids);
        var g = await _db.LeagueClubGames.SingleAsync(x => x.Id == id);
        Assert.Equal(88, g.Plies);
        var sans = ClubPgnText.SansOf(g.Pgn);
        Assert.Equal(88, sans.Count);
        Assert.Equal("Qc3", sans[^1]);
        Assert.Equal(LeagueClubService.HashOf(SavedGameService.LegalSans(sans.ToList())), g.MovesHash);
        Assert.Equal(88, ClubPgnText.ClocksOf(g.Pgn).Count(c => c != null));
    }

    private async Task<LeagueClubGame> Stored268Async()
    {
        await TestClubs.SeedAsync(_db);
        var sans = SignalKingMoveTests.Sans268.ToList();
        var g = new LeagueClubGame { ClubId = TestClubs.HomeId, White = "Weber, Willi", Black = "Huber, Sepp", Result = "1-0", Year = 2026,
            Plies = sans.Count, MovesHash = LeagueClubService.HashOf(sans), CreatedAt = Now };
        g.Pgn = ClubPgnText.PgnOf(g, sans);
        _db.LeagueClubGames.Add(g);
        _db.LeagueClubGames.Add(new LeagueClubGame { ClubId = TestClubs.HomeId, White = "A", Black = "B", Result = "1-0", Year = 2026, Plies = 1,
            MovesHash = "x", Pgn = "[Result \"1-0\"]\n\n1. e4 1-0\n", CreatedAt = Now });
        await _db.SaveChangesAsync();
        _db.ClubGameSummaries.Add(new ClubGameSummary { ClubGameId = g.Id, Status = ClubGameSummaryStatus.Done, Markdown = "45.Ke4?? …",
            MovesHash = g.MovesHash, RequestedAt = Now });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return g;
    }

    [Fact]
    public async Task Repair_DryRun_ListsGame268_AndChangesNothing()
    {
        var g = await Stored268Async();
        var r = await Repair().RunAsync(1, dryRun: true);
        Assert.True(r.DryRun);
        Assert.Equal(2, r.Scanned);
        var item = Assert.Single(r.Games);
        Assert.Equal((g.Id, "45.Ke4", false), (item.ClubGameId, item.Move, item.Applied));
        Assert.Equal(89, (await _db.LeagueClubGames.SingleAsync(x => x.Id == g.Id)).Plies);
        Assert.Equal(ClubGameSummaryStatus.Done, (await _db.ClubGameSummaries.SingleAsync()).Status);
    }

    [Fact]
    public async Task Repair_Run_DropsTheSignal_RecomputesHash_AndRequestsTheSummaryAgain()
    {
        var g = await Stored268Async();
        var r = await Repair().RunAsync(1, dryRun: false);
        var item = Assert.Single(r.Games);
        Assert.True(item.Applied);
        Assert.True(item.SummaryRequested);
        var after = await _db.LeagueClubGames.AsNoTracking().SingleAsync(x => x.Id == g.Id);
        Assert.Equal(88, after.Plies);
        Assert.Equal(LeagueClubService.HashOf(SavedGameService.LegalSans(ClubPgnText.SansOf(after.Pgn).ToList())), after.MovesHash);
        Assert.NotEqual(g.MovesHash, after.MovesHash);
        Assert.Equal(ClubGameSummaryStatus.Requested, (await _db.ClubGameSummaries.AsNoTracking().SingleAsync()).Status);
        // Ein zweiter Lauf findet nichts mehr — nie mehr als ein Halbzug.
        Assert.Empty((await Repair().RunAsync(1, dryRun: false)).Games);
    }
}
