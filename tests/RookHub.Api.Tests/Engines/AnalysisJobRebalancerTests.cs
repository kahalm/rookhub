using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Tests.Engines;

/// <summary>Freie Engines übernehmen wartende Aufträge anderer Engines derselben Hintergrund-Liste (0.920.0).</summary>
public class AnalysisJobRebalancerTests : IDisposable
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const int Owner = 990501;
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTime _now = DateTime.UtcNow;
    private static readonly IReadOnlySet<string> NoExplicit = new HashSet<string>();

    public AnalysisJobRebalancerTests()
    {
        _db.AppUsers.Add(new AppUser { Id = Owner, Username = "house", PasswordHash = "x" });
        var cred = new LichessEngineCredential { UserId = Owner, EncryptedToken = "" };
        cred.SetBackgroundEngines(["rhe_gross1", "rhe_server1", "rhe_server2", "rhe_lc0"]);
        _db.LichessEngineCredentials.Add(cred);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private AnalysisJob Job(string engine, JobPriority p, int minutesAgo, bool batch = true)
    {
        var j = new AnalysisJob
        {
            UserId = Owner, Fen = Start, EngineId = engine, TargetDepth = 20, MultiPv = 1, Status = AnalysisJobStatus.Queued,
            CreatedAt = _now.AddMinutes(-minutesAgo), Background = true, Batch = batch, Priority = p,
        };
        _db.AnalysisJobs.Add(j);
        return j;
    }

    private Task<int> RunAsync(params string[] busy) =>
        AnalysisJobRebalancer.RebalanceAsync(_db, _now, e => busy.Contains(e), new HashSet<string> { "rhe_lc0" }, quiet: null);

    [Fact]
    public async Task IdleEngines_TakeWaitingJobsOfABusyEngine_HighestPriorityFirst()
    {
        var league = Job("rhe_gross1", JobPriority.League, 60);
        var waiting = Job("rhe_gross1", JobPriority.Waiting, 1, batch: false);
        var club = Job("rhe_gross1", JobPriority.ClubNew, 30);
        await _db.SaveChangesAsync();

        Assert.Equal(2, await RunAsync("rhe_gross1"));

        Assert.Equal("rhe_server1", waiting.EngineId);   // die zwei wichtigsten wandern, die Liga bleibt
        Assert.Equal("rhe_server2", club.EngineId);
        Assert.Equal("rhe_gross1", league.EngineId);
    }

    [Fact]
    public async Task EngineThatCanRunItsOwnJob_KeepsIt()
    {
        var job = Job("rhe_gross1", JobPriority.League, 5);
        await _db.SaveChangesAsync();
        Assert.Equal(0, await RunAsync());   // gross1 ist frei und nimmt ihn gleich selbst
        Assert.Equal("rhe_gross1", job.EngineId);
    }

    [Fact]
    public async Task NeverTouches_HandPickedOrExplicitOnlyEngines_AndExplicitOnlyNeverTakes()
    {
        var manual = Job("rhe_elsewhere", JobPriority.League, 5);   // nicht in der Liste: von Hand gewählt
        var lc0 = Job("rhe_lc0", JobPriority.ClubNew, 5);
        Job("rhe_server1", JobPriority.Master, 5);
        Job("rhe_server2", JobPriority.Master, 5);
        var gross = Job("rhe_gross1", JobPriority.League, 5);
        Job("rhe_gross1", JobPriority.League, 4);
        await _db.SaveChangesAsync();

        // Alle Stockfish-Engines haben eigene Arbeit; Lc0 ist frei, darf aber nichts nehmen.
        Assert.Equal(0, await RunAsync("rhe_elsewhere", "rhe_lc0", "rhe_gross1"));
        Assert.Equal("rhe_elsewhere", manual.EngineId);
        Assert.Equal("rhe_lc0", lc0.EngineId);
        Assert.Equal("rhe_gross1", gross.EngineId);
    }

    /// <summary>R1-012: die wartenden kommen nur als Projektion; als Entität geladen (und gespeichert) wird nur, was umzieht.</summary>
    [Fact]
    public async Task OnlyMovedJobs_AreLoadedAsEntities()
    {
        for (var i = 0; i < 10; i++) Job("rhe_gross1", JobPriority.League, 60 - i);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Assert.Equal(2, await RunAsync("rhe_gross1"));

        var tracked = _db.ChangeTracker.Entries<AnalysisJob>().ToList();
        Assert.Equal(2, tracked.Count);
        Assert.All(tracked, e => Assert.NotEqual("rhe_gross1", e.Entity.EngineId));
    }
}
