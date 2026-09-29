namespace RookHub.Api.Services;

/// <summary>
/// Hält die Zugvergleiche in Bewegung (<see cref="MoveComparisonService.PumpAllAsync"/>): fertige Aufträge einsammeln,
/// die Antworten einreihen, die Begründungen anstoßen. Kürzerer Takt als die Partie-Analyse (5 s) — hier wartet jemand
/// vor der Seite, und ein Vergleich besteht aus einer Handvoll Stellungen, nicht aus einer Partie.
/// <para>Konfiguration: <c>MoveComparison:PumpIntervalSeconds</c> (2..120).</para>
/// </summary>
public class MoveComparisonPumpService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MoveComparisonPumpService> _logger;
    private readonly TimeSpan _interval;

    public MoveComparisonPumpService(IServiceScopeFactory scopes, IConfiguration config, ILogger<MoveComparisonPumpService> logger)
    {
        _scopes = scopes;
        _logger = logger;
        var seconds = Math.Clamp(config.GetValue<int?>("MoveComparison:PumpIntervalSeconds") ?? 5, 2, 120);
        _interval = TimeSpan.FromSeconds(seconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<MoveComparisonService>().PumpAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;   // Shutdown ist kein Fehler (sonst Fehlalarm im log-watcher)
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Zugvergleich: Durchlauf uebersprungen");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
