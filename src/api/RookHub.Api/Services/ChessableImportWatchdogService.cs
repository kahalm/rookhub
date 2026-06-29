using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Sicherheitsnetz für den Chessable-Import-Drain. Der normale Antrieb ist ein Ticket je Import in der
/// IN-MEMORY <see cref="IBackgroundTaskQueue"/> — und der kann Jobs liegen lassen: die Queue ist
/// bounded (<c>BoundedChannelFullMode.DropOldest</c>), ein großer Schwung Importe auf einmal verwirft
/// also die ältesten Tickets; zudem reiht ein FERTIGER Job den nächsten nicht automatisch nach (nur
/// das Anlegen und ein Stillstand reihen nach). Folge: Importe bleiben auf <c>Status="running"</c> /
/// <c>Phase="queued"</c> liegen, obwohl gar nichts mehr läuft (Vorfall 2026-06-29: 82 wartende, kein
/// aktiver — Drain erst nach API-Neustart via <see cref="ChessableImportResumeService"/> wieder an).
///
/// Dieser Watchdog prüft periodisch: gibt es wartende Importe (Phase "queued") UND ist KEINER aktiv
/// (Phase "claimed"/"fetching"/"importing")? Dann stößt er den nächsten Job DIREKT an
/// (<see cref="ChessableImportService.RunNextAsync"/>) — bewusst OHNE die bounded Queue, damit das
/// Nachfüllen nicht selbst wieder verworfen werden kann und auch ein hängender Queue-Consumer den
/// Drain nicht blockiert. Solange etwas läuft, hält er sich raus (kein Über-Parallelisieren).
/// </summary>
public class ChessableImportWatchdogService : BackgroundService
{
    /// <summary>Phasen, in denen ein Import AKTIV bearbeitet wird (nicht bloß wartend). Solange einer
    /// davon belegt ist, läuft der Drain — der Watchdog greift dann nicht ein.</summary>
    internal static readonly string[] InflightPhases = { "claimed", "fetching", "importing" };

    // Intern überschreibbar für Tests. Startverzögerung lässt den ResumeService beim Hochfahren zuerst
    // greifen; danach im Ruhe-Takt prüfen, nach einem Anstoß zügig weiterdrainen.
    internal TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    internal TimeSpan IdleInterval = TimeSpan.FromMinutes(2);
    internal TimeSpan BusyDelay = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChessableImportWatchdogService> _logger;

    public ChessableImportWatchdogService(IServiceScopeFactory scopeFactory, ILogger<ChessableImportWatchdogService> logger)
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
            try
            {
                drove = await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chessable-Import-Watchdog: Tick fehlgeschlagen");
            }

            try { await Task.Delay(drove ? BusyDelay : IdleInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Ein Durchlauf: stößt den nächsten wartenden Import an, wenn der Drain steht.
    /// Liefert <c>true</c>, wenn angestoßen wurde (dann zügig erneut prüfen statt voll zu warten).</summary>
    internal async Task<bool> TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await IsDrainStalledAsync(db, ct)) return false;

        var queued = await db.ChessableImports.CountAsync(i => i.Status == "running" && i.Phase == "queued", ct);
        _logger.LogWarning(
            "Chessable-Import-Watchdog: {Queued} wartende Importe, kein aktiver — stoße den Drain an", queued);

        var svc = scope.ServiceProvider.GetRequiredService<ChessableImportService>();
        await svc.RunNextAsync(ct);   // claimt + verarbeitet GENAU einen wartenden Job (atomar)
        return true;
    }

    /// <summary>Der Drain steht: mindestens ein Import wartet (Phase "queued") und KEINER ist gerade
    /// aktiv (Phase "claimed"/"fetching"/"importing"). Rein/testbar.</summary>
    internal static async Task<bool> IsDrainStalledAsync(AppDbContext db, CancellationToken ct = default)
    {
        var hasQueued = await db.ChessableImports
            .AnyAsync(i => i.Status == "running" && i.Phase == "queued", ct);
        if (!hasQueued) return false;

        var hasInflight = await db.ChessableImports
            .AnyAsync(i => i.Status == "running" && InflightPhases.Contains(i.Phase), ct);
        return !hasInflight;
    }
}
