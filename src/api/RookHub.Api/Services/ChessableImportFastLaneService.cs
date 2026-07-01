using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Treibt die SCHNELLE Import-Lane: voll-gecachte Chessable-Kurse (<c>FullyCached==true</c>) brauchen
/// keinen Chessable-/VPN-Abruf (alle Linien liegen in der piratechess-DB) → sie dürfen sofort laufen,
/// statt hinter den langsamen, seriell/gepaced laufenden Downloads in der Queue zu warten.
///
/// Die Lane läuft NEBENLÄUFIG zur Download-Lane (die der <see cref="ChessableImportWatchdogService"/> +
/// die Background-Queue treiben) UND — da reine DB-/CPU-Arbeit ohne Netz/Block-Risiko — bis zu
/// <see cref="MaxParallel"/> gecachte Importe GLEICHZEITIG (Config <c>Chessable:FastLaneParallelism</c>,
/// Default 3). Jeder Drain läuft in einem eigenen DI-Scope (eigener <see cref="AppDbContext"/>) und
/// übernimmt seinen Job ATOMAR (<c>ExecuteUpdate</c> queued→claimed in <c>ChessableImportService</c>),
/// sodass parallele Drains garantiert verschiedene Jobs greifen (keine Doppelverarbeitung).
/// Eigener kontinuierlicher Loop statt Queue-Ticket → immun gegen den bounded-DropOldest-Ticketverlust,
/// der die Importe schon einmal „einschlafen" ließ.
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
    /// <summary>Wie viele gecachte Importe gleichzeitig laufen dürfen (netzfrei → gefahrlos parallel).</summary>
    internal int MaxParallel = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChessableImportFastLaneService> _logger;

    public ChessableImportFastLaneService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<ChessableImportFastLaneService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        MaxParallel = Math.Clamp(config.GetValue("Chessable:FastLaneParallelism", 3), 1, 8);
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

    /// <summary>Stößt bis zu <see cref="MaxParallel"/> wartende gecachte Importe GLEICHZEITIG an (so viele,
    /// wie freie Slots UND wartende Jobs es gibt). Liefert <c>true</c>, wenn mindestens einer angestoßen
    /// wurde (dann zügig erneut prüfen statt voll zu warten).</summary>
    internal async Task<bool> TickAsync(CancellationToken ct)
    {
        int slots;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            slots = await FreeSlotsAsync(db, MaxParallel, ct);
        }
        if (slots <= 0) return false;

        // Jeder Drain in EIGENEM Scope/DbContext (DbContext ist nicht thread-safe); der atomare Claim
        // sorgt dafür, dass parallele Drains verschiedene Jobs greifen.
        var tasks = new List<Task>(slots);
        for (var i = 0; i < slots; i++) tasks.Add(DriveOneAsync(ct));
        await Task.WhenAll(tasks);
        return true;
    }

    private async Task DriveOneAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<ChessableImportService>();
            await svc.RunNextAsync(ct, fastLane: true);   // claimt + verarbeitet EINEN gecachten Job
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogError(ex, "Chessable-Import-Fast-Lane: paralleler Drain fehlgeschlagen"); }
    }

    /// <summary>Wie viele gecachte Importe JETZT zusätzlich starten dürfen:
    /// <c>min(MaxParallel − aktuell inflight, wartende gecachte Jobs)</c>, mindestens 0. Rein/testbar.</summary>
    internal static async Task<int> FreeSlotsAsync(AppDbContext db, int maxParallel, CancellationToken ct = default)
    {
        var queued = await db.ChessableImports
            .CountAsync(i => i.Status == "running" && i.Phase == "queued" && i.FullyCached == true, ct);
        if (queued == 0) return 0;

        var inflight = await db.ChessableImports
            .CountAsync(i => i.Status == "running" && i.FullyCached == true && InflightPhases.Contains(i.Phase), ct);
        return Math.Max(0, Math.Min(maxParallel - inflight, queued));
    }
}
