using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Tests;

/// <summary>
/// Admin-Schalter „Hintergrundjobs pausieren": Zustand in der Datenbank (übersteht Neustarts), gecacht, per
/// Konfiguration erzwingbar; <see cref="PeriodicWorker"/> überspringt Takte, nicht pausierbare Dienste laufen weiter,
/// der Wechsel wird einmal geloggt; der Endpunkt ist Admins vorbehalten.
/// </summary>
public class BackgroundPauseTests : IAsyncDisposable
{
    private readonly ServiceProvider _sp;

    public BackgroundPauseTests() => _sp = BuildProvider(forced: false);

    public async ValueTask DisposeAsync() => await _sp.DisposeAsync();

    private static ServiceProvider BuildProvider(bool forced, string? dbName = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [BackgroundPause.ConfigKey] = forced ? "true" : null,
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        var name = dbName ?? Guid.NewGuid().ToString();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddSingleton<BackgroundPause>();
        return services.BuildServiceProvider();
    }

    private BackgroundPause Pause => _sp.GetRequiredService<BackgroundPause>();

    private sealed class MutableTime(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestWorker(IServiceScopeFactory scopes, ILogger logger, bool pausable,
        Func<int, CancellationToken, Task> step) : PeriodicWorker(scopes, logger)
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        protected override WorkerSchedule Schedule => WorkerSchedule.Every(TimeSpan.FromMilliseconds(20));
        protected override WorkerStart Start => WorkerStart.Immediately;
        protected override bool Pausable => pausable;
        protected override void LogFailure(Exception ex) => Logger.LogError(ex, "Testdienst Pause: Durchlauf fehlgeschlagen");
        protected override Task StepAsync(IServiceProvider services, CancellationToken ct)
            => step(Interlocked.Increment(ref _calls), ct);
    }

    // ── Zustand ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Default_IsRunning()
    {
        var state = await Pause.GetAsync();
        Assert.False(state.Paused);
        Assert.False(state.ForcedByConfig);
        Assert.Null(state.Since);
        Assert.Null(state.By);
    }

    [Fact]
    public async Task SetAsync_PersistsInDb_AndSurvivesARestart()
    {
        var dbName = Guid.NewGuid().ToString();
        await using (var first = BuildProvider(forced: false, dbName))
        {
            var state = await first.GetRequiredService<BackgroundPause>().SetAsync(true, 7, "kahalm");
            Assert.True(state.Paused);
            Assert.Equal("kahalm", state.By);
            Assert.NotNull(state.Since);
        }

        // „Neustart": neuer Prozess-Singleton, gleiche Datenbank.
        await using var second = BuildProvider(forced: false, dbName);
        var again = await second.GetRequiredService<BackgroundPause>().GetAsync();
        Assert.True(again.Paused);
        Assert.Equal("kahalm", again.By);

        using var scope = second.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SystemSettings.SingleAsync();
        Assert.Equal(BackgroundPause.SettingKey, row.Key);
        Assert.Equal("true", row.Value);
        Assert.Equal(7, row.UpdatedByUserId);
    }

    [Fact]
    public async Task ConfigForcesPause_RegardlessOfDb_AndTheSwitchCannotLiftIt()
    {
        await using var sp = BuildProvider(forced: true);
        var pause = sp.GetRequiredService<BackgroundPause>();

        Assert.True(await pause.IsPausedAsync());
        var state = await pause.SetAsync(false, 1, "admin");
        Assert.True(state.Paused);
        Assert.True(state.ForcedByConfig);
        Assert.False(state.PausedInDb);
        Assert.True(pause.IsPaused);
    }

    [Fact]
    public async Task DbChange_IsSeenOnlyAfterTheCacheExpires()
    {
        var time = new MutableTime(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        var pause = new BackgroundPause(_sp.GetRequiredService<IServiceScopeFactory>(), _sp.GetRequiredService<IConfiguration>(),
            NullLogger<BackgroundPause>.Instance, time);
        Assert.False(await pause.IsPausedAsync());

        // Direkt in der Datenbank umgelegt (z. B. von der zweiten API-Instanz) — der Merker hält noch.
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SystemSettings.Add(new SystemSetting { Key = BackgroundPause.SettingKey, Value = "true", UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        time.Now += TimeSpan.FromSeconds(4);
        Assert.False(await pause.IsPausedAsync());

        time.Now += TimeSpan.FromSeconds(2);
        Assert.True(await pause.IsPausedAsync());
    }

    [Fact]
    public async Task UnreadableDb_KeepsTheLastState_AndWarnsOnce()
    {
        var services = new ServiceCollection();   // ohne AppDbContext: jedes Lesen scheitert
        await using var sp = services.BuildServiceProvider();
        var log = new CapturingLogger<BackgroundPause>();
        var time = new MutableTime(DateTimeOffset.UtcNow);
        var pause = new BackgroundPause(sp.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), log, time);

        Assert.False(await pause.IsPausedAsync());
        time.Now += TimeSpan.FromSeconds(10);
        Assert.False(await pause.IsPausedAsync());

        Assert.Single(log.Events, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SetAsync_RaisesChanged()
    {
        bool? seen = null;
        Pause.Changed += p => seen = p;
        await Pause.SetAsync(true, null, null);
        Assert.True(seen);
    }

    // ── Wirkung auf die Dienste ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PeriodicWorker_SkipsStepsWhilePaused_AndRunsAgainAfterResume()
    {
        await Pause.SetAsync(true, 1, "admin");
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new TestWorker(_sp.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance, pausable: true,
            (_, _) => { ran.TrySetResult(); return Task.CompletedTask; });

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(300);
            Assert.Equal(0, worker.Calls);                  // pausiert: kein Takt
            Assert.Null(worker.LastTickUtc);

            await Pause.SetAsync(false, 1, "admin");
            await ran.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(worker.LastTickUtc);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task NonPausableWorker_KeepsRunningWhilePaused()
    {
        await Pause.SetAsync(true, 1, "admin");
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new TestWorker(_sp.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance, pausable: false,
            (call, _) => { if (call >= 2) ran.TrySetResult(); return Task.CompletedTask; });

        await worker.StartAsync(CancellationToken.None);
        try { await ran.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { await worker.StopAsync(CancellationToken.None); }
        Assert.False(worker.PausableByAdmin);
    }

    [Fact]
    public void RetentionWorkers_AreNotPausable_WorkersByDefaultAre()
    {
        var scopes = _sp.GetRequiredService<IServiceScopeFactory>();
        Assert.False(new NotificationRetentionScheduler(scopes, NullLogger<NotificationRetentionScheduler>.Instance).PausableByAdmin);
        Assert.True(new TestWorker(scopes, NullLogger.Instance, pausable: true, (_, _) => Task.CompletedTask).PausableByAdmin);
    }

    [Fact]
    public async Task PauseGate_LogsOnlyTheChange_NotEverySkippedTick()
    {
        var log = new CapturingLogger<BackgroundPauseTests>();
        var gate = new PauseGate("Testdienst", log).Use(Pause);

        await Pause.SetAsync(true, null, "admin");
        for (var i = 0; i < 3; i++) Assert.True(await gate.ShouldSkipAsync(null, CancellationToken.None));
        await Pause.SetAsync(false, null, "admin");
        for (var i = 0; i < 2; i++) Assert.False(await gate.ShouldSkipAsync(null, CancellationToken.None));

        var lines = log.Events.Select(e => e.Message).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Contains("pausiert", lines[0]);
        Assert.Contains("laufen wieder", lines[1]);
    }

    [Fact]
    public async Task PauseGate_WithoutRegisteredSwitch_NeverSkips()
    {
        await using var sp = new ServiceCollection().BuildServiceProvider();
        var gate = new PauseGate("Testdienst", NullLogger.Instance);
        Assert.False(await gate.ShouldSkipAsync(sp.GetRequiredService<IServiceScopeFactory>(), CancellationToken.None));
    }

    // ── Endpunkt ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Endpoint_IsAdminOnly()
    {
        var type = typeof(BackgroundJobsController);
        var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("Admin", authorize!.Roles);
        Assert.Equal("api/admin/background-jobs", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.DoesNotContain(type.GetMethods(), m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null);
        Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public async Task Endpoint_SetsThePause_WithTheAdminsName_AndListsTheServices()
    {
        var scopes = _sp.GetRequiredService<IServiceScopeFactory>();
        IHostedService[] hosted =
        [
            new TestWorker(scopes, NullLogger.Instance, pausable: true, (_, _) => Task.CompletedTask),
            new NotificationRetentionScheduler(scopes, NullLogger<NotificationRetentionScheduler>.Instance),
            new HeartbeatService(scopes, NullLogger<HeartbeatService>.Instance, new ConfigurationBuilder().Build()),
        ];
        var controller = new BackgroundJobsController(Pause, hosted)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "5"), new Claim(ClaimTypes.Name, "kahalm"), new Claim(ClaimTypes.Role, "Admin")],
                        "test")),
                },
            },
        };

        var put = Assert.IsType<OkObjectResult>((await controller.Set(new SetBackgroundJobsPauseDto(true), CancellationToken.None)).Result);
        var dto = Assert.IsType<BackgroundJobsPauseDto>(put.Value);
        Assert.True(dto.Paused);
        Assert.Equal("kahalm", dto.By);
        Assert.False(dto.ForcedByConfig);

        var get = Assert.IsType<OkObjectResult>((await controller.Get(CancellationToken.None)).Result);
        var listed = Assert.IsType<BackgroundJobsPauseDto>(get.Value).Services;
        Assert.Equal(["TestWorker", "HeartbeatService", "NotificationRetentionScheduler"], listed.Select(s => s.Name));
        Assert.Equal([true, false, false], listed.Select(s => s.Pausable));
    }
}
