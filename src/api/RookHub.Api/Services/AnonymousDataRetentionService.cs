using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Räumt ANONYME Spielstände auf, die niemand mehr abholen kann. Der anonyme Endless-Pfad ist
/// bewusst offen (ohne Konto spielen), und die Session-Id ist ein frei wählbares Feld des Requests:
/// jede neue Kennung legt eine eigene Zeile an — <c>ActiveGameState</c> ist LONGTEXT. Es gab dafür
/// weder einen Deckel noch ein Verfallsdatum, während die Anon-Senke der Chessable-Linien längst
/// eine Retention hat. Die Zeilen sind nach dem Spielen wertlos: ein Rückkehrer bringt seine
/// Session-Id im Browser-Speicher mit, und wer sich anmeldet, übernimmt sie sofort
/// (<c>POST /api/endless/claim-session</c>).
///
/// Läuft täglich; ein Fehler beendet den Dienst nicht (nur Logzeile).
/// </summary>
public class AnonymousDataRetentionService : BackgroundService
{
    /// <summary>Anonyme Endless-Zeilen ohne Berührung verfallen danach. Großzügig gewählt: ein
    /// Gelegenheitsspieler soll seinen Lauf auch nach zwei Monaten Pause noch vorfinden.</summary>
    public static readonly TimeSpan AnonymousEndlessMaxAge = TimeSpan.FromDays(60);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnonymousDataRetentionService> _logger;

    public AnonymousDataRetentionService(IServiceScopeFactory scopeFactory,
        ILogger<AnonymousDataRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { _logger.LogError(ex, "Anonyme Retention fehlgeschlagen"); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Ein Durchlauf; liefert die Zahl gelöschter Zeilen (für Tests/Logs).</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await PruneAsync(db, DateTime.UtcNow - AnonymousEndlessMaxAge, ct);
    }

    /// <summary>Anonyme Endless-Zeilen älter als <paramref name="cutoff"/> löschen. Zeilen MIT
    /// <c>UserId</c> bleiben immer — das ist die Statistik angemeldeter Nutzer.</summary>
    public static async Task<int> PruneAsync(AppDbContext db, DateTime cutoff, CancellationToken ct = default)
    {
        var progresses = await db.EndlessProgresses
            .Where(p => p.UserId == null && p.UpdatedAt < cutoff)
            .ToListAsync(ct);
        var sessions = await db.EndlessSessions
            .Where(s => s.UserId == null && s.CreatedAt < cutoff)
            .ToListAsync(ct);
        if (progresses.Count == 0 && sessions.Count == 0) return 0;

        db.EndlessProgresses.RemoveRange(progresses);
        db.EndlessSessions.RemoveRange(sessions);
        await db.SaveChangesAsync(ct);
        return progresses.Count + sessions.Count;
    }
}
