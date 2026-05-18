using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

public class RoundMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RoundMonitorService> _logger;

    public RoundMonitorService(IServiceScopeFactory scopeFactory, ILogger<RoundMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RoundMonitorService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAllMonitorsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error in RoundMonitorService loop");
            }

            await Task.Delay(30_000, stoppingToken);
        }
    }

    private async Task CheckAllMonitorsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proxy = scope.ServiceProvider.GetRequiredService<CrawlerProxyService>();

        // Clean up expired monitors
        var expired = await db.TournamentMonitors
            .Where(m => m.ActiveUntil < DateTime.UtcNow)
            .ToListAsync(ct);

        if (expired.Count > 0)
        {
            db.TournamentMonitors.RemoveRange(expired);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Cleaned up {Count} expired monitors", expired.Count);
        }

        // Check all active monitors
        var monitors = await db.TournamentMonitors
            .Where(m => m.ActiveUntil >= DateTime.UtcNow)
            .ToListAsync(ct);

        if (monitors.Count == 0) return;

        _logger.LogDebug("Checking {Count} active monitors", monitors.Count);

        foreach (var monitor in monitors)
        {
            try
            {
                var checkResult = await proxy.GetAsync(
                    $"/api/tournaments/{monitor.CrawlerTournamentDbId}/rounds/check");

                var hasNewRound = checkResult.TryGetProperty("hasNewRound", out var hnr) && hnr.GetBoolean();

                if (hasNewRound)
                {
                    var newRounds = checkResult.TryGetProperty("newRoundNumbers", out var nrn)
                        ? nrn.ToString()
                        : "?";

                    _logger.LogInformation(
                        "New round detected for tournament {TournamentId} (DB {DbId}). New rounds: {NewRounds}",
                        monitor.CrawlerTournamentId, monitor.CrawlerTournamentDbId, newRounds);

                    // Trigger PairingsOnly crawl
                    var crawlBody = JsonSerializer.Deserialize<JsonElement>(
                        JsonSerializer.Serialize(new
                        {
                            chessResultsId = monitor.CrawlerTournamentId,
                            jobType = "PairingsOnly"
                        }));

                    await proxy.PostAsync("/api/crawl", crawlBody);

                    // Update known rounds
                    if (checkResult.TryGetProperty("availableRounds", out var ar))
                        monitor.LastKnownRounds = ar.GetInt32();
                }

                monitor.LastCheckedAt = DateTime.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Error checking monitor for tournament {TournamentId}",
                    monitor.CrawlerTournamentId);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
