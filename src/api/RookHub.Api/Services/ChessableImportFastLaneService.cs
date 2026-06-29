using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Treibt die SCHNELLE Import-Lane: voll-gecachte Chessable-Kurse (<c>FullyCached==true</c>) brauchen
/// keinen Chessable-/VPN-Abruf (alle Linien liegen in der piratechess-DB) → sie dürfen sofort laufen,
/// statt hinter den langsamen, seriell/gepaced laufenden Downloads in der Queue zu warten.
///
/// Die Lane läuft SERIELL (immer höchstens ein gecachter Import gleichzeitig — reine DB-/CPU-Arbeit),
/// aber NEBENLÄUFIG zur Download-Lane (die der <see cref="ChessableImportWatchdogService"/> + die
/// Background-Queue treiben). Eigener kontinuierlicher Loop statt Queue-Ticket → immun gegen den
/// bounded-DropOldest-Ticketverlust, der die Importe schon einmal „einschlafen" ließ.
///
/// Klassifiziert wird beim Anlegen (<c>IsCourseCachedAsync</c>); unklassifizierte (null) Jobs gelten
/// als Download und werden hier NICHT angefasst → nichts kann hängen bleiben.
/// </summary>
public class ChessableImportFastLaneService : BackgroundService
{
    /// <summary>Phasen, in denen ein Import aktiv bearbeitet wird (nicht bloß wartend).</summary>
    internal static readonly string[] InflightPhases = { "claimed", "fetching", "importing" };

    internal TimeSpan StartupDelay = TimeSpan.FromSeconds(20);
    internal TimeSpan IdleInterval = TimeSpan.FromSeconds(20);
    internal TimeSpan BusyDelay = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChessableImportFastLaneService> _logger;

    public ChessableImportFastLaneService(IServiceScopeFactory scopeFactory, ILogger<ChessableImportFastLaneService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            bool drove = false;
            try { drove = await TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Chessable-Import-Fast-Lane: Tick fehlgeschlagen"); }

            try { await Task.Delay(drove ? BusyDelay : IdleInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Verarbeitet GENAU einen wartenden gecachten Import, wenn die Fast-Lane frei ist.
    /// Liefert <c>true</c>, wenn einer angestoßen wurde (dann zügig erneut prüfen statt voll zu warten).</summary>
    internal async Task<bool> TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await IsFastLaneReadyAsync(db, ct)) return false;

        var svc = scope.ServiceProvider.GetRequiredService<ChessableImportService>();
        await svc.RunNextAsync(ct, fastLane: true);   // claimt + verarbeitet einen gecachten Job (seriell)
        return true;
    }

    /// <summary>Es wartet mindestens ein gecachter Import (Phase "queued", <c>FullyCached==true</c>) UND
    /// in der Fast-Lane läuft gerade keiner (seriell). Rein/testbar.</summary>
    internal static async Task<bool> IsFastLaneReadyAsync(AppDbContext db, CancellationToken ct = default)
    {
        var hasQueued = await db.ChessableImports
            .AnyAsync(i => i.Status == "running" && i.Phase == "queued" && i.FullyCached == true, ct);
        if (!hasQueued) return false;

        var inflight = await db.ChessableImports
            .AnyAsync(i => i.Status == "running" && i.FullyCached == true && InflightPhases.Contains(i.Phase), ct);
        return !inflight;
    }
}
