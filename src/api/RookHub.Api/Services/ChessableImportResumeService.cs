using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Beim API-Start: reiht alle Chessable-Importe, die noch auf "running" stehen (also durch einen
/// Crash/Deploy unterbrochen wurden), erneut in die <see cref="IBackgroundTaskQueue"/> ein.
/// <see cref="ChessableImportService.RunAsync"/> ist resume-fähig (PGN-Checkpoint, idempotent)
/// und begrenzt über <see cref="ChessableImportService.MaxAttempts"/> Endlos-Resumes.
/// </summary>
public class ChessableImportResumeService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundTaskQueue _queue;
    private readonly ILogger<ChessableImportResumeService> _logger;

    public ChessableImportResumeService(
        IServiceScopeFactory scopeFactory,
        IBackgroundTaskQueue queue,
        ILogger<ChessableImportResumeService> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var running = await db.ChessableImports
                .Where(i => i.Status == "running")
                .Select(i => i.Id)
                .ToListAsync(cancellationToken);

            if (running.Count == 0) return;

            _logger.LogInformation("Resume: {Count} unterbrochene Chessable-Importe werden fortgesetzt", running.Count);
            foreach (var id in running)
            {
                await _queue.EnqueueAsync(async (sp, ct) =>
                {
                    var svc = sp.GetRequiredService<ChessableImportService>();
                    await svc.RunAsync(id, ct);
                });
            }
        }
        catch (Exception ex)
        {
            // Nicht kritisch fürs Hochfahren (z. B. wenn die Tabelle bei einem frischen Start noch fehlt).
            _logger.LogError(ex, "Resume der Chessable-Importe beim Start fehlgeschlagen");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
