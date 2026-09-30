using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;
using Serilog.Events;

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
        protected override void LogFailure(Exception ex) => Logger.LogError(ex, "Testdienst: Durchlauf fehlgeschlagen");
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

    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    private static async Task RunUntilDoneAsync(PeriodicWorker worker)
    {
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);
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
        var log = new CapturingLogger<PeriodicWorkerTests>();
        var worker = new TestWorker(Scopes(), log, (_, _, _) =>
        {
            calls++;
            return Task.CompletedTask;
        }, enabled: false);

        await RunUntilDoneAsync(worker);

        Assert.Equal(0, calls);
        // Vorgabe-Zeile: festes Template, der Dienst steht als Property daneben (nicht im Template).
        var line = Assert.Single(log.Events);
        Assert.Equal("{Worker}: per Konfiguration abgeschaltet", line.State["{OriginalFormat}"]);
        Assert.Equal("TestWorker", line.State["Worker"]);
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
        var config = Config(("PlayerHistory:StartupDelayMinutes", startupMinutes));
        var worker = new PlayerHistoryScheduler(Scopes(), NullLogger<PlayerHistoryScheduler>.Instance, config);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), worker.InitialDelay(At0100));
    }

    [Fact]
    public async Task PlayerHistoryScheduler_Disabled_EndsAtOnce_WithItsOldLine()
    {
        var log = new CapturingLogger<PlayerHistoryScheduler>();
        var worker = new PlayerHistoryScheduler(Scopes(), log, Config(("PlayerHistory:Enabled", "false")));

        await RunUntilDoneAsync(worker);

        Assert.Equal("Turnierverlauf: Hintergrund-Durchgang per Konfiguration abgeschaltet",
            Assert.Single(log.Events).State["{OriginalFormat}"]);
    }

    // ---- Log-Vertrag: je Dienst ein FESTES Template (A8-008-Nacharbeit) ----
    // Serilog/ECS schreibt das Template nach labels.MessageTemplate; Kibana und der log-watcher gruppieren und
    // fingerprinten danach. Das gemeinsame "{FailureMessage}" ließ alle Dienste auf EINE Signatur fallen und setzte den
    // Text in Anführungszeichen — der MEL-gerenderte Text (Entry.Message) sah dabei unverändert aus, deshalb wird hier
    // das Template geprüft. Der Scope kennt den Fachdienst nicht: GetRequiredService wirft im Schritt → Fehlerzeile.

    [Fact]
    public async Task MovedServices_FailureLine_KeepsItsOldLiteralTemplate()
    {
        var chessable = new CapturingLogger<ChessableCourseRefreshScheduler>();
        Assert.False(await new ChessableCourseRefreshScheduler(Scopes(), chessable).RunStepAsync(CancellationToken.None));
        var chessableLine = Assert.Single(chessable.Events);
        Assert.Equal(LogLevel.Error, chessableLine.Level);
        Assert.Equal("ChessableCourseRefreshScheduler: nächtlicher Kurslisten-Refresh fehlgeschlagen",
            chessableLine.State["{OriginalFormat}"]);

        var history = new CapturingLogger<PlayerHistoryScheduler>();
        Assert.False(await new PlayerHistoryScheduler(Scopes(), history, Config()).RunStepAsync(CancellationToken.None));
        var historyLine = Assert.Single(history.Events);
        Assert.Equal(LogLevel.Error, historyLine.Level);
        Assert.Equal("Turnierverlauf: Hintergrund-Durchgang fehlgeschlagen", historyLine.State["{OriginalFormat}"]);
    }

    [Theory]
    [InlineData(nameof(ChessableCourseRefreshScheduler), "ChessableCourseRefreshScheduler: nächtlicher Kurslisten-Refresh fehlgeschlagen")]
    [InlineData(nameof(PlayerHistoryScheduler), "Turnierverlauf: Hintergrund-Durchgang fehlgeschlagen")]
    [InlineData(nameof(NotificationRetentionScheduler), "Benachrichtigungs-Retention fehlgeschlagen")]
    public async Task FailureLine_InTheSerilogPipeline_HasTheServicesOwnTemplate_AndAnUnquotedMessage(
        string service, string template)
    {
        var sink = new CollectingSink();
        var serilog = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        using var logs = new Serilog.Extensions.Logging.SerilogLoggerFactory(serilog, dispose: true);
        PeriodicWorker worker = service switch
        {
            nameof(ChessableCourseRefreshScheduler) =>
                new ChessableCourseRefreshScheduler(Scopes(), logs.CreateLogger<ChessableCourseRefreshScheduler>()),
            nameof(PlayerHistoryScheduler) =>
                new PlayerHistoryScheduler(Scopes(), logs.CreateLogger<PlayerHistoryScheduler>(), Config()),
            nameof(NotificationRetentionScheduler) =>
                new NotificationRetentionScheduler(Scopes(), logs.CreateLogger<NotificationRetentionScheduler>()),
            _ => throw new ArgumentOutOfRangeException(nameof(service)),
        };

        Assert.False(await worker.RunStepAsync(CancellationToken.None));

        var e = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Error, e.Level);
        Assert.Equal(template, e.MessageTemplate.Text);   // → labels.MessageTemplate (log-watcher message_field)
        Assert.Equal(template, e.RenderMessage());         // → message, ohne Anführungszeichen
        Assert.IsType<InvalidOperationException>(e.Exception);
    }

    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        private readonly List<LogEvent> _events = new();
        public IReadOnlyList<LogEvent> Events { get { lock (_events) return _events.ToList(); } }
        public void Emit(LogEvent logEvent) { lock (_events) _events.Add(logEvent); }
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
