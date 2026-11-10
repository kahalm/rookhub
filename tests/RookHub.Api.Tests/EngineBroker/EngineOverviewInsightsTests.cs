using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs.EngineBroker;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Tests.EngineBroker;

/// <summary>
/// Admin-Reiter „Engines“, Übersichten (0.953.0): Engine-Tokens, Zeitpläne je Maschine, kN/s normal/Schonzeit,
/// Leerlauf-Grund, Hinweise, Maschinen-Gruppen, Abschätzung. Feste Ids im 99xxxx-Bereich, je Test eigene DB.
/// </summary>
public class EngineOverviewInsightsTests : IDisposable
{
    // Dienstag 10.11.2026 12:00 UTC = 13:00 Wien — in der RookHub-Sperrzeit „Mon-Thu 08:00-17:00“.
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private const int Owner = 990901, Other = 990902;
    private const string Live = "rhe_inLive000001", Bg1 = "rhe_inBg10000001", Bg2 = "rhe_inBg20000002",
        Pc = "rhe_inPc00000001", Orphan = "rhe_inOr00000009";

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ServiceProvider _sp;
    private readonly EngineHub _hub;
    private readonly EngineSelectorDirectory _directory;
    private readonly LocalBrokerOptions _options = new();
    private readonly EngineThroughputRecorder _recorder = new();
    private readonly QuietHours _quiet = new("Mon-Thu 08:00-17:00", "Europe/Vienna");

    public EngineOverviewInsightsTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        _sp = services.BuildServiceProvider();
        _hub = new EngineHub(_options, () => Now, startSweeper: false);
        _directory = new EngineSelectorDirectory(_sp.GetRequiredService<IServiceScopeFactory>(), () => Now);
    }

    public void Dispose()
    {
        _hub.Dispose();
        _sp.Dispose();
    }

    private AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options);

    private EngineOverviewService Service(AppDbContext db, bool paused = false) => new(db, new MemoryCache(new MemoryCacheOptions()),
        _hub, _directory, _options, new EngineActivityTracker(), new AnalysisJobLive(), _recorder,
        new BackgroundPause(_sp.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [BackgroundPause.ConfigKey] = paused ? "true" : null }).Build(),
            NullLogger<BackgroundPause>.Instance),
        new ConfigurationBuilder().Build(), _quiet);

    private static ExternalEngineRegistration Reg(string id, int user, string name, int? token, DateTime? seen) => new()
    {
        Id = id, UserId = user, Name = name, ClientSecret = "cs", ProviderSelector = "sel" + id, MaxThreads = 4, MaxHash = 256,
        Variants = "chess", CreatedAt = Now.AddDays(-3), LastSeenAt = seen, ApiTokenId = token,
    };

    private static AnalysisJob Job(int id, string engine, AnalysisJobStatus status, bool batch, string? error = null) => new()
    {
        Id = id, UserId = Owner, EngineId = engine, Status = status, Priority = batch ? JobPriority.League : JobPriority.Interactive,
        Batch = batch, Background = true, Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", TargetDepth = 20, MultiPv = 1,
        CreatedAt = Now.AddHours(-26), UpdatedAt = Now.AddHours(-1), LastError = error,
    };

    private async Task SeedAsync(string ownerList)
    {
        await using var db = Db();
        db.AppUsers.AddRange(
            new AppUser { Id = Owner, Username = "in-owner", PasswordHash = "x" },
            new AppUser { Id = Other, Username = "in-other", PasswordHash = "x" });
        db.UserApiTokens.AddRange(
            new UserApiToken { Id = 990951, UserId = Owner, Name = "Server-Token", TokenHash = "SECRET-HASH-990951", Prefix = "rkh_abcd1234",
                Scope = ApiTokenService.EngineScope, CreatedAt = Now.AddDays(-60), LastUsedAt = Now.AddMinutes(-3) },
            new UserApiToken { Id = 990952, UserId = Owner, Name = "Alter Laptop", TokenHash = "SECRET-HASH-990952", Prefix = "rkh_old00000",
                Scope = ApiTokenService.EngineScope, CreatedAt = Now.AddDays(-90), LastUsedAt = Now.AddDays(-40) },
            new UserApiToken { Id = 990953, UserId = Owner, Name = "Extension", TokenHash = "SECRET-HASH-990953", Prefix = "rkh_ext00000",
                Scope = "extension", CreatedAt = Now.AddDays(-5) });
        db.ExternalEngineRegistrations.AddRange(
            Reg(Live, Owner, "RookHub Server", 990951, Now.AddSeconds(-5)),
            Reg(Bg1, Owner, "RookHub Server Hintergrund 1", 990951, Now.AddSeconds(-5)),
            Reg(Bg2, Owner, "RookHub Server Hintergrund 2", 990951, Now.AddSeconds(-5)),
            Reg(Pc, Owner, "RookHub PC Hintergrund 1", null, Now.AddDays(-1)),
            Reg(Orphan, Owner, "RookHub Server Hintergrund 3", 990951, Now.AddSeconds(-5)));
        // Server meldet „werktags 08-17 Uhr nur 50 %“: 3 Engines (Live = Platz 1) → bei 50 % laufen 1 + 1.
        foreach (var (name, slot) in new[] { ("RookHub Server", 1), ("RookHub Server Hintergrund 1", 2), ("RookHub Server Hintergrund 2", 3) })
            db.EngineClientSchedules.Add(new EngineClientSchedule
            {
                UserId = Owner, EngineName = name, Slot = slot, Total = 3, Scope = "background",
                Rule = "Mo-Fr 08:00-17:00 50%", TimeZone = "Europe/Vienna", ReportedAt = Now.AddHours(-2),
            });
        db.LichessEngineCredentials.Add(new LichessEngineCredential { UserId = Owner, BackgroundEngineIds = ownerList });
        db.AnalysisJobs.AddRange(
            Job(990961, Pc, AnalysisJobStatus.Paused, batch: true, error: "Broker antwortete 503"),
            Job(990962, Pc, AnalysisJobStatus.Queued, batch: true),
            Job(990963, Bg2, AnalysisJobStatus.Queued, batch: true));
        // Körbe: Bg1 normal (Montag 20:00 Wien, 100 %) 2 000 kN/s, Schonzeit (heute 10:00 Wien, 50 %) 1 000 kN/s.
        db.EngineThroughputBuckets.AddRange(
            new EngineThroughputBucket { EngineId = Bg1, BucketStart = new DateTime(2026, 11, 9, 19, 0, 0, DateTimeKind.Utc), Runs = 1, Done = 1,
                RunSeconds = 10, Nodes = 20_000_000, NodeSeconds = 10 },
            new EngineThroughputBucket { EngineId = Bg1, BucketStart = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc), Runs = 1, Done = 1,
                RunSeconds = 10, Nodes = 10_000_000, NodeSeconds = 10 },
            // Pc ohne Zeitplan: Korb in der RookHub-Sperrzeit → Schonzeit.
            new EngineThroughputBucket { EngineId = Pc, BucketStart = Now.AddHours(-2), Runs = 1, Done = 1, RunSeconds = 4, Nodes = 4_000_000, NodeSeconds = 4 });
        for (var i = 0; i < 12; i++)
            db.EngineThroughputBuckets.Add(new EngineThroughputBucket
            {
                EngineId = Live, BucketStart = Now.AddMinutes(-50).AddSeconds(i), Runs = 1, Done = 1, RunSeconds = 1,
            });
        await db.SaveChangesAsync();
    }

    private async Task<EngineOverviewDto> BuildAsync(bool paused = false)
    {
        await using var db = Db();
        return await Service(db, paused).BuildAsync(Now, CancellationToken.None);
    }

    // ── reine Bausteine ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("RookHub Gross Hintergrund 12", "RookHub Gross")]
    [InlineData("RookHub Gross", "RookHub Gross")]
    [InlineData("Laptop Background 3", "Laptop")]
    [InlineData("Spark Lc0", "Spark Lc0")]
    [InlineData("PC 2", "PC")]
    public void Stem_StripsRoleAndNumber(string name, string stem) => Assert.Equal(stem, EngineOverviewSchedules.Stem(name));

    [Fact]
    public void MachineKeys_UnknownTokenDoesNotSplit_TwoKnownTokensDo()
    {
        var keys = EngineOverviewSchedules.MachineKeys([
            (Owner, "rookhub", 990951, "RookHub Gross"),
            (Owner, "rookhub", null, "RookHub Gross Hintergrund 1"),        // noch nicht zugeordnet → dieselbe Maschine
            (Owner, "rookhub", 990951, "RookHub Gross Hintergrund 2"),
            (Owner, "rookhub", 990951, "Laptop"),
            (Owner, "rookhub", 990952, "Laptop Hintergrund 1"),             // zweiter bekannter Token → zwei Maschinen
            (Owner, "rookhub", null, "Laptop Hintergrund 2"),
            (Other, "rookhub", null, "RookHub Gross"),                      // anderes Konto
        ]);
        Assert.Equal(1, keys.Take(3).Select(k => k.Key).Distinct().Count());
        Assert.All(keys.Take(3), k => Assert.False(k.Split));
        Assert.Equal([$"{Owner}|Laptop|t990951", $"{Owner}|Laptop|t990952", $"{Owner}|Laptop|t-"], keys.Skip(3).Take(3).Select(k => k.Key));
        Assert.All(keys.Skip(3).Take(3), k => Assert.True(k.Split));
        Assert.NotEqual(keys[0].Key, keys[6].Key);
    }

    [Fact]
    public async Task Machines_MixedKnownAndUnknownToken_OneMachine_TwoTokens_NamedApart()
    {
        await using (var db = Db())
        {
            db.AppUsers.Add(new AppUser { Id = Owner, Username = "in-owner", PasswordHash = "x" });
            db.UserApiTokens.AddRange(
                new UserApiToken { Id = 990951, UserId = Owner, Name = "testprx3", TokenHash = "H1", Prefix = "rkh_a", Scope = ApiTokenService.EngineScope,
                    CreatedAt = Now.AddDays(-1), LastUsedAt = Now.AddMinutes(-2) },
                new UserApiToken { Id = 990952, UserId = Owner, Name = "laptop2026", TokenHash = "H2", Prefix = "rkh_b", Scope = ApiTokenService.EngineScope,
                    CreatedAt = Now.AddDays(-1), LastUsedAt = Now.AddMinutes(-2) });
            // Wie Prod 10.11.: 2 Gross-Engines nach dem Deploy neu gemeldet (Token bekannt), 2 von vorher (null).
            db.ExternalEngineRegistrations.AddRange(
                Reg("rhe_inGross00001", Owner, "RookHub Gross", null, Now.AddSeconds(-5)),
                Reg("rhe_inGross00002", Owner, "RookHub Gross Hintergrund 1", null, Now.AddSeconds(-5)),
                Reg("rhe_inGross00003", Owner, "RookHub Gross Hintergrund 2", 990951, Now.AddSeconds(-5)),
                Reg("rhe_inGross00004", Owner, "RookHub Gross Hintergrund 3", 990951, Now.AddSeconds(-5)),
                Reg("rhe_inLap000001", Owner, "Laptop", 990951, Now.AddSeconds(-5)),
                Reg("rhe_inLap000002", Owner, "Laptop Hintergrund 1", 990952, Now.AddSeconds(-5)));
            await db.SaveChangesAsync();
        }
        var dto = await BuildAsync();

        var gross = Assert.Single(dto.Machines, m => m.Name.StartsWith("RookHub Gross", StringComparison.Ordinal));
        Assert.Equal(("RookHub Gross", 4, 990951, "testprx3"), (gross.Name, gross.Total, gross.ApiTokenId, gross.TokenName));
        var laptops = dto.Machines.Where(m => m.Name.StartsWith("Laptop", StringComparison.Ordinal)).Select(m => m.Name).Order().ToList();
        Assert.Equal(["Laptop · laptop2026", "Laptop · testprx3"], laptops);
        // Token-Tabelle: nur die Engines, die ihm wirklich zugeordnet sind.
        Assert.Equal(3, dto.Tokens.Single(t => t.Id == 990951).Engines.Count);
    }

    [Fact]
    public void MachineSchedule_PercentNow_OpenNow_AndNextChange()
    {
        var rows = Enumerable.Range(1, 3).Select(slot => new EngineClientSchedule
        {
            Slot = slot, Total = 3, Scope = "background", Rule = "Mo-Fr 08:00-17:00 50%", TimeZone = "Europe/Vienna", ReportedAt = Now,
        }).ToList();
        var s = EngineOverviewSchedules.MachineSchedule(rows, Now)!;
        Assert.Equal((50, 3, 2), (s.PercentNow, s.Reported, s.OpenNow));                 // Platz 1 + 1 Hintergrund
        Assert.Equal(new DateTime(2026, 11, 10, 16, 0, 0, DateTimeKind.Utc), s.NextChangeAt);   // 17:00 Wien
        Assert.Equal(100, s.NextPercent);
        Assert.Null(EngineOverviewSchedules.MachineSchedule([], Now));
    }

    [Fact]
    public void IsReduced_ScheduleBelow100_OrQuietHoursWithoutSchedule()
    {
        var s = new EngineClientSchedule { Slot = 2, Total = 3, Scope = "background", Rule = "Mo-Fr 08:00-17:00 50%", TimeZone = "Europe/Vienna" };
        Assert.True(EngineOverviewSchedules.IsReduced(s, _quiet, Now));
        Assert.False(EngineOverviewSchedules.IsReduced(s, _quiet, Now.AddHours(8)));
        Assert.True(EngineOverviewSchedules.IsReduced(null, _quiet, Now));
        Assert.False(EngineOverviewSchedules.IsReduced(null, _quiet, Now.AddDays(4)));     // Samstag
        Assert.False(EngineOverviewSchedules.IsReduced(null, null, Now));
    }

    [Fact]
    public void IdleReason_FollowsTheAssignmentOrder()
    {
        HashSet<string> none = [], both = ["e"];
        string R(bool paused = false, bool expl = false, bool inList = true, bool sched = false, HashSet<string>? all = null,
            HashSet<string>? batch = null, int waiting = 1, int nonBatch = 0) =>
            EngineOverviewSchedules.IdleReason("e", paused, expl, inList, sched, all ?? both, batch ?? both, waiting, nonBatch);
        Assert.Equal("paused", R(paused: true));
        Assert.Equal("explicitOnly", R(expl: true));
        Assert.Equal("notInList", R(inList: false));
        Assert.Equal("scheduleOff", R(sched: true, all: none, batch: none));
        Assert.Equal("nothingWaiting", R(waiting: 0));
        Assert.Equal("quietHours", R(batch: none));
        Assert.Equal("waitingElsewhere", R(batch: none, nonBatch: 1));
        Assert.Equal("waitingElsewhere", R());
    }

    // ── Übersicht ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tokens_OnlyEngineScope_WithEngines_NeverTheHash()
    {
        await SeedAsync($"{Live},{Bg1},{Bg2},{Pc}");
        var dto = await BuildAsync();

        Assert.Equal(["Alter Laptop", "Server-Token"], dto.Tokens.Select(t => t.Name));
        var server = dto.Tokens.Single(t => t.Id == 990951);
        Assert.Equal("rkh_abcd1234", server.Prefix);
        Assert.Equal(new[] { Live, Bg1, Bg2, Orphan }.Order(), server.Engines.Select(e => e.Id).Order());
        Assert.Empty(dto.Tokens.Single(t => t.Id == 990952).Engines);
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("SECRET-HASH", json);
        Assert.DoesNotContain("TokenHash", json);
        Assert.Contains(dto.Hints, h => h.Kind == "staleToken" && h.Name == "Alter Laptop");
        Assert.DoesNotContain(dto.Hints, h => h.Kind == "staleToken" && h.Name == "Server-Token");
    }

    [Fact]
    public async Task Machines_GroupByStemAndToken_WithScheduleAndSpeedSplit()
    {
        await SeedAsync($"{Live},{Bg1},{Bg2},{Pc}");
        var dto = await BuildAsync();

        var server = dto.Machines.Single(m => m.Name == "RookHub Server");
        Assert.Equal(new[] { Live, Bg1, Bg2, Orphan }.Order(), server.EngineIds.Order());
        Assert.Equal(("Server-Token", 4, 4, 16), (server.TokenName, server.Online, server.Total, server.Threads));
        Assert.NotNull(server.Schedule);
        Assert.Equal((50, 3, 2), (server.Schedule!.PercentNow, server.Schedule.Reported, server.Schedule.OpenNow));
        Assert.Equal(new EngineSpeedSplitDto(2000.0, 1000.0), server.Speed);

        var pc = dto.Machines.Single(m => m.Name == "RookHub PC");                  // anderer Stamm, ohne Token
        Assert.Null(pc.Schedule);
        Assert.Null(pc.ApiTokenId);
        Assert.Equal(new EngineSpeedSplitDto(null, 1000.0), pc.Speed);              // ohne Zeitplan: RookHub-Sperrzeit

        var bg1 = dto.Engines.Single(e => e.Id == Bg1);
        Assert.Equal((2, 3, true, 50), (bg1.Schedule!.Slot, bg1.Schedule.Total, bg1.Schedule.OpenNow, bg1.Schedule.PercentNow));
        Assert.False(dto.Engines.Single(e => e.Id == Bg2).Schedule!.OpenNow);       // Platz 3 bei 50 % aus
        Assert.Equal(new EngineSpeedSplitDto(2000.0, 1000.0), bg1.Speed);

        Assert.True(dto.Quiet.Enabled);
        Assert.True(dto.Quiet.QuietNow);
        Assert.Equal(new DateTime(2026, 11, 10, 16, 0, 0, DateTimeKind.Utc), dto.Quiet.QuietUntil);
        Assert.Contains(dto.Quiet.Windows, w => w.Day == 1 && w.From == "08:00" && w.To == "17:00");
        Assert.Equal(64, dto.BackgroundCap);
    }

    [Fact]
    public async Task IdleReasons_AndHints()
    {
        await SeedAsync($"{Live},{Bg1},{Bg2},{Pc}");
        var dto = await BuildAsync();

        // Bg1 frei, Zeitplan offen, aber es warten nur Stapel-Aufträge — die nimmt sie (Zeitplan ≠ Sperrzeit) → woanders.
        Assert.Equal("waitingElsewhere", dto.Engines.Single(e => e.Id == Bg1).IdleReason);
        Assert.Equal("notInList", dto.Engines.Single(e => e.Id == Orphan).IdleReason);
        Assert.Null(dto.Engines.Single(e => e.Id == Pc).IdleReason);                  // offline, nicht im Leerlauf

        var stuck = Assert.Single(dto.Hints, h => h.Kind == "stuckJobs");
        Assert.Equal((Pc, 2), (stuck.EngineId, stuck.Count));
        Assert.Equal("offline|Broker antwortete 503", stuck.Detail);
        Assert.Equal(Now.AddHours(-26), stuck.Since);
        var orphan = Assert.Single(dto.Hints, h => h.Kind == "notInList");
        Assert.Equal(("RookHub Server", 1, "RookHub Server Hintergrund 3"), (orphan.Name, orphan.Count, orphan.Detail));
        Assert.DoesNotContain(dto.Hints, h => h.Kind == "listCap");                   // 4 von 64

        Assert.Equal(0.3, dto.QueueEmptyInHours);                                     // 3 wartend ÷ 12 fertig/h
    }

    [Fact]
    public async Task Paused_AndListNearCap()
    {
        var ids = string.Join(',', new[] { Live, Bg1, Bg2, Pc }.Concat(Enumerable.Range(0, 30).Select(i => $"rhe_fill{i:D8}")));
        await SeedAsync(ids);
        var dto = await BuildAsync(paused: true);
        Assert.Equal("paused", dto.Engines.Single(e => e.Id == Bg1).IdleReason);
        var cap = Assert.Single(dto.Hints, h => h.Kind == "listCap");
        Assert.Equal((34, 64, "in-owner"), (cap.Count, cap.Max!.Value, cap.Name));
    }

    [Fact]
    public void Recorder_UsesSearchSecondsFromReportedNps()
    {
        _recorder.Record(Bg1, EngineRunOutcome.Done, runSeconds: 30, nodes: 10_000_000, doneJobSeconds: 30, Now, searchSeconds: 10);
        _recorder.Record(Bg2, EngineRunOutcome.Done, runSeconds: 30, nodes: 10_000_000, doneJobSeconds: 30, Now);
        var p = _recorder.Pending().ToDictionary(b => b.EngineId);
        Assert.Equal((30L, 10L), (p[Bg1].RunSeconds, p[Bg1].NodeSeconds));      // Tempo aus der Engine, nicht die Wanduhr
        Assert.Equal(30L, p[Bg2].NodeSeconds);                                   // ohne nps: Wanduhr wie bisher
    }

    [Fact]
    public async Task Registration_RemembersTheEngineToken()
    {
        await using var db = Db();
        db.AppUsers.Add(new AppUser { Id = Owner, Username = "in-owner", PasswordHash = "x" });
        await db.SaveChangesAsync();
        var svc = new ExternalEngineRegistrationService(db, _directory, NullLogger<ExternalEngineRegistrationService>.Instance);
        ExternalEngineRegistrationRequest Req() => new() { Name = "Laptop", MaxThreads = 2, MaxHash = 64, Variants = ["chess"], ProviderSecret = "in-provider-secret-01" };

        var first = (await svc.SaveAsync(Owner, null, Req(), apiTokenId: 990951)).Engine!;
        Assert.Equal(990951, first.ApiTokenId);
        await svc.SaveAsync(Owner, null, Req());                                  // ohne Token-Claim: bleibt
        Assert.Equal(990951, (await db.ExternalEngineRegistrations.AsNoTracking().SingleAsync()).ApiTokenId);
        await svc.SaveAsync(Owner, first.Id, Req(), apiTokenId: 990952);          // anderer Token: ummelden
        Assert.Equal(990952, (await db.ExternalEngineRegistrations.AsNoTracking().SingleAsync()).ApiTokenId);
    }
}
