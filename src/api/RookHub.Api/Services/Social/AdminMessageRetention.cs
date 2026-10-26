using RookHub.Api.Data;

namespace RookHub.Api.Services.Social;

/// <summary>
/// Frist der Admin-/Direktnachrichten (<c>AdminMessages</c>, Codereview 2026-09-29 O-7, Entscheidung 2026-10-25): GELESENE
/// Nachrichten älter als <see cref="RetentionOptions.MessagesDays"/> (Vorgabe 365 Tage) gehen; ungelesene bleiben, bis die
/// Gegenseite sie gesehen hat (dann greift die Frist beim nächsten Lauf). Gelesen = vom Empfänger gesehen: Admin-Nachricht
/// mit <c>SeenByUserAt</c>, Antwort des Nutzers mit <c>SeenByAdminAt</c>. Die Thread-Zeile (<c>MessageThreads</c>, nur
/// Übernahme durch einen Admin) bleibt. Läuft im täglichen Aufräumlauf
/// (<see cref="Puzzles.AnonymousDataRetentionService"/>).
/// </summary>
public static class AdminMessageRetention
{
    public static Task<int> PruneAsync(AppDbContext db, DateTime nowUtc, RetentionOptions o,
        int chunkSize = RetentionDelete.ChunkSize, CancellationToken ct = default)
    {
        var cutoff = nowUtc.AddDays(-o.MessagesDays);
        return RetentionDelete.AllAsync(db, db.AdminMessages.Where(m => m.CreatedAt < cutoff
            && (m.FromAdmin ? m.SeenByUserAt != null : m.SeenByAdminAt != null)), chunkSize, ct);
    }
}
