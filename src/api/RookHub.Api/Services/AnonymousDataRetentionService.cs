using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Räumt ANONYME Spielstände auf, die niemand mehr abholen kann. Der anonyme Endless-Pfad ist
/// bewusst offen (ohne Konto spielen), und die Session-Id ist ein frei wählbares Feld des Requests:
/// jede neue Kennung legt eine eigene Zeile an — <c>ActiveGameState</c> ist LONGTEXT. Diese Retention
/// ist der RÜCKBAU; die Schranke gegen das Vollschreiben sind die Gesamtdeckel
/// (<see cref="EndlessProgressService.MaxAnonymousProgressRowsTotal"/>,
/// <see cref="EndlessProgressService.MaxAnonymousSessionRowsTotal"/>) und der engere Deckel des anonymen
/// Spielstands (<see cref="DTOs.SaveAnonymousProgressDto.MaxActiveGameStateLength"/>) — ein Verfall
/// nach 60 Tagen allein hätte ein Skript mit frischen Session-Ids nicht gebremst. Die Zeilen sind nach
/// dem Spielen wertlos: ein Rückkehrer bringt seine Session-Id im Browser-Speicher mit, und wer sich
/// anmeldet, übernimmt sie sofort (<c>POST /api/endless/claim-session</c>).
///
/// <para>Dazu die anonyme getReview-Senke (<c>AnonymousChessableReviewLines</c>, offener Endpunkt
/// <c>/api/extension/chessable/review-lines/anon</c>): ungeclaimte Zeilen nach
/// <see cref="AnonymousReviewLineMaxAge"/>, die von uids ohne verknüpftes Konto schon nach
/// <see cref="UnlinkedAnonymousReviewLineMaxAge"/>. Die lief bis 2026-09-30 im nächtlichen
/// Kurslisten-Refresh — und der ist nur mit <c>Chessable:Enabled=true</c> registriert: auf PROD (Schalter
/// seit 2026-09-09 aus) wurde keine Zeile mehr gelöscht, obwohl der Extension-Endpunkt weiter annimmt und
/// PRIVACY.md 90 Tage zusagt. Dieser Dienst läuft IMMER.</para>
///
/// Läuft täglich; jede Senke hat ihren eigenen Versuch — ein Fehler der einen lässt die andere nicht aus
/// und beendet den Dienst nicht (nur Logzeile).
/// </summary>
public class AnonymousDataRetentionService : BackgroundService
{
    /// <summary>Anonyme Endless-Zeilen ohne Berührung verfallen danach. Großzügig gewählt: ein
    /// Gelegenheitsspieler soll seinen Lauf auch nach zwei Monaten Pause noch vorfinden.</summary>
    public static readonly TimeSpan AnonymousEndlessMaxAge = TimeSpan.FromDays(60);

    /// <summary>Ungeclaimte anonyme getReview-Linien verfallen danach (Zusage in RepCheck-PRIVACY.md).</summary>
    public static readonly TimeSpan AnonymousReviewLineMaxAge = TimeSpan.FromDays(90);

    /// <summary>Kürzere Frist für uids OHNE verknüpftes Konto: nicht claimbar, also der Vorrats-Topf, den der offene
    /// Endpunkt füllen kann (der legitime Weg dauert Tage, nicht Monate).</summary>
    public static readonly TimeSpan UnlinkedAnonymousReviewLineMaxAge = TimeSpan.FromDays(14);

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

    /// <summary>Ein Durchlauf; liefert die Zahl gelöschter Zeilen (für Tests/Logs). Beide Senken in getrennten
    /// Versuchen: scheitert die eine, läuft die andere trotzdem.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var removed = 0;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var reviewLines = scope.ServiceProvider.GetRequiredService<ChessableReviewLineService>();
            var pruned = await reviewLines.PruneAnonOlderThanAsync(AnonymousReviewLineMaxAge, ct);
            pruned += await reviewLines.PruneUnlinkedAnonOlderThanAsync(UnlinkedAnonymousReviewLineMaxAge, ct);
            if (pruned > 0)
                _logger.LogInformation("Anon-getReview-Retention: {Count} ungeclaimte Zeilen gelöscht", pruned);
            removed += pruned;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Anon-getReview-Retention fehlgeschlagen");
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            removed += await PruneAsync(db, DateTime.UtcNow - AnonymousEndlessMaxAge, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Anonyme Retention fehlgeschlagen");
        }
        return removed;
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
