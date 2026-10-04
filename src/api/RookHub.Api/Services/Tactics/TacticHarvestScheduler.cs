namespace RookHub.Api.Services.Tactics;

/// <summary>Taktik-Ernte im Hintergrund (0.657.0): jede Minute ernten, prüfen, veröffentlichen
/// (<see cref="TacticHarvestService.RunAsync"/>). Abschaltbar mit <c>TacticHarvest:Enabled=false</c>.</summary>
public sealed class TacticHarvestScheduler(IServiceScopeFactory scopes, IConfiguration config, ILogger<TacticHarvestScheduler> logger)
    : PeriodicWorker(scopes, logger)
{
    protected override WorkerSchedule Schedule { get; } = WorkerSchedule.Every(TimeSpan.FromSeconds(
        Math.Clamp(config.GetValue<int?>("TacticHarvest:TickSeconds") ?? 60, 10, 3600)));
    protected override WorkerStart Start => WorkerStart.After(TimeSpan.FromMinutes(5));
    protected override bool Enabled => config.GetValue<bool?>("TacticHarvest:Enabled") ?? true;
    protected override void LogFailure(Exception ex) => logger.LogError(ex, "Taktik-Ernte: Takt fehlgeschlagen");

    protected override Task StepAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<TacticHarvestService>().RunAsync(ct);
}
