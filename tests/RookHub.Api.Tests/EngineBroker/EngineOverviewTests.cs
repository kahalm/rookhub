using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers.EngineBroker;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Tests.EngineBroker;

/// <summary>Admin-Seite „Engines" (0.946.0): Berechtigung, Zusammenstellung der Übersicht und Durchsatz-Zählung.
/// Feste Ids im 99xxxx-Bereich, jede Testmethode mit eigener InMemory-DB und eigenen Singletons.</summary>
public class EngineOverviewTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private const int Owner = 990101, Other = 990102;
    private const string RheA = "rhe_ovA000000001", RheB = "rhe_ovB000000002", RheOff = "rhe_ovC000000003", Eei = "eei_ovLichess01";

    private readonly ServiceProvider _sp;
    private readonly EngineHub _hub;
    private readonly EngineSelectorDirectory _directory;
    private readonly EngineActivityTracker _tracker = new();
    private readonly AnalysisJobLive _live = new();
    private readonly EngineThroughputRecorder _recorder = new();
    private readonly LocalBrokerOptions _options = new();

    public EngineOverviewTests()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
        _sp = services.BuildServiceProvider();
        _hub = new EngineHub(_options, () => Now, startSweeper: false);
        _directory = new EngineSelectorDirectory(_sp.GetRequiredService<IServiceScopeFactory>(), () => Now);
    }

    public void Dispose()
    {
        _hub.Dispose();
        _sp.Dispose();
    }

    private EngineOverviewService Service(AppDbContext db) => new(db, new MemoryCache(new MemoryCacheOptions()), _hub, _directory,
        _options, _tracker, _live, _recorder,
        new BackgroundPause(_sp.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(),
            NullLogger<BackgroundPause>.Instance),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ExplicitOnlyEngines.ConfigKey + ":0"] = RheB,
        }).Build());

    private static ExternalEngineRegistration Reg(string id, int user, string name, string selector, DateTime? lastSeen) => new()
    {
        Id = id, UserId = user, Name = name, ClientSecret = "cs", ProviderSelector = selector, MaxThreads = 4, MaxHash = 256,
        Variants = "chess", CreatedAt = Now.AddDays(-3), LastSeenAt = lastSeen,
    };

    private static AnalysisJob Job(int id, string engine, AnalysisJobStatus status, JobPriority prio, DateTime created) => new()
    {
        Id = id, UserId = Owner, EngineId = engine, Status = status, Priority = prio, Fen = "8/8/8/8/8/8/8/K6k w - - 0 1",
        TargetDepth = 30, MultiPv = 3, CreatedAt = created, ReachedDepth = 12, CurrentDepth = 14, CurrentNps = 500_000,
        SecondsSpent = 40, LastRunAt = Now.AddSeconds(-20), Batch = prio >= JobPriority.ClubNew, Background = prio != JobPriority.Interactive,
    };

    private async Task SeedAsync()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AppUsers.AddRange(
            new AppUser { Id = Owner, Username = "ov-owner", PasswordHash = "x" },
            new AppUser { Id = Other, Username = "ov-other", PasswordHash = "x" });
        db.ExternalEngineRegistrations.AddRange(
            Reg(RheA, Owner, "Server 1", "selA", Now.AddSeconds(-5)),
            Reg(RheB, Owner, "Lc0", "selB", Now.AddSeconds(-10)),
            Reg(RheOff, Other, "Alter PC", "selC", Now.AddDays(-2)));
        db.LichessEngineCredentials.AddRange(
            new LichessEngineCredential { UserId = Owner, BackgroundEngineIds = $"{RheA},{Eei}", ShareAsHouseEngine = true },
            new LichessEngineCredential { UserId = Other, BackgroundEngineIds = RheA });
        db.AnalysisJobs.AddRange(
            Job(990201, RheA, AnalysisJobStatus.Running, JobPriority.League, Now.AddHours(-1)),
            Job(990202, RheA, AnalysisJobStatus.Queued, JobPriority.League, Now.AddHours(-3)),
            Job(990203, RheA, AnalysisJobStatus.Queued, JobPriority.Interactive, Now.AddMinutes(-5)),
            Job(990204, Eei, AnalysisJobStatus.Paused, JobPriority.Master, Now.AddHours(-5)),
            Job(990205, RheA, AnalysisJobStatus.Done, JobPriority.League, Now.AddDays(-10)));
        db.GameAnalyses.Add(new GameAnalysis
        {
            Id = 990301, UserId = Owner, Pgn = "1. e4", StartFen = "x", White = "Weiß", Black = "Schwarz",
            Status = GameAnalysisStatus.Running,
        });
        db.GameAnalysisPositions.Add(new GameAnalysisPosition
        {
            Id = 990401, GameAnalysisId = 990301, Ply = 17, Fen = "x", GameMoveUci = "e2e4", GameMoveSan = "e4", AnalysisJobId = 990201,
        });
        db.EngineThroughputBuckets.AddRange(
            new EngineThroughputBucket { EngineId = RheA, BucketStart = Now.AddMinutes(-30), Runs = 5, Done = 4, Unavailable = 1, RunSeconds = 200, DoneJobSeconds = 160, Nodes = 100_000_000, NodeSeconds = 200 },
            new EngineThroughputBucket { EngineId = RheA, BucketStart = Now.AddHours(-5), Runs = 10, Done = 10, RunSeconds = 400, DoneJobSeconds = 400 },
            new EngineThroughputBucket { EngineId = RheA, BucketStart = Now.AddDays(-3), Runs = 2, Done = 1, Failed = 1, RunSeconds = 50 });
        await db.SaveChangesAsync();
    }

    // ── Berechtigung ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Controller_IsAdminOnly_ReadOnly()
    {
        var type = typeof(AdminEnginesController);
        Assert.Equal("perm:" + Permissions.AdminOnly, type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("api/admin/engines", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
        var actions = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.DoesNotContain(actions, m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null);
        // Nur lesend: keine schreibende Aktion.
        Assert.All(actions, m => Assert.DoesNotContain(m.GetCustomAttributes(), a => a is HttpPostAttribute or HttpPutAttribute or HttpDeleteAttribute or HttpPatchAttribute));
        Assert.Contains(actions, m => m.GetCustomAttribute<HttpGetAttribute>()?.Template == "overview");
    }

    /// <summary>Die Richtlinie des Controllers lässt ein Konto ohne Admin-Flag nicht durch (→ 403), auch mit allen Rechten.</summary>
    [Fact]
    public async Task Policy_RejectsNonAdmin_AcceptsAdmin()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.AppUsers.AddRange(
            new AppUser { Id = 990501, Username = "ov-admin", PasswordHash = "x", IsAdmin = true },
            new AppUser { Id = 990502, Username = "ov-user", PasswordHash = "x" });
        var role = new Role { Id = 990601, Key = "ov-alles", Name = "Alles" };
        db.Roles.Add(role);
        foreach (var perm in Permissions.All) db.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = perm });
        db.UserRoles.Add(new UserRole { UserId = 990502, RoleId = role.Id });
        await db.SaveChangesAsync();
        var resolver = new PermissionResolver(db, new MemoryCache(new MemoryCacheOptions()));
        var policy = typeof(AdminEnginesController).GetCustomAttribute<AuthorizeAttribute>()!.Policy!;
        var requirement = new PermissionRequirement(policy["perm:".Length..]);

        async Task<bool> Allowed(int id)
        {
            var ctx = new AuthorizationHandlerContext([requirement],
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test")), null);
            await new PermissionAuthorizationHandler(resolver).HandleAsync(ctx);
            return ctx.HasSucceeded;
        }

        Assert.True(await Allowed(990501));
        Assert.False(await Allowed(990502));
    }

    // ── Übersicht ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Build_ListsAllEngines_WithBackground_Queue_Current_AndThroughput()
    {
        await SeedAsync();
        _directory.MarkSeen("selA");
        _live.Start(990201, Owner, 40, Now.AddSeconds(-20));
        _live.Update(990201, depth: 22, nps: 900_000, nowUtc: Now, nodes: 18_000_000);
        _tracker.Begin(Owner, RheB);
        _recorder.Record(RheA, EngineRunOutcome.Done, 30, 0, 50, Now.AddMinutes(-1));   // noch nicht geschrieben

        using var scope = _sp.CreateScope();
        var dto = await Service(scope.ServiceProvider.GetRequiredService<AppDbContext>()).BuildAsync(Now, CancellationToken.None);

        Assert.Equal([RheA, RheB, Eei, RheOff], dto.Engines.Select(e => e.Id));   // rechnet, live, unbekannt, offline
        var a = dto.Engines[0];
        Assert.Equal("working", a.State);
        Assert.Equal("rookhub", a.Source);
        Assert.Equal("Server 1", a.Name);
        Assert.Equal("ov-owner", a.OwnerName);
        Assert.True(a.Online);
        Assert.True(a.HouseEngine);
        Assert.Equal(["ov-other", "ov-owner"], a.BackgroundFor.Select(u => u.Name));
        Assert.Equal(2, a.WaitingQueued);
        Assert.Equal(Now.AddHours(-3), a.OldestWaitingSince);
        Assert.NotNull(a.Current);
        Assert.Equal(990201, a.Current!.JobId);
        Assert.Equal("league", a.Current.Kind);
        Assert.Equal(22, a.Current.Depth);                       // Speicher schlägt DB (14)
        Assert.Equal(900_000, a.Current.Nps);
        Assert.Equal(60, a.Current.Seconds);                     // 40 verbucht + 20 laufend
        Assert.Equal(990301, a.Current.GameAnalysisId);
        Assert.Equal(17, a.Current.Ply);
        Assert.Equal("Weiß – Schwarz", a.Current.GameTitle);
        // Durchsatz: Stunde = Korb vor 30 min + ungeschriebener Lauf; Tag + 5 h; Woche + 3 Tage.
        Assert.Equal(5, a.Throughput.Hour.Done);
        Assert.Equal(1, a.Throughput.Hour.Unavailable);
        Assert.Equal(42.0, a.Throughput.Hour.AvgJobSeconds);    // (160 + 50) / 5
        Assert.Equal(500.0, a.Throughput.Hour.KiloNodesPerSecond);
        Assert.Equal(15, a.Throughput.Day.Done);
        Assert.Equal(16, a.Throughput.Week.Done);
        Assert.Equal(1, a.Throughput.Week.Failed);

        var b = dto.Engines[1];
        Assert.Equal("live", b.State);
        Assert.Equal(1, b.LiveStreams);
        Assert.True(b.ExplicitOnly);
        Assert.Empty(b.BackgroundFor);

        var lichess = dto.Engines[2];
        Assert.Equal("lichess", lichess.Source);
        Assert.Equal("unknown", lichess.State);
        Assert.False(lichess.Registered);
        Assert.Null(lichess.Online);
        Assert.Equal(Owner, lichess.OwnerUserId);                // aus der einzigen Hintergrund-Liste
        Assert.Equal(1, lichess.WaitingPaused);

        Assert.Equal("offline", dto.Engines[3].State);
        Assert.False(dto.Engines[3].Online);

        Assert.Equal(2, dto.Queue.Queued);
        Assert.Equal(1, dto.Queue.Paused);
        Assert.Equal(1, dto.Queue.Running);
        Assert.Equal(Now.AddHours(-5), dto.Queue.OldestWaitingSince);
        Assert.Equal(["interactive", "league", "master"], dto.Queue.ByKind.Select(k => k.Kind));
        var league = dto.Queue.ByKind.Single(k => k.Kind == "league");
        Assert.Equal((1, 1), (league.Queued, league.Running));
        Assert.Equal(1, dto.Queue.GameAnalysesRunning);
        Assert.Equal(5, dto.Totals.Hour.Done);
        Assert.False(dto.Pause.Paused);
        Assert.Equal(EngineThroughputBucket.BucketMinutes, dto.BucketMinutes);
    }

    [Fact]
    public async Task Build_WithoutAnything_IsEmpty_NotAnError()
    {
        using var scope = _sp.CreateScope();
        var dto = await Service(scope.ServiceProvider.GetRequiredService<AppDbContext>()).BuildAsync(Now, CancellationToken.None);
        Assert.Empty(dto.Engines);
        Assert.Empty(dto.Queue.ByKind);
        Assert.Null(dto.Queue.OldestWaitingSince);
        Assert.Equal(0, dto.Totals.Week.Runs);
        Assert.Null(dto.Totals.Week.AvgJobSeconds);
        Assert.Null(dto.Totals.Week.KiloNodesPerSecond);
    }

    [Fact]
    public void KindAndSource_Names()
    {
        Assert.Equal(["interactive", "waiting", "clubNew", "clubBackfill", "league", "master"],
            Enum.GetValues<JobPriority>().Select(EngineOverviewService.KindOf));
        Assert.Equal("rookhub", EngineOverviewService.SourceOf("rhe_abc"));
        Assert.Equal("lichess", EngineOverviewService.SourceOf("eei_abc"));
        Assert.Equal("other", EngineOverviewService.SourceOf("xyz"));
    }

    // ── Durchsatz-Zählung ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Recorder_CountsIntoBuckets_FlushAdds_AndPrunesOld()
    {
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.EngineThroughputBuckets.AddRange(
                new EngineThroughputBucket { EngineId = RheA, BucketStart = EngineThroughputRecorder.BucketOf(Now), Runs = 1, Done = 1, RunSeconds = 10 },
                new EngineThroughputBucket { EngineId = RheA, BucketStart = Now.AddDays(-EngineThroughputBucket.RetentionDays - 1), Runs = 1 });
            await db.SaveChangesAsync();
        }

        _recorder.Record(RheA, EngineRunOutcome.Done, 20.4, 4_000_000, 90, Now.AddMinutes(3));
        _recorder.Record(RheA, EngineRunOutcome.Unavailable, 15, 0, 0, Now.AddMinutes(4));
        _recorder.Record(RheA, EngineRunOutcome.Timeout, 300, 0, 0, Now.AddMinutes(12));   // nächster Korb
        _recorder.Record(RheB, EngineRunOutcome.Interrupted, 5, 1000, 0, Now);
        Assert.Equal(3, _recorder.Pending().Count);

        using (var scope = _sp.CreateScope())
            await _recorder.FlushAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), Now.AddMinutes(13), CancellationToken.None);
        Assert.Empty(_recorder.Pending());

        using (var scope = _sp.CreateScope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().EngineThroughputBuckets.AsNoTracking()
                .OrderBy(b => b.EngineId).ThenBy(b => b.BucketStart).ToListAsync();
            Assert.Equal(3, rows.Count);                              // alter Korb weg
            var first = rows[0];
            Assert.Equal(Now, first.BucketStart);
            Assert.Equal((3, 2, 1), (first.Runs, first.Done, first.Unavailable));   // DB-Zeile + 2 neue
            Assert.Equal(10 + 20 + 15, first.RunSeconds);
            Assert.Equal(90, first.DoneJobSeconds);
            Assert.Equal((4_000_000L, 20L), (first.Nodes, first.NodeSeconds));
            Assert.Equal(Now.AddMinutes(10), rows[1].BucketStart);
            Assert.Equal(1, rows[1].Timeouts);
            Assert.Equal((RheB, 1), (rows[2].EngineId, rows[2].Interrupted));
        }
    }

    [Fact]
    public void BucketOf_RoundsDownToTenMinutes()
    {
        Assert.Equal(new DateTime(2026, 11, 10, 12, 10, 0, DateTimeKind.Utc),
            EngineThroughputRecorder.BucketOf(new DateTime(2026, 11, 10, 12, 19, 59, DateTimeKind.Utc)));
    }

    [Fact]
    public void Worker_ClassifiesRunEnds_ForTheCount()
    {
        Assert.Equal(EngineRunOutcome.Done, AnalysisJobWorker.OutcomeOf(RunOutcome.Done()));
        Assert.Equal(EngineRunOutcome.Failed, AnalysisJobWorker.OutcomeOf(RunOutcome.Fail("x")));
        Assert.Equal(EngineRunOutcome.Interrupted, AnalysisJobWorker.OutcomeOf(RunOutcome.Pause(null)));
        Assert.Equal(EngineRunOutcome.Retried, AnalysisJobWorker.OutcomeOf(RunOutcome.Pause("Stream vorzeitig beendet")));
    }
}
