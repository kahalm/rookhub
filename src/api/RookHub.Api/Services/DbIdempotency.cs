using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Gemeinsames Idempotenz-Muster für Inserts, die mit einem parallelen Request um denselben
/// Unique-Index rennen.
/// FALLE: das früher überall kopierte »catch (DbUpdateException) { ChangeTracker.Clear(); }«
/// schluckte JEDEN Persistenzfehler — FK-Verletzung, „data too long", Timeout nach erschöpften
/// Retries — und meldete dem Aufrufer trotzdem Erfolg. Ein fehlgeschlagener Solve-Insert ging so
/// still verloren (kein Log; Program.cs dämpft zusätzlich das EF-eigene SaveChangesFailed-Event auf
/// Information). Hier wird ausschließlich der echte Duplikat-Fehler geschluckt, alles andere fliegt
/// weiter nach oben.
/// </summary>
public static class DbIdempotency
{
    /// <summary>Speichert; ein Unique-Index-Race gilt als „ist schon da" (ChangeTracker leeren +
    /// Debug-Log). Rückgabe: <c>true</c> = gespeichert, <c>false</c> = Race erkannt.</summary>
    public static async Task<bool> SaveIgnoringUniqueRaceAsync(this AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (AuthService.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            logger?.LogDebug(ex, "Unique-Index-Race beim Speichern — idempotent behandelt.");
            return false;
        }
    }
}
