using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services.Tournaments;

/// <summary>
/// Tägliche Schleife der Turnier-Abos (Zerlegung Z10, Codereview A8-013): je Takt erst alle Nutzer mit Schach-Identität
/// prüfen (neue Abos + Auto-Favoriten), dann die laufenden Abos neu crawlen lassen. Die Fachlogik steht in
/// <see cref="AutoSubscriptionService"/>; Takt, Pause und Fehlerpolitik in <see cref="PeriodicWorker"/>.
///
/// <para>Logger bewusst <c>ILogger&lt;AutoSubscriptionService&gt;</c> und die Zeilen wörtlich wie vor der Trennung —
/// Quelle und Message-Template sind die Signatur, nach der Kibana und der log-watcher gruppieren.</para>
/// </summary>
public sealed class AutoSubscriptionScheduler : PeriodicWorker
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AutoSubscriptionScheduler(IServiceScopeFactory scopeFactory, ILogger<AutoSubscriptionService> logger)
        : base(scopeFactory, logger)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Initiale Pause vor dem ersten Lauf. Beim Deploy startet die API regelmaessig ein paar
    /// Sekunden vor dem Crawler (gluetun:8080); ohne diese Pause liefe der erste Check sofort
    /// ins "Connection refused". Da der Service ohnehin nur alle 24 h laeuft, kostet die Pause nichts.
    /// (internal settable nur fuer Tests.)
    /// </summary>
    internal TimeSpan FirstRunDelay { get; set; } = TimeSpan.FromSeconds(60);

    protected override WorkerSchedule Schedule => WorkerSchedule.Every(TimeSpan.FromHours(24));

    protected override WorkerStart Start => WorkerStart.After(FirstRunDelay);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("AutoSubscriptionService started");
        return base.ExecuteAsync(stoppingToken);
    }

    /// <summary>Beide Phasen fangen ihre Fehler selbst (je eigene Zeile), damit die zweite auch nach einem Fehler der
    /// ersten läuft.</summary>
    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        try
        {
            await CheckAllUsersAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Unexpected error in AutoSubscriptionService loop");
        }

        try
        {
            var db = services.GetRequiredService<AppDbContext>();
            var proxy = services.GetRequiredService<CrawlerProxyService>();
            await services.GetRequiredService<AutoSubscriptionService>().RefreshActiveSubscriptionsAsync(db, proxy, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Unexpected error in AutoSubscriptionService refresh");
        }
    }

    /// <summary>Erreicht nur eine Abbruch-Ausnahme ohne Herunterfahren (alles andere fangen die Phasen selbst).</summary>
    protected override void LogFailure(Exception ex) => Logger.LogError(ex, "Unexpected error in AutoSubscriptionService loop");

    private async Task CheckAllUsersAsync(CancellationToken ct)
    {
        // Kandidaten-User einmal mit einem kurzlebigen Scope laden …
        List<int> userIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            userIds = await db.UserProfiles
                .Where(p => p.ChessResultsId != null && p.LastName != null)
                .Select(p => p.UserId)
                .ToListAsync(ct);
        }

        Logger.LogInformation("AutoSubscriptionService checking {Count} users", userIds.Count);

        // … dann pro User einen FRISCHEN Scope/DbContext. Vorher teilte sich der ganze Lauf einen Context:
        // der ChangeTracker wuchs über alle User unbeschränkt (jedes spätere SaveChanges langsamer) und ein
        // Teilfehler konnte getrackte Entitäten zwischen Usern verschleppen. Jetzt ist jeder User isoliert.
        foreach (var userId in userIds)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var proxy = scope.ServiceProvider.GetRequiredService<CrawlerProxyService>();
                var autoSub = scope.ServiceProvider.GetRequiredService<AutoSubscriptionService>();
                await autoSub.CheckUserAsync(db, proxy, userId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogWarning(ex, "AutoSubscription check failed for user {UserId}", userId);
            }
        }
    }
}
