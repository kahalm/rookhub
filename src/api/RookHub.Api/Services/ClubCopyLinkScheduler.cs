namespace RookHub.Api.Services;

/// <summary>Kopien von Vereinspartien aus der Zeit vor 0.660.0 nachträglich mit ihrer Vereinspartie verbinden
/// (<see cref="SavedGameService.LinkClubCopiesAsync"/>) — kurz nach dem Start und dann täglich.</summary>
public sealed class ClubCopyLinkScheduler(IServiceScopeFactory scopes, ILogger<ClubCopyLinkScheduler> logger)
    : PeriodicWorker(scopes, logger)
{
    protected override WorkerSchedule Schedule { get; } = WorkerSchedule.Every(TimeSpan.FromHours(24));
    protected override WorkerStart Start => WorkerStart.After(TimeSpan.FromMinutes(3));
    protected override void LogFailure(Exception ex) => logger.LogError(ex, "Kopien von Vereinspartien verbinden fehlgeschlagen");

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        var linked = await services.GetRequiredService<SavedGameService>().LinkClubCopiesAsync(ct: ct);
        if (linked > 0) logger.LogInformation("{Count} Kopien von Vereinspartien verbunden", linked);
    }
}
