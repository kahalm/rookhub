using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using Xunit;

namespace RookHub.Api.Tests.League;

/// <summary>0.913.0, Wunsch 2026-11-08: „auf Wunsch vom Admin soll Claude auch eine Zusammenfassung der Partie machen — eine
/// eigene Webseite, bei der Partie verlinkt". Anfordern, Warteschlange des Watchers, Abliefern, Fehlschlag, Korrektur danach.</summary>
public class ClubGameSummariesTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 8, 20, 0, 0, TimeSpan.Zero));
    private ClubGameSummaries Svc => new(_db, _clock);

    public void Dispose() => _db.Dispose();

    private async Task<int> GameAsync(int clubId = TestClubs.HomeId, string hash = "h1")
    {
        await TestClubs.SeedAsync(_db);
        var g = new LeagueClubGame { ClubId = clubId, White = "Testdorf", Black = "Gast, Max", Result = "1-0", Pgn = "1. e4 e5 1-0", MovesHash = hash };
        _db.LeagueClubGames.Add(g);
        await _db.SaveChangesAsync();
        return g.Id;
    }

    [Fact]
    public async Task Get_WithoutRequest_IsNone_ForeignGameIsNull()
    {
        var id = await GameAsync();
        var other = await GameAsync(TestClubs.OtherId);
        Assert.Equal("none", (await Svc.GetAsync(TestClubs.Home, id))!.Status);
        Assert.Null(await Svc.GetAsync(TestClubs.Home, other));
        Assert.Null(await Svc.RequestAsync(TestClubs.Home, other, 7));
    }

    [Fact]
    public async Task Request_Start_Complete_ShowsMarkdown_AndLeavesTheQueue()
    {
        var id = await GameAsync();
        Assert.Equal("requested", (await Svc.RequestAsync(TestClubs.Home, id, 7))!.Status);
        var job = Assert.Single(await Svc.QueueAsync());
        Assert.Equal((id, TestClubs.HomeId, 7), (job.ClubGameId, job.ClubId, job.RequestedByUserId));

        Assert.Equal(ClubGameSummaries.Outcome.Ok, await Svc.StartAsync(id));
        Assert.Equal(ClubGameSummaries.Outcome.Conflict, await Svc.StartAsync(id));
        Assert.Empty(await Svc.QueueAsync());
        Assert.Equal("running", (await Svc.GetAsync(TestClubs.Home, id))!.Status);

        Assert.Equal(ClubGameSummaries.Outcome.Invalid, await Svc.CompleteAsync(id, "  ", "fable"));
        Assert.Equal(ClubGameSummaries.Outcome.Ok, await Svc.CompleteAsync(id, "# Partie\n\nText", "claude-fable-5-1"));
        var dto = (await Svc.GetAsync(TestClubs.Home, id))!;
        Assert.Equal(("done", "# Partie\n\nText", false, "claude-fable-5-1"), (dto.Status, dto.Markdown, dto.Stale, dto.Model));
    }

    [Fact]
    public async Task RequestAgain_KeepsTheOldText_UntilTheNewOneIsThere()
    {
        var id = await GameAsync();
        await Svc.CompleteAsync(id, "alt", null);
        var again = (await Svc.RequestAsync(TestClubs.Home, id, 7))!;
        Assert.Equal(("requested", "alt"), (again.Status, again.Markdown));
        await Svc.StartAsync(id);
        Assert.Equal(ClubGameSummaries.Outcome.Ok, await Svc.FailAsync(id, "claude exit 1"));
        var failed = (await Svc.GetAsync(TestClubs.Home, id))!;
        Assert.Equal(("failed", "alt", "claude exit 1"), (failed.Status, failed.Markdown, failed.Error));
    }

    [Fact]
    public async Task Running_TooLong_ComesBackIntoTheQueue()
    {
        var id = await GameAsync();
        await Svc.RequestAsync(TestClubs.Home, id, 7);
        await Svc.StartAsync(id);
        _clock.Advance(ClubGameSummaries.StaleRunning + TimeSpan.FromMinutes(1));
        Assert.Single(await Svc.QueueAsync());
        Assert.Equal(ClubGameSummaries.Outcome.Ok, await Svc.StartAsync(id));
    }

    [Fact]
    public async Task CorrectedGame_MarksTheSummaryStale()
    {
        var id = await GameAsync(hash: "vorher");
        await Svc.CompleteAsync(id, "Text", null);
        var g = await _db.LeagueClubGames.FirstAsync(x => x.Id == id);
        g.MovesHash = "nachher";
        await _db.SaveChangesAsync();
        Assert.True((await Svc.GetAsync(TestClubs.Home, id))!.Stale);
    }

    [Fact]
    public async Task UnknownGame_IsNotFound()
    {
        Assert.Equal(ClubGameSummaries.Outcome.NotFound, await Svc.CompleteAsync(999, "x", null));
        Assert.Equal(ClubGameSummaries.Outcome.NotFound, await Svc.StartAsync(999));
        Assert.Equal(ClubGameSummaries.Outcome.NotFound, await Svc.FailAsync(999, null));
    }

    [Fact]
    public async Task RequestEndpoint_OnlyForAdmins()
    {
        var id = await GameAsync();
        var c = new RookHub.Api.Controllers.League.LeagueClubOverviewController(null!, null!, null!, null!, null!).As(7);
        Assert.IsType<Microsoft.AspNetCore.Mvc.ForbidResult>(await c.RequestSummary(id, Svc, default));
        Assert.Equal("none", (await Svc.GetAsync(TestClubs.Home, id))!.Status);
    }
}
