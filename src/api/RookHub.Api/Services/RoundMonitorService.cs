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
            // FALLE: `ex is not OperationCanceledException` schloss AUSGERECHNET den häufigsten Fall aus.
            // Ein HttpClient-Timeout zum Crawler wirft eine TaskCanceledException (= OperationCanceled)
            // OHNE dass `stoppingToken` gesetzt ist — die flog aus ExecuteAsync heraus, und weil
            // BackgroundServiceExceptionBehavior nirgends gesetzt ist, beendet der .NET-Default
            // (StopHost) den ganzen API-Prozess. Deshalb wie in allen anderen Hosted Services hier:
            // nur der ECHTE Shutdown darf durch.
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in RoundMonitorService loop");
            }

            try { await Task.Delay(30_000, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CheckAllMonitorsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proxy = scope.ServiceProvider.GetRequiredService<CrawlerProxyService>();
        await CheckAllMonitorsAsync(db, proxy,
            () => scope.ServiceProvider.GetRequiredService<NotificationService>(), ct);
    }

    /// <summary>Ein Durchlauf ueber alle Monitore; ohne Scope-Fabrik → direkt testbar.</summary>
    internal async Task CheckAllMonitorsAsync(AppDbContext db, CrawlerProxyService proxy,
        Func<NotificationService> notificationService, CancellationToken ct)
    {
        var crawls = new CrawlQueueClient(proxy);

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
        // CrawlerTournamentDbId > 0: defensiv gegen Alt-Datensaetze, die sonst
        // dauerhaft /api/tournaments/0/rounds/check pollen wuerden.
        var monitors = await db.TournamentMonitors
            .Where(m => m.ActiveUntil >= DateTime.UtcNow && m.CrawlerTournamentDbId > 0)
            .ToListAsync(ct);

        if (monitors.Count == 0) return;

        _logger.LogDebug("Checking {Count} active monitors", monitors.Count);

        foreach (var monitor in monitors)
        {
            try
            {
                var checkResult = await proxy.GetAsync(
                    $"/api/tournaments/{monitor.CrawlerTournamentDbId}/rounds/check", ct);

                var hasNewRound = checkResult.TryGetProperty("hasNewRound", out var hnr) && hnr.GetBoolean();

                if (hasNewRound)
                {
                    var newRounds = checkResult.TryGetProperty("newRoundNumbers", out var nrn)
                        ? nrn.ToString()
                        : "?";

                    _logger.LogInformation(
                        "New round detected for tournament {TournamentId} (DB {DbId}). New rounds: {NewRounds}",
                        monitor.CrawlerTournamentId, monitor.CrawlerTournamentDbId, newRounds);

                    // Der Crawl-Auftrag braucht die chess-results-NUMMER. Die gespeicherte Kennung ist
                    // je nach Einstiegsweg die Crawler-DB-Id (Turnierseite) — als Nummer gedeutet holte
                    // der Crawler ein fremdes Turnier. Deshalb ueber die eindeutige DB-Id aufloesen.
                    var resolved = await crawls.ResolveAsync(monitor.CrawlerTournamentDbId.ToString(), ct);
                    if (resolved is null || resolved.DbId != monitor.CrawlerTournamentDbId)
                        throw new InvalidOperationException(
                            $"Crawler kennt Turnier-DB-Id {monitor.CrawlerTournamentDbId} nicht (mehr)");
                    var chessResultsId = resolved.ChessResultsId;
                    // Abos und Favoriten desselben Turniers stehen unter JEDER der beiden Kennungen.
                    var tournamentKeys = new[]
                    {
                        monitor.CrawlerTournamentId, monitor.CrawlerTournamentDbId.ToString(), chessResultsId
                    }.Distinct().ToList();

                    // Trigger PairingsOnly crawl
                    var crawlBody = JsonSerializer.Deserialize<JsonElement>(
                        JsonSerializer.Serialize(new
                        {
                            chessResultsId,
                            jobType = "PairingsOnly"
                        }));

                    await proxy.PostAsync("/api/crawl", crawlBody, ct);

                    // Trigger player detail crawl for favorited players
                    try
                    {
                        var favSnrs = await db.TournamentFavorites
                            .Where(f => tournamentKeys.Contains(f.CrawlerTournamentId) && f.PlayerSnr != null)
                            .Select(f => f.PlayerSnr!.Value)
                            .Distinct()
                            .ToListAsync(ct);

                        if (favSnrs.Count > 0)
                        {
                            _logger.LogInformation(
                                "Triggering player detail crawl for {Count} favorited player(s) in tournament {TournamentId}",
                                favSnrs.Count, monitor.CrawlerTournamentId);

                            await proxy.PostJsonAsync("/api/crawl/player-details", new
                            {
                                chessResultsId,
                                playerSnrs = favSnrs
                            }, ct);
                        }
                    }
                    catch (Exception favEx) when (favEx is not OperationCanceledException)
                    {
                        _logger.LogWarning(favEx,
                            "Error triggering player detail crawl for tournament {TournamentId}",
                            monitor.CrawlerTournamentId);
                    }

                    // Update known rounds
                    if (checkResult.TryGetProperty("availableRounds", out var ar))
                        monitor.LastKnownRounds = ar.GetInt32();

                    // Abonnenten des Turniers per In-App-Glocke über die neue Runde informieren.
                    try
                    {
                        await NotifyNewRoundAsync(db, notificationService(), tournamentKeys,
                            monitor.CrawlerTournamentDbId, monitor.LastKnownRounds, ct);
                    }
                    catch (Exception notifyEx) when (notifyEx is not OperationCanceledException)
                    {
                        _logger.LogWarning(notifyEx,
                            "Error notifying subscribers of new round for tournament {TournamentId}",
                            monitor.CrawlerTournamentId);
                    }
                }

                monitor.LastCheckedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            // Auch hier: ein Crawler-Timeout ist eine OperationCanceledException ohne Shutdown — die darf
            // den Monitor-Durchlauf nicht verlassen, sonst reißt sie den ganzen Dienst mit.
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Error checking monitor for tournament {TournamentId}",
                    monitor.CrawlerTournamentId);
                // Die (teil-)geänderte Entität dieses Monitors aus dem GETEILTEN ChangeTracker der
                // Schleife nehmen: ein liegen gebliebener dirty Eintrag (z. B. DbUpdateConcurrency-
                // Exception, weil ein User den Monitor parallel abbestellt/gelöscht hat) ließe sonst
                // JEDEN folgenden SaveChangesAsync erneut scheitern — kein Monitor dahinter würde
                // mehr persistiert (LastCheckedAt/LastKnownRounds) und dessen „neue Runde"-
                // Benachrichtigungen feuerten beim nächsten 30-s-Durchlauf doppelt.
                db.Entry(monitor).State = EntityState.Detached;
            }
        }
    }

    /// <summary>
    /// Legt für alle Abonnenten eines Turniers eine „neue Runde"-Benachrichtigung an (In-App-Glocke,
    /// Link zur Turnier-Detailseite). No-op, wenn es keine Abonnenten gibt. Statisch + ohne Proxy →
    /// direkt testbar. <paramref name="tournamentKeys"/>: alle Kennungen des Turniers (DB-Id und
    /// chess-results-Nummer) — ein Abo aus dem Kalender traegt die Nummer, eins von der Turnierseite
    /// (Altbestand) die DB-Id; wer beide hat, bekommt trotzdem nur eine Meldung.
    /// </summary>
    internal static async Task NotifyNewRoundAsync(
        AppDbContext db, NotificationService notifications,
        IReadOnlyCollection<string> tournamentKeys, int tournamentDbId, int round, CancellationToken ct)
    {
        var subs = await db.TournamentSubscriptions
            .Where(s => tournamentKeys.Contains(s.CrawlerTournamentId))
            .Select(s => new { s.UserId, s.TournamentName })
            .ToListAsync(ct);
        if (subs.Count == 0) return;

        var name = subs.Select(s => s.TournamentName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "";
        await notifications.CreateManyAsync(
            subs.Select(s => s.UserId),
            Models.NotificationType.TournamentNewRound,
            new Dictionary<string, string> { ["tournamentName"] = name, ["round"] = round.ToString() },
            $"/tournaments/{tournamentDbId}");
    }
}
