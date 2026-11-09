namespace RookHub.Api.Services.Engines;

/// <summary>
/// Hält die Zugvergleiche in Bewegung (<see cref="MoveComparisonService.PumpAllAsync"/>): fertige Aufträge einsammeln,
/// die Antworten einreihen, die Begründungen anstoßen. Kürzerer Takt als die Partie-Analyse (5 s) — hier wartet jemand
/// vor der Seite, und ein Vergleich besteht aus einer Handvoll Stellungen, nicht aus einer Partie.
/// <para>Konfiguration: <c>MoveComparison:PumpIntervalSeconds</c> (2..120).</para>
/// <para>Auf <see cref="PeriodicWorker"/> (R1-013): fester Takt, Start sofort, Pause/Fehler/Shutdown wie alle anderen.
/// Die Fehlerzeile bleibt wörtlich die bisherige (Warnung, ein Durchlauf fällt aus, der nächste kommt).</para>
/// </summary>
public class MoveComparisonPumpService(IServiceScopeFactory scopes, IConfiguration config, ILogger<MoveComparisonPumpService> logger)
    : PeriodicWorker(scopes, logger)
{
    private readonly TimeSpan _interval =
        TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("MoveComparison:PumpIntervalSeconds") ?? 5, 2, 120));

    protected override WorkerSchedule Schedule => WorkerSchedule.Every(_interval);
    protected override WorkerStart Start => WorkerStart.Immediately;
    protected override void LogFailure(Exception ex) => logger.LogWarning(ex, "Zugvergleich: Durchlauf uebersprungen");

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        await services.GetRequiredService<MoveComparisonService>().PumpAllAsync(ct);
        // Zugvergleiche der Vereinspartien (0.908.0): Nachtrag + wartende starten (gedeckelt, Stapel-Vorrang).
        await services.GetRequiredService<ClubMistakeComparisons>().TickAsync(ct);
    }
}
