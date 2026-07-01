namespace RookHub.Api.Services;

/// <summary>
/// Stößt einmal je 24 h (um 04:00 UTC) den Refresh aller Chessable-Kurslisten an
/// (<see cref="ChessableCourseRefreshService.RefreshAllAsync"/>). Kein Lauf unmittelbar beim Start
/// (kein Refresh-Sturm bei jedem Deploy/Neustart) — es wird erst bis zum nächsten 04:00 UTC gewartet.
/// Fehler eines Laufs werden nur geloggt; der Loop läuft weiter.
/// </summary>
public class ChessableCourseRefreshScheduler : BackgroundService
{
    /// <summary>Uhrzeit (UTC) des täglichen Laufs.</summary>
    public static readonly TimeSpan RunAtUtc = TimeSpan.FromHours(4);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChessableCourseRefreshScheduler> _logger;

    public ChessableCourseRefreshScheduler(IServiceScopeFactory scopeFactory, ILogger<ChessableCourseRefreshScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun(DateTime.UtcNow);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            await RunOnceAsync(stoppingToken);
        }
    }

    /// <summary>Wartezeit bis zum nächsten <see cref="RunAtUtc"/> (heute, falls noch nicht vorbei; sonst morgen).</summary>
    public static TimeSpan TimeUntilNextRun(DateTime nowUtc)
    {
        var todayRun = nowUtc.Date + RunAtUtc;
        var next = nowUtc < todayRun ? todayRun : todayRun.AddDays(1);
        var delay = next - nowUtc;
        return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<ChessableCourseRefreshService>();
            await svc.RefreshAllAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ChessableCourseRefreshScheduler: nächtlicher Kurslisten-Refresh fehlgeschlagen");
        }
    }
}
