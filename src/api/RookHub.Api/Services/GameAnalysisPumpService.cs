using RookHub.Api.Services;

namespace RookHub.Api.Services;

/// <summary>
/// Hält die Partie-Analysen in Bewegung: sammelt fertige Aufträge ein und füttert neue nach.
/// Bewusst ein eigener, langsamer Takt (Vorgabe 20 s) statt Arbeit im Request — eine Partie läuft
/// über Stunden, da zählt Verlässlichkeit, nicht Reaktionszeit.
/// <para>Konfiguration: <c>GameAnalysis:PumpIntervalSeconds</c> (10..600).</para>
/// </summary>
public class GameAnalysisPumpService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<GameAnalysisPumpService> _logger;
    private readonly TimeSpan _interval;

    public GameAnalysisPumpService(IServiceScopeFactory scopes, IConfiguration config,
        ILogger<GameAnalysisPumpService> logger)
    {
        _scopes = scopes;
        _logger = logger;
        var seconds = Math.Clamp(config.GetValue<int?>("GameAnalysis:PumpIntervalSeconds") ?? 20, 10, 600);
        _interval = TimeSpan.FromSeconds(seconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<GameAnalysisService>();
                await service.PumpAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;   // Shutdown ist kein Fehler (sonst Fehlalarm im log-watcher)
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Partie-Analyse: Durchlauf uebersprungen");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
