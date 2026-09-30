namespace RookHub.Api.Services;

/// <summary>
/// Gemeinsamer Rumpf für Hintergrunddienste, die in einem festen Takt einen Schritt ausführen: Schleife, Takt,
/// Startverhalten und EINE Fehlerpolitik stehen hier statt in jedem Dienst neu.
///
/// <para><b>Fehlerpolitik.</b> Nur der ECHTE Shutdown (gesetzter <c>stoppingToken</c>) beendet die Schleife, und
/// zwar still. Alles andere — ausdrücklich auch eine <see cref="OperationCanceledException"/> OHNE Shutdown, etwa die
/// <see cref="TaskCanceledException"/> eines HttpClient-Timeouts — wird mit der Fehlerzeile des Dienstes geloggt
/// (<see cref="LogFailure"/>), und der nächste Takt kommt. Die Fallenform
/// <c>catch (Exception ex) when (ex is not OperationCanceledException)</c> ließ genau den Timeout aus
/// <c>ExecuteAsync</c> fliegen, und <see cref="BackgroundServiceExceptionBehavior.StopHost"/> nahm dann die ganze API
/// mit (siehe RoundMonitorService).</para>
///
/// <para>Jeder Schritt bekommt einen FRISCHEN Scope (eigener DbContext, kein Tracker-Erbe vom Vorlauf).</para>
/// </summary>
public abstract class PeriodicWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    protected PeriodicWorker(IServiceScopeFactory scopeFactory, ILogger logger)
    {
        _scopeFactory = scopeFactory;
        Logger = logger;
    }

    protected ILogger Logger { get; }

    /// <summary>Wann nach einem Schritt der nächste kommt.</summary>
    protected abstract WorkerSchedule Schedule { get; }

    /// <summary>Ob und wann der erste Schritt nach dem Start kommt.</summary>
    protected abstract WorkerStart Start { get; }

    /// <summary>
    /// Fehlerzeile eines gescheiterten Schritts. Jeder Dienst schreibt sie mit seinem EIGENEN, festen Template, z. B.
    /// <c>Logger.LogError(ex, "Turnierverlauf: Hintergrund-Durchgang fehlgeschlagen")</c>; ein umgezogener Dienst
    /// behält wörtlich sein bisheriges.
    ///
    /// <para><b>Warum kein gemeinsames Template.</b> Serilog/ECS schreibt das Template nach
    /// <c>labels.MessageTemplate</c>, und genau danach gruppieren Kibana und der log-watcher
    /// (<c>message_field</c>, Fingerprint neuer Fehler-Signaturen). Ein gemeinsames <c>"{FailureMessage}"</c> mit dem
    /// Text als Parameter ließe ALLE Dienste auf eine Signatur fallen (und setzte den Text im gerenderten
    /// <c>message</c> in Anführungszeichen): nach dem ersten gesehenen Fehler meldete der log-watcher den Ausfall eines
    /// anderen Dienstes nicht mehr als neu. Dasselbe gälte für eine Vorgabe <c>"{Worker}: …"</c> bei jedem Dienst,
    /// der sie nicht überschreibt — deshalb ist die Methode abstrakt.</para>
    /// </summary>
    protected abstract void LogFailure(Exception ex);

    /// <summary>Per Konfiguration abgeschaltet? Dann endet der Dienst sofort (mit <see cref="LogDisabled"/>).</summary>
    protected virtual bool Enabled => true;

    /// <summary>Zeile beim Abschalten per Konfiguration (einmal je Start, Information). Die Vorgabe trägt den
    /// Dienstnamen als Property; ein Dienst mit eingeführtem Text überschreibt sie mit seinem wörtlichen Template
    /// (Begründung wie bei <see cref="LogFailure"/>).</summary>
    protected virtual void LogDisabled() => Logger.LogInformation("{Worker}: per Konfiguration abgeschaltet", GetType().Name);

    /// <summary>Ein Schritt. <paramref name="services"/> ist ein frischer Scope, der nach dem Schritt verworfen wird.</summary>
    protected abstract Task StepAsync(IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
        {
            LogDisabled();
            return;
        }

        var delay = InitialDelay(DateTime.UtcNow);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (delay > TimeSpan.Zero)
            {
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            }
            await RunStepAsync(stoppingToken);
            delay = Schedule.NextDelay(DateTime.UtcNow);
        }
    }

    /// <summary>Wartezeit bis zum ersten Schritt nach einem Start um <paramref name="nowUtc"/>.</summary>
    internal TimeSpan InitialDelay(DateTime nowUtc) => Start.FirstRunAfter ?? Schedule.NextDelay(nowUtc);

    /// <summary>Ein Schritt unter der einen Fehlerpolitik; wirft nie. <c>true</c>, wenn er durchlief.</summary>
    internal async Task<bool> RunStepAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await StepAsync(scope.ServiceProvider, stoppingToken);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;   // Herunterfahren — kein Fehler
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return false;
        }
    }
}

/// <summary>Takt eines <see cref="PeriodicWorker"/>.</summary>
public abstract record WorkerSchedule
{
    /// <summary>Wartezeit bis zum nächsten Schritt, gemessen ab <paramref name="nowUtc"/>.</summary>
    public abstract TimeSpan NextDelay(DateTime nowUtc);

    /// <summary>Fester Abstand zwischen zwei Schritten (gemessen ab dem Ende des vorigen).</summary>
    public static WorkerSchedule Every(TimeSpan interval) => new IntervalSchedule(interval);

    /// <summary>Einmal täglich zur Uhrzeit <paramref name="timeOfDay"/> (UTC).</summary>
    public static WorkerSchedule DailyAtUtc(TimeSpan timeOfDay) => new DailySchedule(timeOfDay);
}

public sealed record IntervalSchedule(TimeSpan Interval) : WorkerSchedule
{
    public override TimeSpan NextDelay(DateTime nowUtc) => Interval;
}

public sealed record DailySchedule(TimeSpan RunAtUtc) : WorkerSchedule
{
    public override TimeSpan NextDelay(DateTime nowUtc) => TimeUntilNextRun(nowUtc, RunAtUtc);

    /// <summary>Wartezeit bis zum nächsten <paramref name="runAtUtc"/> (heute, falls noch nicht vorbei; sonst morgen),
    /// mindestens eine Sekunde. Die EINE Fassung — vorher stand sie zeichengleich in drei Schedulern.</summary>
    public static TimeSpan TimeUntilNextRun(DateTime nowUtc, TimeSpan runAtUtc)
    {
        var todayRun = nowUtc.Date + runAtUtc;
        var next = nowUtc < todayRun ? todayRun : todayRun.AddDays(1);
        var delay = next - nowUtc;
        return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
    }
}

/// <summary>Startverhalten eines <see cref="PeriodicWorker"/>: <see cref="FirstRunAfter"/> <c>null</c> heißt „kein
/// Startlauf, erst zum ersten Takt".</summary>
public readonly record struct WorkerStart(TimeSpan? FirstRunAfter)
{
    /// <summary>Erster Schritt sofort beim Start.</summary>
    public static WorkerStart Immediately => new(TimeSpan.Zero);

    /// <summary>Erster Schritt nach <paramref name="delay"/> (z. B. um Migration/Crawler hochfahren zu lassen).</summary>
    public static WorkerStart After(TimeSpan delay) => new(delay);

    /// <summary>Kein Startlauf (kein Sturm bei jedem Deploy) — erst zum ersten Takt des Zeitplans.</summary>
    public static WorkerStart OnSchedule => new((TimeSpan?)null);
}
