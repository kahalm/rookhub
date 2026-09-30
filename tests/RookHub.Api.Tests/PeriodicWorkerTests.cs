using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Der gemeinsame Rumpf der Hintergrunddienste (A8-008): EINE Fehlerpolitik — nur der echte Shutdown beendet
/// die Schleife, alles andere (auch eine TaskCanceledException ohne Shutdown, etwa ein HttpClient-Timeout) wird geloggt,
/// und der nächste Takt kommt.</summary>
public class PeriodicWorkerTests
{
    private sealed class Marker;

    private sealed class TestWorker(IServiceScopeFactory scopes, ILogger logger,
        Func<IServiceProvider, int, CancellationToken, Task> step, bool enabled = true) : PeriodicWorker(scopes, logger)
    {
        private int _calls;
        protected override WorkerSchedule Schedule => WorkerSchedule.Every(TimeSpan.Zero);
        protected override WorkerStart Start => WorkerStart.Immediately;
        protected override string FailureMessage => "Testdienst: Durchlauf fehlgeschlagen";
        protected override bool Enabled => enabled;
        protected override Task StepAsync(IServiceProvider services, CancellationToken ct)
            => step(services, Interlocked.Increment(ref _calls), ct);
    }

    private static IServiceScopeFactory Scopes()
    {
        var services = new ServiceCollection();
        services.AddScoped<Marker>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static List<CapturingLogger<PeriodicWorkerTests>.Entry> Errors(CapturingLogger<PeriodicWorkerTests> log)
    {
        lock (log.Events) return log.Events.Where(e => e.Level == LogLevel.Error).ToList();
    }

    [Fact]
    public async Task CanceledWithoutShutdown_IsLogged_AndTheLoopGoesOn()
    {
        // Die Fallenform `when (ex is not OperationCanceledException)` ließ genau diesen Fall aus ExecuteAsync fliegen,
        // und StopHost nahm die ganze API mit.
        var log = new CapturingLogger<PeriodicWorkerTests>();
        var secondStep = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new TestWorker(Scopes(), log, async (_, call, ct) =>
        {
            if (call == 1) throw new TaskCanceledException("HttpClient-Timeout");
            secondStep.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });

        await worker.StartAsync(CancellationToken.None);
        await secondStep.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);

        var error = Assert.Single(Errors(log));
        Assert.Equal("Testdienst: Durchlauf fehlgeschlagen", error.Message);
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Shutdown_DuringAStep_EndsTheLoopWithoutErrorLog()
    {
        var log = new CapturingLogger<PeriodicWorkerTests>();
        var inStep = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new TestWorker(Scopes(), log, async (_, _, ct) =>
        {
            inStep.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });

        await worker.StartAsync(CancellationToken.None);
        await inStep.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(Errors(log));
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task FailingStep_IsLoggedWithTheServiceMessage_AndNeverEscapes()
    {
        var log = new CapturingLogger<PeriodicWorkerTests>();
        var worker = new TestWorker(Scopes(), log, (_, _, _) => throw new InvalidOperationException("kaputt"));

        Assert.False(await worker.RunStepAsync(CancellationToken.None));
        Assert.Equal("Testdienst: Durchlauf fehlgeschlagen", Assert.Single(Errors(log)).Message);
    }

    [Fact]
    public async Task EachStep_GetsAFreshScope()
    {
        var seen = new List<Marker>();
        var worker = new TestWorker(Scopes(), NullLogger.Instance, (sp, _, _) =>
        {
            seen.Add(sp.GetRequiredService<Marker>());
            return Task.CompletedTask;
        });

        Assert.True(await worker.RunStepAsync(CancellationToken.None));
        Assert.True(await worker.RunStepAsync(CancellationToken.None));
        Assert.NotSame(seen[0], seen[1]);
    }

    [Fact]
    public async Task Disabled_EndsAtOnce_WithoutAStep()
    {
        var calls = 0;
        var worker = new TestWorker(Scopes(), NullLogger.Instance, (_, _, _) =>
        {
            calls++;
            return Task.CompletedTask;
        }, enabled: false);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(2, 0, 2)]    // vor der Uhrzeit: heute
    [InlineData(5, 0, 23)]   // danach: morgen
    [InlineData(4, 0, 24)]   // genau zur Uhrzeit: morgen, nicht sofort
    public void DailySchedule_WaitsForTheNextRunTime(int hour, int minute, int expectedHours)
        => Assert.Equal(TimeSpan.FromHours(expectedHours), DailySchedule.TimeUntilNextRun(
            new DateTime(2026, 9, 30, hour, minute, 0, DateTimeKind.Utc), TimeSpan.FromHours(4)));

    [Fact]
    public void DailySchedule_NeverWaitsLessThanASecond()
        => Assert.Equal(TimeSpan.FromSeconds(1), DailySchedule.TimeUntilNextRun(
            new DateTime(2026, 9, 30, 3, 59, 59, 500, DateTimeKind.Utc), TimeSpan.FromHours(4)));

    // ---- Umgezogene Dienste: Startzeitpunkte unverändert ----

    private static readonly DateTime At0100 = new(2026, 9, 30, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ChessableCourseRefreshScheduler_HasNoStartRun_WaitsForTheNext0400()
    {
        var worker = new ChessableCourseRefreshScheduler(Scopes(), NullLogger<ChessableCourseRefreshScheduler>.Instance);
        Assert.IsAssignableFrom<PeriodicWorker>(worker);
        Assert.Equal(TimeSpan.FromHours(3), worker.InitialDelay(At0100));
    }

    [Theory]
    [InlineData(null, 10 * 60)]     // Vorgabe: Startlauf nach zehn Minuten
    [InlineData("0", 3.5 * 3600)]   // 0 schaltet den Startlauf ab → erst 04:30 UTC
    public void PlayerHistoryScheduler_KeepsItsStartRun(string? startupMinutes, double expectedSeconds)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlayerHistory:StartupDelayMinutes"] = startupMinutes,
        }).Build();
        var worker = new PlayerHistoryScheduler(Scopes(), NullLogger<PlayerHistoryScheduler>.Instance, config);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), worker.InitialDelay(At0100));
    }

    [Fact]
    public async Task PlayerHistoryScheduler_Disabled_EndsAtOnce()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlayerHistory:Enabled"] = "false",
        }).Build();
        var worker = new PlayerHistoryScheduler(Scopes(), NullLogger<PlayerHistoryScheduler>.Instance, config);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void ProgramCs_SetsTheBackgroundServiceExceptionBehaviorExplicitly()
    {
        // Vorher stand er nirgends — der Kommentar im RoundMonitorService musste den .NET-Default erklären.
        var src = File.ReadAllText(ProgramCs());
        Assert.Contains("BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.", src);
    }

    private static string ProgramCs([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "src", "api", "RookHub.Api", "Program.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        Assert.Fail("Program.cs nicht gefunden");
        return "";
    }
}
