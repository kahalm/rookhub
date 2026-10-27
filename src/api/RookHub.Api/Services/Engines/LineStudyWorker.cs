namespace RookHub.Api.Services.Engines;

/// <summary>Takt von „ohne Engine analysieren" (0.855.0): alle 20 s <see cref="LineStudyService.StepAsync"/> — fertige Aufträge
/// in ihre Stellung übernehmen, neue gedrosselt anlegen. Pausierbar.</summary>
public class LineStudyWorker(IServiceScopeFactory scopeFactory, ILogger<LineStudyWorker> logger) : PeriodicWorker(scopeFactory, logger)
{
    protected override WorkerSchedule Schedule => WorkerSchedule.Every(TimeSpan.FromSeconds(20));
    protected override WorkerStart Start => WorkerStart.After(TimeSpan.FromMinutes(1));
    protected override void LogFailure(Exception ex) => Logger.LogError(ex, "Linien ohne Engine: Durchlauf fehlgeschlagen");

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        var (collected, queued) = await services.GetRequiredService<LineStudyService>().StepAsync(ct);
        if (collected + queued > 0)
            Logger.LogInformation("Linien ohne Engine: {Collected} Stellungen übernommen, {Queued} Aufträge angelegt", collected, queued);
    }
}
