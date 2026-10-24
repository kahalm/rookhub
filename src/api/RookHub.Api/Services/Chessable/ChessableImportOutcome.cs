using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Abschluss eines Chessable-Import-Datensatzes: Fehlschlag festschreiben (samt Bearer-Breaker und Benachrichtigung)
/// bzw. die Fertig-Benachrichtigung schicken. Teil von <see cref="ChessableImportService"/> (Zerlegung Z3), von der
/// Fassade mit deren scoped Abhängigkeiten gebaut; loggt unter der Quelle der Fassade.
/// </summary>
internal sealed class ChessableImportOutcome
{
    private readonly AppDbContext _db;
    private readonly ChessableBearerBreaker _breaker;
    private readonly NotificationService _notifications;
    private readonly ILogger<ChessableImportService> _logger;

    public ChessableImportOutcome(AppDbContext db, ChessableBearerBreaker breaker, NotificationService notifications,
        ILogger<ChessableImportService> logger)
    {
        _db = db;
        _breaker = breaker;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>Benachrichtigung „Kurs fertig importiert" → direkt zur passenden Ansicht.</summary>
    public Task NotifyCompletedAsync(ChessableImport import, string courseName, string queueTime, string fetchTime)
        => _notifications.CreateAsync(import.UserId, NotificationType.ChessableImportCompleted,
            ChessableImportProgress.CompletedData(courseName, import.Target, queueTime, fetchTime),
            ChessableImportTargets.ResultLink(import.Target));

    public async Task FailAsync(ChessableImport import, string error)
    {
        import.Status = ChessableImportStatus.Failed;
        import.Error = ChessableImportTargets.Trunc(error, 1000);
        import.CompletedAt = DateTime.UtcNow;
        // Terminalen Status mit CancellationToken.None festschreiben: scheitert ein Import WEGEN einer
        // (Timeout-)Cancellation, muss er trotzdem als "failed" persistiert werden — sonst bliebe er als
        // Zombie auf "running" haengen und wuerde beim Neustart endlos resumed.
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // FALLE: Ist die DB gerade weg (Ausfall/Timeout), scheitert AUCH dieses Fehler-Update — die
            // Exception liefe sonst aus RunAsync bis in den Worker und der Import bliebe für immer auf
            // "fetching"/"importing" stehen, also als Zombie im Lane-Slot (Drain steht bis zum Neustart).
            // Darum hier schlucken: der Watchdog erkennt den verwaisten Inflight-Job und reiht ihn neu ein.
            _logger.LogError(ex, "Chessable-Import {Id}: Fehlerstatus konnte nicht gespeichert werden", import.Id);
            return;
        }

        // Circuit-Breaker: war der Fehlschlag fatal für den Bearer selbst (Account gesperrt/gelöscht
        // bzw. Token tot), den Bearer sperren — danach pausieren weitere Importe sofort, statt mit dem
        // toten Bearer immer wieder fehlzuschlagen (siehe ChessableBearerBreaker). IP-/VPN-Blocks lösen
        // das bewusst NICHT aus.
        // Nachgelagerte Nebenwirkungen dürfen den bereits festgeschriebenen Fehlerstatus nicht mehr
        // gefährden: eine Exception von hier liefe erneut in den Worker (Fehlalarm im log-watcher),
        // ohne dass am Import-Satz noch etwas zu retten wäre.
        try
        {
            if (ChessableBearerBreaker.IsBearerFatal(error))
                await _breaker.TripAsync(import.BearerUserId ?? import.UserId, error, CancellationToken.None);

            // User benachrichtigen: Import fehlgeschlagen.
            var name = ChessableImportTargets.NameOrFallback(import.CourseName, import.Bid);
            await _notifications.CreateAsync(import.UserId, NotificationType.ChessableImportFailed,
                new Dictionary<string, string> { ["courseName"] = name }, "/chessable");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chessable-Import {Id}: Nachbereitung des Fehlschlags fehlgeschlagen", import.Id);
        }
    }
}
