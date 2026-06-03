using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Schreibt periodisch (Standard 60 s, via <c>Heartbeat:IntervalSeconds</c>) ein strukturiertes
/// „Heartbeat"-Log nach Elasticsearch — damit der log-watcher einen toten/hängenden Dienst an
/// AUSBLEIBENDEN Heartbeats erkennt (statt nur an Stille, die auch „gesund, aber gerade ruhig"
/// sein kann). Enthält einen kurzen Selbst-Check (DB erreichbar) → Status healthy/degraded.
/// Marker „Heartbeat" + Feld <c>HeartbeatService</c> machen es in Kibana/Watcher filterbar.
/// </summary>
public class HeartbeatService : BackgroundService
{
    public const string ServiceName = "rookhub-api";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly TimeSpan _interval;
    private readonly DateTime _startedAt = DateTime.UtcNow;

    public HeartbeatService(IServiceScopeFactory scopeFactory, ILogger<HeartbeatService> logger, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var seconds = config.GetValue<int?>("Heartbeat:IntervalSeconds") ?? 60;
        _interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 10, 3600));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Erstes Lebenszeichen sofort beim Start (Boot-Signal), danach periodisch.
        await EmitAsync();
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await EmitAsync();
        }
        catch (OperationCanceledException) { /* Shutdown */ }
    }

    /// <summary>Einen Heartbeat (mit DB-Selbst-Check) loggen. public als Test-Seam.</summary>
    public async Task EmitAsync()
    {
        bool dbOk;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbOk = await db.Database.CanConnectAsync();
        }
        catch
        {
            dbOk = false;
        }

        var uptimeSeconds = (int)(DateTime.UtcNow - _startedAt).TotalSeconds;
        _logger.LogInformation(
            "Heartbeat: {HeartbeatService} {HeartbeatStatus} db={HeartbeatDbOk} uptime={HeartbeatUptimeSeconds}s",
            ServiceName, dbOk ? "healthy" : "degraded", dbOk, uptimeSeconds);
    }
}
