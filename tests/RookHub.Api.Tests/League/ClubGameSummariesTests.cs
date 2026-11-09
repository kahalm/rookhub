using Microsoft.Extensions.Configuration;
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

    /// <summary>Hauptanalyse der Partie anlegen: fertig, Vertiefung erledigt, Vergleiche angelegt (alle gerechnet) — „bereit".</summary>
    private async Task<GameAnalysis> ReadyAsync(int gameId, GameAnalysisStatus status = GameAnalysisStatus.Done, bool refined = true,
        bool comparisonsPlanned = true, params MoveComparisonStatus[] comparisons)
    {
        var a = new GameAnalysis
        {
            UserId = 1, Pgn = "", StartFen = "", Origin = GameAnalysisOrigin.Club, LeagueClubGameId = gameId, Status = status,
            PlyCount = 2, White = "W", Black = "S", RefineDepth = 30, RefinedAt = refined ? DateTime.UtcNow : null,
            MistakeComparisonsAt = comparisonsPlanned ? DateTime.UtcNow : null,
            Positions = [new GameAnalysisPosition { Ply = 0, Fen = "x", GameMoveUci = "e2e4", Refined = true },
                         new GameAnalysisPosition { Ply = 1, Fen = "y", GameMoveUci = "e7e5", Refined = refined }],
        };
        _db.GameAnalyses.Add(a);
        await _db.SaveChangesAsync();
        var ply = 0;
        foreach (var st in comparisons)
            _db.MoveComparisons.Add(new MoveComparison { UserId = 1, GameAnalysisId = a.Id, Ply = ply++, Fen = "x", Status = st });
        await _db.SaveChangesAsync();
        return a;
    }

    [Fact]
    public async Task Readiness_WaitsForRefinementAndEveryComparison()
    {
        var id = await GameAsync();
        Assert.False((await Svc.ReadinessAsync(id)).HasAnalysis);

        var a = await ReadyAsync(id, refined: false, comparisonsPlanned: false);
        var r = await Svc.ReadinessAsync(id);
        Assert.Equal((false, true, true, 1, 2, false), (r.Ready, r.AnalysisDone, r.Refining, r.RefinedPositions, r.TotalPositions, r.ComparisonsPlanned));

        a.RefinedAt = DateTime.UtcNow;
        a.Positions.ForEach(p => p.Refined = true);
        a.MistakeComparisonsAt = DateTime.UtcNow;
        _db.MoveComparisons.AddRange(
            new MoveComparison { UserId = 1, GameAnalysisId = a.Id, Ply = 3, Fen = "x", Status = MoveComparisonStatus.Done },
            new MoveComparison { UserId = 1, GameAnalysisId = a.Id, Ply = 7, Fen = "x", Status = MoveComparisonStatus.Waiting });
        await _db.SaveChangesAsync();
        r = await Svc.ReadinessAsync(id);
        Assert.Equal((false, false, 1, 2), (r.Ready, r.Refining, r.ComparisonsDone, r.ComparisonsTotal));

        // offene Anforderung: nicht in der Warteschlange, Start abgelehnt, Stand im DTO
        await Svc.RequestAsync(TestClubs.Home, id, 7);
        Assert.Empty(await Svc.QueueAsync());
        Assert.Equal(ClubGameSummaries.Outcome.Conflict, await Svc.StartAsync(id));
        Assert.Equal(1, (await Svc.GetAsync(TestClubs.Home, id))!.Readiness!.ComparisonsDone);

        (await _db.MoveComparisons.SingleAsync(c => c.Ply == 7)).Status = MoveComparisonStatus.Failed;   // gescheitert zählt als durch
        await _db.SaveChangesAsync();
        Assert.True((await Svc.ReadinessAsync(id)).Ready);
        Assert.Single(await Svc.QueueAsync());
    }

    [Fact]
    public async Task Readiness_Endstellung_zaehltNichtMit()
    {
        var id = await GameAsync();
        var a = await ReadyAsync(id, refined: false, comparisonsPlanned: false);
        a.Positions.Add(new GameAnalysisPosition { Ply = 2, Fen = "z" });   // Endstellung: kein Zug, wird nie vertieft
        await _db.SaveChangesAsync();
        var r = await Svc.ReadinessAsync(id);
        Assert.Equal((1, 2), (r.RefinedPositions, r.TotalPositions));
    }

    [Fact]
    public async Task Readiness_SchnellerDurchgang_zaehltGerechneteStellungen()
    {
        var id = await GameAsync();
        var a = await ReadyAsync(id, GameAnalysisStatus.Running, refined: false, comparisonsPlanned: false);
        a.Positions[0].AnalyzedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var r = await Svc.ReadinessAsync(id);
        Assert.Equal((false, false, 1, 2), (r.Ready, r.AnalysisDone, r.AnalyzedPositions, r.TotalPositions));
    }

    [Fact]
    public async Task Readiness_FailedAnalysis_OrComparisonsSwitchedOff_DoNotBlock()
    {
        var id = await GameAsync();
        await ReadyAsync(id, GameAnalysisStatus.Failed, refined: false, comparisonsPlanned: false);
        Assert.True((await Svc.ReadinessAsync(id)).Ready);

        var other = await GameAsync(hash: "h2");
        await ReadyAsync(other, comparisonsPlanned: false);
        Assert.False((await Svc.ReadinessAsync(other)).Ready);
        var off = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ClubMistakeComparisons:Enabled"] = "false" }).Build();
        Assert.True((await new ClubGameSummaries(_db, _clock, off).ReadinessAsync(other)).Ready);
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
        await ReadyAsync(id, comparisons: MoveComparisonStatus.Done);
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
        await ReadyAsync(id);
        await Svc.RequestAsync(TestClubs.Home, id, 7);   // seit R4-005 nur mit vorhandener Zeile
        Assert.Equal(ClubGameSummaries.Outcome.Ok, await Svc.CompleteAsync(id, "alt", null));
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
        await ReadyAsync(id);
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
        _db.ClubGameSummaries.Add(new ClubGameSummary { ClubGameId = id, RequestedAt = _clock.GetUtcNow().UtcDateTime });   // R4-005: Zeile nötig
        await _db.SaveChangesAsync();
        Assert.Equal(ClubGameSummaries.Outcome.Ok, await Svc.CompleteAsync(id, "Text", null));
        var g = await _db.LeagueClubGames.FirstAsync(x => x.Id == id);
        g.MovesHash = "nachher";
        await _db.SaveChangesAsync();
        Assert.True((await Svc.GetAsync(TestClubs.Home, id))!.Stale);
    }

    /// <summary>R4-005: der Handlauf (Abliefern ohne Start) bleibt, legt aber keine Zusammenfassung mehr für eine Partie an, die
    /// keiner angefordert hat — vorher schrieb eine frei gewählte Id in jede Vereinspartie.</summary>
    [Fact]
    public async Task Complete_ohneZeile_legtNichtsAn()
    {
        var id = await GameAsync();
        Assert.Equal(ClubGameSummaries.Outcome.NotFound, await Svc.CompleteAsync(id, "# untergeschoben", null));
        Assert.Empty(_db.ClubGameSummaries);
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

    [Fact]
    public void ElapsedSecondsOf_WartenAbAnforderung_SchreibenAbStart_NieNegativ()
    {
        var now = new DateTime(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc);
        var requested = now.AddSeconds(-300);
        Assert.Equal(125, ClubGameSummaries.ElapsedSecondsOf(ClubGameSummaryStatus.Running, requested, now.AddSeconds(-125), now));
        Assert.Equal(0, ClubGameSummaries.ElapsedSecondsOf(ClubGameSummaryStatus.Running, requested, now.AddSeconds(5), now));
        Assert.Null(ClubGameSummaries.ElapsedSecondsOf(ClubGameSummaryStatus.Running, requested, null, now));
        // Offene Anforderung: ab RequestedAt, ein alter StartedAt spielt keine Rolle.
        Assert.Equal(300, ClubGameSummaries.ElapsedSecondsOf(ClubGameSummaryStatus.Requested, requested, null, now));
        Assert.Equal(300, ClubGameSummaries.ElapsedSecondsOf(ClubGameSummaryStatus.Requested, requested, now.AddDays(-1), now));
        Assert.Equal(0, ClubGameSummaries.ElapsedSecondsOf(ClubGameSummaryStatus.Requested, now.AddSeconds(5), null, now));
        Assert.Null(ClubGameSummaries.ElapsedSecondsOf(ClubGameSummaryStatus.Done, requested, now.AddSeconds(-125), now));
        Assert.Null(ClubGameSummaries.ElapsedSecondsOf(ClubGameSummaryStatus.Failed, requested, now.AddSeconds(-125), now));
    }
}
