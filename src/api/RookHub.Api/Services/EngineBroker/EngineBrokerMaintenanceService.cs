using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using Serilog.Context;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Hintergrundpflege des eigenen Brokers: minütlich die im Speicher gesehenen Polls als <c>LastSeenAt</c>
/// in die Registrierungen schreiben (damit der Online-Punkt einen API-Neustart überlebt) und alle zehn
/// Minuten die Zähler je Engine protokollieren — nur, wenn sich seitdem etwas getan hat.
/// Das Aufräumen der Schlangen erledigt der Hub selbst (eigener Takt, läuft auch ohne diesen Dienst).
/// Dazu minütlich die Durchsatz-Zähler des Auftrags-Workers (<see cref="EngineThroughputRecorder"/>) — auch bei
/// abgeschaltetem eigenen Broker, die Lichess-Engines rechnen dann weiter.
/// </summary>
public sealed class EngineBrokerMaintenanceService(
    IServiceScopeFactory scopes,
    EngineSelectorDirectory directory,
    EngineHub hub,
    LocalBrokerOptions options,
    ILogger<EngineBrokerMaintenanceService> logger,
    EngineThroughputRecorder? throughput = null) : BackgroundService
{
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StatsInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled && throughput is null) return;
        var lastStats = DateTime.UtcNow;
        var lastPrune = DateTime.MinValue;
        long statsVersion = 0;
        using var timer = new PeriodicTimer(FlushInterval);
        while (await WaitAsync(timer, stoppingToken))
        {
            try { await FlushThroughputAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "EngineBroker: Durchsatz nachtragen fehlgeschlagen");
            }
            if (!options.Enabled) continue;

            try { await FlushLastSeenAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "EngineBroker: LastSeenAt nachtragen fehlgeschlagen");
            }

            if (DateTime.UtcNow - lastPrune >= PruneInterval)
            {
                lastPrune = DateTime.UtcNow;
                try { await PruneAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "EngineBroker: Aufraeumen alter Engines fehlgeschlagen");
                }
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

    /// <summary>Registrierungen, die sich länger als <see cref="LocalBrokerOptions.PruneAfter"/> nicht gemeldet haben,
    /// wegräumen (öffentlich für Tests).</summary>
    public async Task<int> PruneAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ExternalEngineRegistrationService>()
            .PruneStaleAsync(DateTime.UtcNow, options.PruneAfter, ct);
    }

    /// <summary>Durchsatz-Zuwächse in die Datenbank (öffentlich für Tests).</summary>
    public async Task FlushThroughputAsync(CancellationToken ct)
    {
        if (throughput is null) return;
        using var scope = scopes.CreateScope();
        await throughput.FlushAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), DateTime.UtcNow, ct);
    }

    /// <summary>Schreibt die seit dem letzten Lauf gesehenen Selectors in <c>LastSeenAt</c> und die beim Poll gesehenen
    /// Engine-Tokens in <c>ApiTokenId</c> (nur wenn die Zeile einen ANDEREN Wert trägt und dem Konto des Tokens gehört) —
    /// dieselbe Abfrage, ein <c>SaveChanges</c> (öffentlich für Tests). Scheitert das Schreiben, gehen die Tokens beim
    /// nächsten Mal mit.</summary>
    public async Task FlushLastSeenAsync(CancellationToken ct)
    {
        var seen = directory.DrainSeen();
        var tokens = directory.DrainTokens();
        if (seen.Count == 0 && tokens.Count == 0) return;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var selectors = seen.Keys.Union(tokens.Keys, StringComparer.Ordinal).ToList();
            var rows = await db.ExternalEngineRegistrations.Where(r => selectors.Contains(r.ProviderSelector)).ToListAsync(ct);
            var assigned = new List<(string EngineId, int? From, int To)>();
            foreach (var r in rows)
            {
                if (seen.TryGetValue(r.ProviderSelector, out var t) && (r.LastSeenAt is null || r.LastSeenAt < t))
                    r.LastSeenAt = t;
                if (tokens.TryGetValue(r.ProviderSelector, out var token) && token.UserId == r.UserId && r.ApiTokenId != token.TokenId)
                {
                    assigned.Add((r.Id, r.ApiTokenId, token.TokenId));
                    r.ApiTokenId = token.TokenId;
                }
            }
            await db.SaveChangesAsync(ct);
            foreach (var (engineId, from, to) in assigned)
                logger.LogInformation("EngineBroker: Engine-Token zugeordnet engine={EngineId} token={TokenId} (vorher {Previous})",
                    engineId, to, from?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "keiner");
        }
        catch
        {
            directory.RestoreTokens(tokens);
            throw;
        }
    }
}
