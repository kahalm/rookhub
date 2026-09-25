using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using Serilog.Context;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Hintergrundpflege des eigenen Brokers: minütlich die im Speicher gesehenen Polls als <c>LastSeenAt</c>
/// in die Registrierungen schreiben (damit der Online-Punkt einen API-Neustart überlebt) und alle zehn
/// Minuten die Zähler je Engine protokollieren — nur, wenn sich seitdem etwas getan hat.
/// Das Aufräumen der Schlangen erledigt der Hub selbst (eigener Takt, läuft auch ohne diesen Dienst).
/// </summary>
public sealed class EngineBrokerMaintenanceService(
    IServiceScopeFactory scopes,
    EngineSelectorDirectory directory,
    EngineHub hub,
    LocalBrokerOptions options,
    ILogger<EngineBrokerMaintenanceService> logger) : BackgroundService
{
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StatsInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        var lastStats = DateTime.UtcNow;
        long statsVersion = 0;
        using var timer = new PeriodicTimer(FlushInterval);
        while (await WaitAsync(timer, stoppingToken))
        {
            try { await FlushLastSeenAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "EngineBroker: LastSeenAt nachtragen fehlgeschlagen");
            }

            if (DateTime.UtcNow - lastStats >= StatsInterval && hub.Stats.Version != statsVersion)
            {
                statsVersion = hub.Stats.Version;
                lastStats = DateTime.UtcNow;
                using var _ = LogContext.PushProperty("LogTags", LocalEngineBroker.LogTags);
                foreach (var (engineId, counters) in hub.Stats.Snapshot())
                    logger.LogInformation("EngineBroker: Stand engine={EngineId} {Counters}", engineId, counters);
            }
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    /// <summary>Schreibt die seit dem letzten Lauf gesehenen Selectors in <c>LastSeenAt</c> (öffentlich für Tests).</summary>
    public async Task FlushLastSeenAsync(CancellationToken ct)
    {
        var seen = directory.DrainSeen();
        if (seen.Count == 0) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var selectors = seen.Keys.ToList();
        var rows = await db.ExternalEngineRegistrations.Where(r => selectors.Contains(r.ProviderSelector)).ToListAsync(ct);
        foreach (var r in rows)
            if (seen.TryGetValue(r.ProviderSelector, out var t) && (r.LastSeenAt is null || r.LastSeenAt < t))
                r.LastSeenAt = t;
        await db.SaveChangesAsync(ct);
    }
}
