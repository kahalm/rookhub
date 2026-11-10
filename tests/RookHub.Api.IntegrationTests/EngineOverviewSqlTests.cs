using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Admin-Seite „Engines" (0.946.0) gegen ECHTES MariaDB: die Abfragen von <see cref="EngineOverviewService"/> (GroupBy mit
/// Count/Min über die offenen Aufträge, Sum je Engine über die Durchsatz-Körbe, IN-Listen über Stellungen/Partien/Nutzer)
/// und das Schreiben/Addieren/Aufräumen von <see cref="EngineThroughputRecorder"/> auf dem zusammengesetzten Schlüssel.
/// Die Unit-Tests laufen gegen InMemory und prüfen die SQL-Übersetzung nicht. Feste Ids im 99xxxx-Bereich.
/// </summary>
public class EngineOverviewSqlTests(EngineOverviewSqlFixture fixture) : IAsyncLifetime, IClassFixture<EngineOverviewSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private const int Owner = 990101, Other = 990102;
    private const string RheA = "rhe_sqlA00000001", RheB = "rhe_sqlB00000002", Eei = "eei_sqlLichess01";

    private EngineOverviewService Service(AppDbContext db, EngineThroughputRecorder recorder, ServiceProvider sp)
    {
        var options = new LocalBrokerOptions();
        var hub = new EngineHub(options, () => Now, startSweeper: false);
        var scopes = sp.GetRequiredService<IServiceScopeFactory>();
        return new EngineOverviewService(db, new MemoryCache(new MemoryCacheOptions()), hub,
            new EngineSelectorDirectory(scopes, () => Now), options, new EngineActivityTracker(), new AnalysisJobLive(), recorder,
            new BackgroundPause(scopes, new ConfigurationBuilder().Build(), NullLogger<BackgroundPause>.Instance),
            new ConfigurationBuilder().Build());
    }

    private ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseMySql(fixture.Schema.ConnectionString, DbServerVersion.Current));
        return services.BuildServiceProvider();
    }

    private static AnalysisJob Job(int id, int user, string engine, AnalysisJobStatus status, JobPriority prio, DateTime created) => new()
    {
        Id = id, UserId = user, EngineId = engine, Status = status, Priority = prio, Fen = "8/8/8/8/8/8/8/K6k w - - 0 1",
        TargetDepth = 30, MultiPv = 3, CreatedAt = created, UpdatedAt = created, ReachedDepth = 12, CurrentDepth = 14,
        CurrentNps = 500_000, SecondsSpent = 40, LastRunAt = Now.AddSeconds(-20),
    };

    [MySqlFact]
    public async Task Build_TranslatesAllQueries_AndCountsRight()
    {
        await using (var db = fixture.Schema.NewContext())
        {
            db.AppUsers.AddRange(
                new AppUser { Id = Owner, Username = "sql-owner", PasswordHash = "x" },
                new AppUser { Id = Other, Username = "sql-other", PasswordHash = "x" });
            db.ExternalEngineRegistrations.AddRange(
                new ExternalEngineRegistration { Id = RheA, UserId = Owner, Name = "Server 1", ClientSecret = "cs", ProviderSelector = "selA",
                    MaxThreads = 4, MaxHash = 256, Variants = "chess", CreatedAt = Now.AddDays(-3), LastSeenAt = Now.AddSeconds(-5) },
                new ExternalEngineRegistration { Id = RheB, UserId = Other, Name = "Alter PC", ClientSecret = "cs", ProviderSelector = "selB",
                    MaxThreads = 2, MaxHash = 64, Variants = "chess", CreatedAt = Now.AddDays(-9), LastSeenAt = Now.AddDays(-2) });
            db.LichessEngineCredentials.AddRange(
                new LichessEngineCredential { UserId = Owner, BackgroundEngineIds = $"{RheA},{Eei}", ShareAsHouseEngine = true },
                new LichessEngineCredential { UserId = Other, BackgroundEngineIds = RheA });
            db.AnalysisJobs.AddRange(
                Job(990201, Owner, RheA, AnalysisJobStatus.Running, JobPriority.League, Now.AddHours(-1)),
                Job(990202, Owner, RheA, AnalysisJobStatus.Queued, JobPriority.League, Now.AddHours(-3)),
                Job(990203, Other, RheA, AnalysisJobStatus.Queued, JobPriority.Interactive, Now.AddMinutes(-5)),
                Job(990204, Owner, Eei, AnalysisJobStatus.Paused, JobPriority.Master, Now.AddHours(-5)),
                Job(990205, Owner, RheB, AnalysisJobStatus.Queued, JobPriority.Master, Now.AddHours(-2)),
                Job(990206, Owner, RheA, AnalysisJobStatus.Done, JobPriority.League, Now.AddDays(-10)),
                Job(990207, Owner, RheB, AnalysisJobStatus.Failed, JobPriority.Master, Now.AddDays(-10)));
            await db.SaveChangesAsync();
            db.GameAnalyses.AddRange(
                new GameAnalysis { Id = 990301, UserId = Owner, Pgn = "1. e4", StartFen = "x", White = "Weiß", Black = "Schwarz",
                    Status = GameAnalysisStatus.Running, CreatedAt = Now },
                new GameAnalysis { Id = 990302, UserId = Owner, Pgn = "1. d4", StartFen = "x", Status = GameAnalysisStatus.Pending, CreatedAt = Now },
                new GameAnalysis { Id = 990303, UserId = Owner, Pgn = "1. c4", StartFen = "x", Status = GameAnalysisStatus.Done, CreatedAt = Now });
            await db.SaveChangesAsync();
            db.GameAnalysisPositions.AddRange(
                new GameAnalysisPosition { Id = 990401, GameAnalysisId = 990301, Ply = 17, Fen = "x", GameMoveUci = "e2e4", GameMoveSan = "e4", AnalysisJobId = 990201 },
                new GameAnalysisPosition { Id = 990402, GameAnalysisId = 990301, Ply = 18, Fen = "x", GameMoveUci = "e7e5", GameMoveSan = "e5" });
            db.EngineThroughputBuckets.AddRange(
                new EngineThroughputBucket { EngineId = RheA, BucketStart = Now.AddMinutes(-30), Runs = 5, Done = 4, Unavailable = 1, RunSeconds = 200,
                    DoneJobSeconds = 160, Nodes = 100_000_000, NodeSeconds = 200 },
                new EngineThroughputBucket { EngineId = RheA, BucketStart = Now.AddHours(-5), Runs = 10, Done = 10, RunSeconds = 400, DoneJobSeconds = 400 },
                new EngineThroughputBucket { EngineId = RheA, BucketStart = Now.AddDays(-3), Runs = 2, Done = 1, Failed = 1, RunSeconds = 50 },
                new EngineThroughputBucket { EngineId = Eei, BucketStart = Now.AddHours(-2), Runs = 3, Done = 2, Timeouts = 1, RunSeconds = 90, DoneJobSeconds = 70 });
            await db.SaveChangesAsync();
        }

        await using var sp = Provider();
        await using var ctx = fixture.Schema.NewContext();
        var dto = await Service(ctx, new EngineThroughputRecorder(), sp).BuildAsync(Now, CancellationToken.None);

        Assert.Equal([RheA, Eei, RheB], dto.Engines.Select(e => e.Id));   // rechnet, unbekannt (Lichess), offline
        var a = dto.Engines[0];
        Assert.Equal(("working", "Server 1", "sql-owner", true, true), (a.State, a.Name, a.OwnerName, a.Online, a.HouseEngine));
        Assert.Equal(["sql-other", "sql-owner"], a.BackgroundFor.Select(u => u.Name));
        Assert.Equal((2, 0, Now.AddHours(-3)), (a.WaitingQueued, a.WaitingPaused, a.OldestWaitingSince));
        Assert.Equal((990201, "league", 990301, 17, "Weiß – Schwarz"),
            (a.Current!.JobId, a.Current.Kind, a.Current.GameAnalysisId, a.Current.Ply, a.Current.GameTitle));
        Assert.Equal((14, 500_000, 40), (a.Current.Depth, a.Current.Nps, a.Current.Seconds));   // ohne Lauf im Speicher: DB-Stand
        Assert.Equal((4, 1, 40.0, 500.0), (a.Throughput.Hour.Done, a.Throughput.Hour.Unavailable, a.Throughput.Hour.AvgJobSeconds,
            a.Throughput.Hour.KiloNodesPerSecond));
        Assert.Equal((14, 15, 1), (a.Throughput.Day.Done, a.Throughput.Week.Done, a.Throughput.Week.Failed));
        Assert.Equal(650, a.Throughput.Week.BusySeconds);

        var lichess = dto.Engines[1];
        Assert.Equal(("lichess", "unknown", false, 1), (lichess.Source, lichess.State, lichess.Registered, lichess.WaitingPaused));
        Assert.Equal((0, 2, 1), (lichess.Throughput.Hour.Done, lichess.Throughput.Day.Done, lichess.Throughput.Day.Timeouts));

        var off = dto.Engines[2];
        Assert.Equal(("offline", false, 1, "sql-other"), (off.State, off.Online, off.WaitingQueued, off.OwnerName));

        Assert.Equal((3, 1, 1), (dto.Queue.Queued, dto.Queue.Paused, dto.Queue.Running));
        Assert.Equal(Now.AddHours(-5), dto.Queue.OldestWaitingSince);
        Assert.Equal(["interactive", "league", "master"], dto.Queue.ByKind.Select(k => k.Kind));
        var league = dto.Queue.ByKind.Single(k => k.Kind == "league");
        Assert.Equal((1, 0, 1, Now.AddHours(-3)), (league.Queued, league.Paused, league.Running, league.OldestWaitingSince));
        var master = dto.Queue.ByKind.Single(k => k.Kind == "master");
        Assert.Equal((1, 1, 0, Now.AddHours(-5)), (master.Queued, master.Paused, master.Running, master.OldestWaitingSince));
        Assert.Equal((1, 1), (dto.Queue.GameAnalysesPending, dto.Queue.GameAnalysesRunning));
        Assert.Equal((4, 16, 17), (dto.Totals.Hour.Done, dto.Totals.Day.Done, dto.Totals.Week.Done));
        Assert.Equal((1, 1, 1), (dto.Totals.Week.Unavailable, dto.Totals.Week.Timeouts, dto.Totals.Week.Failed));
        Assert.False(dto.Pause.Paused);
    }

    [MySqlFact]
    public async Task Build_OnEmptyTables_DoesNotThrow()
    {
        await using var sp = Provider();
        await using var ctx = fixture.Schema.NewContext();
        var dto = await Service(ctx, new EngineThroughputRecorder(), sp).BuildAsync(Now, CancellationToken.None);
        Assert.Empty(dto.Engines);
        Assert.Empty(dto.Queue.ByKind);
        Assert.Equal((0, 0, 0, 0, 0), (dto.Queue.Queued, dto.Queue.Paused, dto.Queue.Running, dto.Queue.GameAnalysesPending, dto.Queue.GameAnalysesRunning));
        Assert.Null(dto.Queue.OldestWaitingSince);
        Assert.Equal(0, dto.Totals.Week.Runs);
        Assert.Null(dto.Totals.Week.AvgJobSeconds);
    }

    [MySqlFact]
    public async Task Recorder_FlushTwiceIntoSameBucket_AddsOnCompositeKey_ThenPrunes()
    {
        var recorder = new EngineThroughputRecorder();
        var old = Now.AddDays(-EngineThroughputBucket.RetentionDays - 1);
        await using (var db = fixture.Schema.NewContext())
        {
            db.EngineThroughputBuckets.Add(new EngineThroughputBucket { EngineId = RheA, BucketStart = old, Runs = 7 });
            await db.SaveChangesAsync();
        }

        recorder.Record(RheA, EngineRunOutcome.Done, 20, 4_000_000, 90, Now.AddMinutes(1));
        recorder.Record(RheB, EngineRunOutcome.Unavailable, 15, 0, 0, Now.AddMinutes(2));
        await using (var db = fixture.Schema.NewContext())
            await recorder.FlushAsync(db, Now.AddMinutes(3), CancellationToken.None);   // fügt ein, räumt den alten Korb weg

        recorder.Record(RheA, EngineRunOutcome.Done, 30, 6_000_000, 110, Now.AddMinutes(4));
        recorder.Record(RheA, EngineRunOutcome.Timeout, 300, 0, 0, Now.AddMinutes(5));
        await using (var db = fixture.Schema.NewContext())
            await recorder.FlushAsync(db, Now.AddMinutes(6), CancellationToken.None);   // addiert auf die vorhandene Zeile
        Assert.Empty(recorder.Pending());

        await using (var db = fixture.Schema.NewContext())
        {
            var rows = await db.EngineThroughputBuckets.AsNoTracking().OrderBy(b => b.EngineId).ThenBy(b => b.BucketStart).ToListAsync();
            Assert.Equal(2, rows.Count);
            var a = rows[0];
            Assert.Equal((RheA, Now), (a.EngineId, a.BucketStart));
            Assert.Equal((3, 2, 1), (a.Runs, a.Done, a.Timeouts));
            Assert.Equal((350L, 200L, 10_000_000L, 50L), (a.RunSeconds, a.DoneJobSeconds, a.Nodes, a.NodeSeconds));
            Assert.Equal((RheB, 1, 1, 15L), (rows[1].EngineId, rows[1].Runs, rows[1].Unavailable, rows[1].RunSeconds));
        }

        // Ein zweiter Recorder (Neustart) räumt beim ersten Schreiben ebenfalls auf, auch ohne neue Zuwächse.
        await using (var db = fixture.Schema.NewContext())
        {
            db.EngineThroughputBuckets.Add(new EngineThroughputBucket { EngineId = RheB, BucketStart = old, Runs = 1 });
            await db.SaveChangesAsync();
            await new EngineThroughputRecorder().FlushAsync(db, Now, CancellationToken.None);
            Assert.Equal(2, await db.EngineThroughputBuckets.CountAsync());
            Assert.False(await db.EngineThroughputBuckets.AnyAsync(b => b.BucketStart < Now.AddDays(-EngineThroughputBucket.RetentionDays)));
        }
    }
}

public sealed class EngineOverviewSqlFixture() : MariaDbClassFixture("engov", withApp: false);
