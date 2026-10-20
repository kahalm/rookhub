using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services.Social;

/// <summary>
/// Periodischer Poller, der für alle User mit verknüpftem Lichess-/chess.com-Konto und einem
/// effektiven Spielen-Ziel (&gt; 0 Partien/Woche) die gespielten Rapid-/Classical-Partien zählt
/// (siehe <see cref="PlayTimeService"/>). Läuft je User höchstens einmal pro Intervall und
/// sequentiell (rate-limit-schonend gegenüber den öffentlichen APIs).
///
/// Konfiguration: <c>PlayTime:IntervalHours</c> (Default 6).
/// </summary>
public class PlayTimeSyncService : BackgroundService, IBackgroundJobInfo
{
    // ── Admin-Schalter „Hintergrundjobs pausieren" (BackgroundPause): Takt fällt bei Pause aus. Nach dem Fortsetzen
    // kommt der Abgleich innerhalb von 30 s.
    private PauseGate? _pauseGate;
    private PauseGate Pause => _pauseGate ??= new PauseGate(nameof(PlayTimeSyncService), _logger);
    /// <inheritdoc/>
    public bool PausableByAdmin => true;
    /// <inheritdoc/>
    public DateTime? LastTickUtc => _pauseGate?.LastTickUtc;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlayTimeSyncService> _logger;
    private readonly TimeSpan _interval;

    public PlayTimeSyncService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<PlayTimeSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var hours = config.GetValue<double?>("PlayTime:IntervalHours") ?? 6.0;
        _interval = TimeSpan.FromHours(Math.Max(0.5, hours));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Kleiner Versatz nach Start (Migration/Aufwärmen durchlassen).
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (await Pause.SkipAndWaitAsync(_scopeFactory, PauseGate.Recheck, stoppingToken)) continue;
            await RunOnceAsync(stoppingToken);
            Pause.MarkTick();
            try { await Task.Delay(_interval, stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var goals = scope.ServiceProvider.GetRequiredService<TrainingGoalService>();
            var playTime = scope.ServiceProvider.GetRequiredService<PlayTimeService>();

            // Kandidaten: User mit verknüpftem Konto. Der Nutzerkreis (Coach + Schüler) ist klein,
            // daher pro Kandidat das effektive Ziel auflösen und nur bei Spielen-Ziel > 0 syncen.
            var linkedUserIds = await db.UserProfiles
                .Where(p => (p.LichessUsername != null && p.LichessUsername != "")
                         || (p.ChessComUsername != null && p.ChessComUsername != ""))
                .Select(p => p.UserId)
                .ToListAsync(ct);

            var synced = 0;
            foreach (var userId in linkedUserIds)
            {
                if (ct.IsCancellationRequested) break;
                var goal = await goals.GetEffectiveGoalAsync(userId);
                if (goal.PlayGames <= 0) continue;
                await playTime.SyncUserAsync(userId, ct);
                synced++;
            }
            if (synced > 0)
                _logger.LogInformation("PlayTimeSyncService: {Count} User synchronisiert.", synced);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "PlayTimeSyncService: Durchlauf fehlgeschlagen.");
        }
        // OperationCanceledException = App-Shutdown (stoppingToken) → kein Fehler. Propagiert sauber
        // aus ExecuteAsync (normaler BackgroundService-Stop), wird NICHT als Error geloggt
        // (sonst Fehlalarm im log-watcher bei jedem Deploy mitten im Sync).
    }
}
