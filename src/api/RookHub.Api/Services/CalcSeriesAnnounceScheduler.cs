namespace RookHub.Api.Services;

/// <summary>
/// Weckt periodisch (Standard alle 5 min, via <c>CalcSeries:AnnounceIntervalSeconds</c>, 60..3600) und
/// lässt <see cref="CalcSeriesAnnounceService.RunOnceAsync"/> fällige Serien-Ausgaben ankündigen.
/// Erster Lauf sofort beim Start (fängt beim Deploy bereits fällige Freigaben ab), danach je Intervall.
/// Fehler eines Laufs werden nur geloggt; der Loop läuft weiter.
/// </summary>
public class CalcSeriesAnnounceScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CalcSeriesAnnounceScheduler> _logger;
    private readonly TimeSpan _interval;

    public CalcSeriesAnnounceScheduler(IServiceScopeFactory scopeFactory,
        ILogger<CalcSeriesAnnounceScheduler> logger, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var seconds = config.GetValue<int?>("CalcSeries:AnnounceIntervalSeconds") ?? 300;
        _interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 60, 3600));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<CalcSeriesAnnounceService>();
                var n = await svc.RunOnceAsync(stoppingToken);
                if (n > 0)
                    _logger.LogInformation("CalcSeriesAnnounceScheduler: {Count} Ausgaben-Ankündigung(en) verschickt", n);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CalcSeriesAnnounceScheduler: Ankündigungslauf fehlgeschlagen");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    /// <summary>Nächsten Tick abwarten; bei Abbruch <c>false</c> statt einer Exception.</summary>
    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
