using RookHub.Api.Data;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.Social;

/// <summary>
/// Täglicher Lauf der Benachrichtigungs-Retention (<see cref="NotificationService.RunRetentionAsync"/>, Codereview
/// 2026-09-29, A9-003): gelesene Benachrichtigungen älter als <see cref="NotificationService.SeenRetention"/> gehen,
/// und Namen gelöschter Konten in fremden Benachrichtigungen werden ersetzt (Altbestand vor dem Fix, Rennen mit
/// einer gleichzeitigen Löschung). Um 05:00 UTC (nach den übrigen Nachtläufen) und einmal kurz nach dem Start, damit
/// der Altbestand nach einem Deploy nicht bis zur nächsten Nacht stehen bleibt. Immer registriert.
/// </summary>
public class NotificationRetentionScheduler : PeriodicWorker
{
    public static readonly TimeSpan RunAtUtc = TimeSpan.FromHours(5);

    private readonly ILogger<NotificationRetentionScheduler> _logger;

    public NotificationRetentionScheduler(IServiceScopeFactory scopeFactory, ILogger<NotificationRetentionScheduler> logger)
        : base(scopeFactory, logger)
    {
        _logger = logger;
    }

    protected override WorkerSchedule Schedule { get; } = WorkerSchedule.DailyAtUtc(RunAtUtc);
    protected override WorkerStart Start => WorkerStart.After(TimeSpan.FromMinutes(10));
    /// <summary>Nicht pausierbar: Aufbewahrungsfrist + Namen gelöschter Konten ersetzen (Datenschutz/Konsistenz),
    /// einmal täglich und billig.</summary>
    protected override bool Pausable => false;
    protected override void LogFailure(Exception ex) => _logger.LogError(ex, "Benachrichtigungs-Retention fehlgeschlagen");

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        var (purged, anonymized) = await services.GetRequiredService<NotificationService>()
            .RunRetentionAsync(DateTime.UtcNow, ct);
        // Glocke „Analyse fertig" (0.904.0): Zeilen, deren Analyse nie fertig wurde, verfallen nach 60 Tagen.
        var stale = await new GameAnalysisWatchers(services.GetRequiredService<AppDbContext>()).PurgeStaleAsync(DateTime.UtcNow, ct);
        if (stale > 0) _logger.LogInformation("Benachrichtigungs-Retention: {Stale} liegengebliebene Analyse-Wartende entfernt", stale);
        if (purged > 0 || anonymized > 0)
            _logger.LogInformation(
                "Benachrichtigungs-Retention: {Purged} gelesene gelöscht, {Anonymized} Namen gelöschter Konten ersetzt",
                purged, anonymized);
    }
}
