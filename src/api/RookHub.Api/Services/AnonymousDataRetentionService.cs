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

    /// <summary>Portionsgröße der Löschung (Zeilen je DELETE).</summary>
    internal const int DeleteChunkSize = 1000;

    /// <summary>Anonyme Endless-Zeilen älter als <paramref name="cutoff"/> löschen. Zeilen MIT
    /// <c>UserId</c> bleiben immer — das ist die Statistik angemeldeter Nutzer.</summary>
    public static Task<int> PruneAsync(AppDbContext db, DateTime cutoff, CancellationToken ct = default)
        => PruneAsync(db, cutoff, DeleteChunkSize, ct);

    /// <summary>Wie <see cref="PruneAsync(AppDbContext, DateTime, CancellationToken)"/>, mit wählbarer Portion (Tests).
    /// Portionsweise über die Ids, ohne die Zeilen zu laden: bis zu 1 MB <c>ActiveGameState</c> je Zeile — ein fälliger
    /// Rückstand von ein paar tausend Zeilen, auf einmal per ToList geladen, sprengte das Speicherlimit der API, und weil
    /// der erste Lauf beim Start kommt, scheiterte er danach jeden Tag von Neuem.</summary>
    internal static async Task<int> PruneAsync(AppDbContext db, DateTime cutoff, int chunkSize, CancellationToken ct = default)
    {
        var removed = await DeleteInChunksAsync(db,
            db.EndlessProgresses.Where(p => p.UserId == null && p.UpdatedAt < cutoff), chunkSize, ct);
        removed += await DeleteInChunksAsync(db,
            db.EndlessSessions.Where(s => s.UserId == null && s.CreatedAt < cutoff), chunkSize, ct);
        return removed;
    }

    /// <summary>Löscht, was <paramref name="query"/> trifft, in Portionen zu <paramref name="chunkSize"/>: erst nur die
    /// Ids, dann ein DELETE über dieselbe Bedingung plus die Ids (eine inzwischen wieder berührte Zeile bleibt).
    /// InMemory (Tests) kennt kein ExecuteDelete — dort über den Tracker.</summary>
    private static async Task<int> DeleteInChunksAsync<T>(AppDbContext db, IQueryable<T> query, int chunkSize,
        CancellationToken ct) where T : class
    {
        var deleted = 0;
        while (true)
        {
            var ids = await query.OrderBy(e => EF.Property<int>(e, "Id")).Select(e => EF.Property<int>(e, "Id"))
                .Take(chunkSize).ToListAsync(ct);
            if (ids.Count == 0) break;
            var chunk = query.Where(e => ids.Contains(EF.Property<int>(e, "Id")));
            if (db.Database.IsRelational())
                deleted += await chunk.ExecuteDeleteAsync(ct);
            else
            {
                var rows = await chunk.ToListAsync(ct);
                db.Set<T>().RemoveRange(rows);
                await db.SaveChangesAsync(ct);
                deleted += rows.Count;
            }
            if (ids.Count < chunkSize) break;
        }
        return deleted;
    }
}
