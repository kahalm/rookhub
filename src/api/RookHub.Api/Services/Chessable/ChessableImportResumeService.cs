using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Chessable;

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

            // Browser-Importe NICHT: ihre Sitzung ist mit dem Neustart weg, zurückgestellt holte die Download-Lane den
            // Kurs serverseitig (Bearer bzw. Kurs-Cache). Sie schließt der Watchdog als verwaist.
            var running = await db.ChessableImports
                .Where(i => i.Status == ChessableImportStatus.Running && !i.FromBrowser)
                .ToListAsync(cancellationToken);

            if (running.Count == 0) return;

            // Unterbrochene Importe (auch mitten in "fetching"/"importing") als wartend markieren,
            // damit sie der faire Picker (RunNextAsync, greift nur Phase "queued") wieder aufnimmt.
            // RunAsync ist resume-fähig (PGN-Checkpoint, idempotent). Je Import ein Ticket.
            foreach (var imp in running)
                imp.Phase = ChessableImportPhase.Queued;
            await db.SaveChangesAsync(cancellationToken);

            // Admin-Schalter „Hintergrundjobs pausieren": nur zurückstellen, nicht anstoßen — die Warteschlange
            // (BackgroundTaskQueue) pausiert nicht, der Watchdog (pausierbar) holt die wartenden nach dem Fortsetzen.
            if (scope.ServiceProvider.GetService<BackgroundPause>() is { } pause && await pause.IsPausedAsync(cancellationToken))
            {
                _logger.LogInformation("Resume: {Count} unterbrochene Chessable-Importe zurückgestellt — Hintergrundjobs pausiert", running.Count);
                return;
            }

            _logger.LogInformation("Resume: {Count} unterbrochene Chessable-Importe werden fortgesetzt", running.Count);
            foreach (var _ in running)
            {
                await _queue.EnqueueAsync(async (sp, ct) =>
                {
                    var svc = sp.GetRequiredService<ChessableImportService>();
                    await svc.RunNextAsync(ct);
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
