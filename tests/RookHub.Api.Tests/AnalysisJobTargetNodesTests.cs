using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Phase 1 der Lc0-Zweitprüfung: Knotenziel (Zeile lesen, Ziel prüfen, Eingabe prüfen) und Engines, die nur auf
/// ausdrückliche Anforderung rechnen (<c>AnalysisJobs:ExplicitOnlyEngineIds</c>) — automatische Wahl, Failover,
/// Platzzahl der Meisterpartien-Analyse. Der Worker-Lauf selbst steht in <see cref="AnalysisJobWorkerBrokerTests"/>.
/// </summary>
public class AnalysisJobTargetNodesTests : IDisposable
{
    private const string Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";
    private const int Owner = 5;
    private readonly AppDbContext _db =
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private static IConfiguration Config(params string[] explicitOnly) => new ConfigurationBuilder()
        .AddInMemoryCollection(explicitOnly.Select((id, i) =>
            new KeyValuePair<string, string?>($"{ExplicitOnlyEngines.ConfigKey}:{i}", id)).Append(
            new KeyValuePair<string, string?>("Encryption:Key", "TestEncryptionKey32CharsLong!!!!")))
        .Build();

    private void SeedOwner(params string[] engines)
    {
        _db.AppUsers.Add(new AppUser { Id = Owner, Username = "kahalm", PasswordHash = "x", IsAdmin = true });
        var cred = new LichessEngineCredential { UserId = Owner, EncryptedToken = "enc" };
        cred.SetBackgroundEngines(engines);
        _db.LichessEngineCredentials.Add(cred);
        _db.SaveChanges();
    }

    // ── Zeilen und Ziel ──

    [Theory]
    [InlineData("{\"depth\":9,\"nodes\":50000,\"time\":1}", 50000L)]
    [InlineData("{\"depth\":9,\"time\":1}", null)]
    [InlineData("kein json", null)]
    public void NodesOf_readsTheNodeCount(string line, long? expected) =>
        Assert.Equal(expected, AnalysisJobStream.NodesOf(line));

    [Theory]
    [InlineData(100_000L, 100_000L, false, true)]
    [InlineData(100_000L, 99_999L, false, false)]    // ohne Ende der Engine muss das Ziel voll da sein
    [InlineData(100_000L, 95_000L, true, true)]       // Engine beendet selbst: fünf Prozent Spielraum
    [InlineData(100_000L, 94_999L, true, false)]
    public void NodeGoalMet_toleratesTheEnginesOwnEnd(long target, long nodes, bool ended, bool expected) =>
        Assert.Equal(expected, AnalysisJobStream.NodeGoalMet(target, nodes, ended));

    // ── Eingabe ──

    [Theory]
    [InlineData(999L)]
    [InlineData(50_000_001L)]
    [InlineData(0L)]
    [InlineData(-5L)]
    public async Task Create_rejectsNodeGoalsOutsideTheRange(long nodes)
    {
        SeedOwner("rhe_a");
        var svc = new AnalysisJobService(_db);
        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(Owner,
            new CreateAnalysisJobRequest { Fen = Fen, TargetNodes = nodes }, remember: false));
    }

    [Fact]
    public async Task Create_storesTheNodeGoal_andTheDtoCarriesIt()
    {
        SeedOwner("rhe_a");
        var dto = await new AnalysisJobService(_db).CreateAsync(Owner,
            new CreateAnalysisJobRequest { Fen = Fen, TargetNodes = 50_000 }, remember: false);
        Assert.Equal(50_000, dto.TargetNodes);
        Assert.Equal(50_000, (await _db.AnalysisJobs.SingleAsync()).TargetNodes);
    }

    [Fact]
    public async Task Update_leavesADoneNodeJobDone_evenThoughItsDepthIsBelowTheStoredTarget()
    {
        SeedOwner("rhe_a");
        var svc = new AnalysisJobService(_db);
        var dto = await svc.CreateAsync(Owner, new CreateAnalysisJobRequest { Fen = Fen, TargetDepth = 30, TargetNodes = 50_000 }, remember: false);
        var job = await _db.AnalysisJobs.SingleAsync();
        job.Status = AnalysisJobStatus.Done; job.ReachedDepth = 9;
        await _db.SaveChangesAsync();

        // Die Auftragsseite schickt Tiefe/Linien/Engine immer mit.
        var after = await svc.UpdateAsync(Owner, dto.Id, new UpdateAnalysisJobRequest { TargetDepth = 30, MultiPv = job.MultiPv });
        Assert.Equal("done", after!.Status);
        Assert.Equal("done", (await svc.RestartAsync(Owner, dto.Id))!.Status);
    }

    // ── Nur auf Anforderung ──

    [Fact]
    public void ExplicitOnly_isEmptyWithoutConfig_andKeepsEveryEngine()
    {
        Assert.Empty(ExplicitOnlyEngines.From(null));
        Assert.Empty(ExplicitOnlyEngines.From(Config()));
        var engines = new[] { "rhe_a", "rhe_b" };
        Assert.Equal(engines, ExplicitOnlyEngines.Automatic(engines, ExplicitOnlyEngines.From(Config())));
    }

    [Fact]
    public void ExplicitOnly_dropsTheListedEngines_keepingTheOrder()
    {
        var set = ExplicitOnlyEngines.From(Config(" rhe_lc0 "));
        Assert.Equal(new[] { "rhe_a", "rhe_b" }, ExplicitOnlyEngines.Automatic(["rhe_a", "rhe_lc0", "rhe_b"], set));
    }

    [Fact]
    public async Task Pick_neverChoosesAnExplicitOnlyEngine_butAnExplicitRequestStillWorks()
    {
        SeedOwner("rhe_lc0", "rhe_sf");   // lc0 zuerst: ohne Ausschluss gewänne es den Gleichstand der Schlangen
        var svc = new AnalysisJobService(_db, config: Config("rhe_lc0"));
        for (var i = 0; i < 3; i++)
        {
            var auto = await svc.CreateAsync(Owner, new CreateAnalysisJobRequest { Fen = Fen }, remember: false);
            Assert.Equal("rhe_sf", auto.EngineId);
        }
        var asked = await svc.CreateAsync(Owner, new CreateAnalysisJobRequest { Fen = Fen, EngineId = "rhe_lc0", TargetNodes = 50_000 }, remember: false);
        Assert.Equal("rhe_lc0", asked.EngineId);
    }

    [Fact]
    public async Task Pick_withOnlyExplicitOnlyEngines_hasNothingToChooseAutomatically()
    {
        SeedOwner("rhe_lc0");
        var svc = new AnalysisJobService(_db, config: Config("rhe_lc0"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync(Owner, new CreateAnalysisJobRequest { Fen = Fen }, remember: false));
    }

    [Fact]
    public void NextEngineAfter_neverRotatesIntoOrOutOfAnExplicitOnlyEngine()
    {
        var set = ExplicitOnlyEngines.From(Config("rhe_lc0"));
        string[] engines = ["rhe_a", "rhe_lc0", "rhe_b"];
        Assert.Equal("rhe_b", AnalysisJobWorker.NextEngineAfter(engines, "rhe_a", set));   // überspringt lc0
        Assert.Equal("rhe_a", AnalysisJobWorker.NextEngineAfter(engines, "rhe_b", set));
        Assert.Null(AnalysisJobWorker.NextEngineAfter(engines, "rhe_lc0", set));           // bleibt dort
        Assert.Null(AnalysisJobWorker.NextEngineAfter(["rhe_a", "rhe_lc0"], "rhe_a", set)); // nur noch EINE automatische
        Assert.Equal("rhe_lc0", AnalysisJobWorker.NextEngineAfter(engines, "rhe_a"));        // ohne Liste wie bisher
    }

    /// <summary>16 Engines, 14 davon nur auf Anforderung → 2 Plätze statt 16: bei je 10 offenen Halbzügen je Partie legt
    /// der Takt nur EINE Partie an, ohne die Liste zwei (vgl. <c>MasterAnalysisSchedulerTests</c>).</summary>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task MasterAnalysis_slotsIgnoreExplicitOnlyEngines(bool withExplicitOnly, int expectedGames)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.AppUsers.Add(new AppUser { Id = Owner, Username = "kahalm", PasswordHash = "x", IsAdmin = true });
        var cred = new LichessEngineCredential { UserId = Owner, EncryptedToken = "enc", ShareAsHouseEngine = true };
        var ids = Enumerable.Range(1, 16).Select(i => $"rhe_t{i}").ToList();
        cred.SetBackgroundEngines(ids);
        db.LichessEngineCredentials.Add(cred);
        db.LibraryGames.AddRange(Enumerable.Range(1, 3).Select(i => new LibraryGame
        {
            Id = i, SourceFile = "t.pgn", White = "W", Black = "B", CommentedPlies = 3, MovesHash = $"h{i}",
            Pgn = "[White \"W\"]\n[Black \"B\"]\n[Result \"*\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 *",
        }));
        db.SaveChanges();

        var config = withExplicitOnly ? Config(ids.Skip(2).ToArray()) : Config();
        var scheduler = new MasterAnalysisScheduler(null!, new QuietHours(""), config, NullLogger<MasterAnalysisScheduler>.Instance);
        var svc = new GameAnalysisService(db, new AnalysisJobService(db),
            new CommentSetService(db, NullLogger<CommentSetService>.Instance), NullLogger<GameAnalysisService>.Instance);
        for (var i = 0; i < 4; i++) await scheduler.TickOnceAsync(db, svc, default);

        Assert.Equal(expectedGames, await db.GameAnalyses.CountAsync());
    }
}
