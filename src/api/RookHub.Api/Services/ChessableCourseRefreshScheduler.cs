namespace RookHub.Api.Services;

/// <summary>
/// Stößt einmal je 24 h (um 04:00 UTC) den Refresh aller Chessable-Kurslisten an
/// (<see cref="ChessableCourseRefreshService.RefreshAllAsync"/>). Kein Lauf unmittelbar beim Start
/// (kein Refresh-Sturm bei jedem Deploy/Neustart) — es wird erst bis zum nächsten 04:00 UTC gewartet.
/// Fehler eines Laufs werden nur geloggt; der Loop läuft weiter (<see cref="PeriodicWorker"/>).
/// </summary>
public class ChessableCourseRefreshScheduler : PeriodicWorker
{
    /// <summary>Uhrzeit (UTC) des täglichen Laufs.</summary>
    public static readonly TimeSpan RunAtUtc = TimeSpan.FromHours(4);

    public ChessableCourseRefreshScheduler(IServiceScopeFactory scopeFactory, ILogger<ChessableCourseRefreshScheduler> logger)
        : base(scopeFactory, logger)
    {
    }

    protected override WorkerSchedule Schedule { get; } = WorkerSchedule.DailyAtUtc(RunAtUtc);
    protected override WorkerStart Start => WorkerStart.OnSchedule;
    protected override string FailureMessage => "ChessableCourseRefreshScheduler: nächtlicher Kurslisten-Refresh fehlgeschlagen";

    /// <summary>Wartezeit bis zum nächsten <see cref="RunAtUtc"/> (heute, falls noch nicht vorbei; sonst morgen).</summary>
    public static TimeSpan TimeUntilNextRun(DateTime nowUtc) => DailySchedule.TimeUntilNextRun(nowUtc, RunAtUtc);

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        await services.GetRequiredService<ChessableCourseRefreshService>().RefreshAllAsync(ct);
        // Die Retention der anonymen getReview-Senke liegt NICHT mehr hier, sondern im immer laufenden
        // AnonymousDataRetentionService: dieser Dienst ist nur mit Chessable:Enabled=true registriert
        // (PROD: aus), und ein Fehler im Refresh übersprang sie im selben try.
    }
}
