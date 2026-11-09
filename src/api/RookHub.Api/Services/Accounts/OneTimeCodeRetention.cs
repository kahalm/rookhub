using RookHub.Api.Data;

namespace RookHub.Api.Services.Accounts;

/// <summary>
/// Frist der Einmal-Codes (R1-015): <c>PasswordResetTokens</c> (Link 1 h, Admin-Link 24 h) und <c>AuthHandoffTokens</c>
/// (Sprung zwischen den Oberflächen, 60 s) wurden nur entwertet (<c>UsedAt</c>) bzw. beim nächsten Sprung desselben Kontos
/// weggeräumt — wer nie wieder springt oder ein Reset anfordert, behielt die Zeilen samt Zeitstempel für immer. Gelöscht
/// wird, was verbraucht oder abgelaufen ist und dessen Ende (<c>UsedAt</c>, sonst <c>ExpiresAt</c>) länger als
/// <see cref="MaxAgeAfterEnd"/> zurückliegt; ein noch gültiger Code bleibt immer. Läuft im täglichen Aufräumlauf
/// (<see cref="Puzzles.AnonymousDataRetentionService"/>). Übersicht: <c>docs/api/speicherdauer.md</c>.
/// </summary>
public static class OneTimeCodeRetention
{
    /// <summary>So lange bleibt ein erledigter Code noch stehen (Nachvollziehen eines Supportfalls „Link ging nicht“).</summary>
    public static readonly TimeSpan MaxAgeAfterEnd = TimeSpan.FromDays(7);

    public static async Task<int> PruneAsync(AppDbContext db, DateTime nowUtc, int chunkSize = RetentionDelete.ChunkSize,
        CancellationToken ct = default)
    {
        var cutoff = nowUtc - MaxAgeAfterEnd;
        var removed = await RetentionDelete.AllAsync(db, db.PasswordResetTokens.Where(t =>
            (t.UsedAt != null && t.UsedAt < cutoff) || t.ExpiresAt < cutoff), chunkSize, ct);
        removed += await RetentionDelete.AllAsync(db, db.AuthHandoffTokens.Where(t =>
            (t.UsedAt != null && t.UsedAt < cutoff) || t.ExpiresAt < cutoff), chunkSize, ct);
        return removed;
    }
}
