using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services.Accounts;

/// <summary>
/// Einmal-Codes (Anmelde-Übergabe, Passwort-Reset) ATOMAR entwerten (Codereview A1-008). Vorher: lesen → <c>UsedAt == null</c>
/// prüfen → setzen → speichern — zwei gleichzeitige Einlösungen konnten beide gelingen. Jetzt ein bedingtes
/// <c>UPDATE … SET UsedAt = now WHERE Id = … AND UsedAt IS NULL</c>; weiter geht es nur, wenn GENAU diese eine Zeile
/// getroffen wurde. Der InMemory-Test-Provider kann <c>ExecuteUpdate</c> nicht übersetzen → dort Re-Check gegen den
/// Datenbestand (gleiche Logik, ohne Nebenläufigkeitsgarantie; Muster wie <c>ChessableFetchLane.TryClaimAsync</c>).
/// </summary>
internal static class OneTimeCodes
{
    /// <param name="stillUnused">Die EINE Zeile, gefiltert auf „noch nicht verbraucht" (und ggf. „nicht abgelaufen").</param>
    /// <param name="usedAt">Die Spalte, die den Verbrauch markiert.</param>
    /// <returns><c>true</c>, wenn DIESER Aufruf den Code entwertet hat.</returns>
    public static async Task<bool> TryConsumeAsync<T>(AppDbContext db, IQueryable<T> stillUnused,
        Expression<Func<T, DateTime?>> usedAt, DateTime now, CancellationToken ct) where T : class
    {
        if (db.Database.IsRelational())
            return await stillUnused.ExecuteUpdateAsync(s => s.SetProperty(usedAt, now), ct) == 1;

        // InMemory: der Filter läuft gegen den Datenbestand, nicht gegen eine veraltet geladene Zeile.
        var row = await stillUnused.FirstOrDefaultAsync(ct);
        if (row is null || db.Entry(row).Property(usedAt).CurrentValue is not null) return false;
        db.Entry(row).Property(usedAt).CurrentValue = now;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
