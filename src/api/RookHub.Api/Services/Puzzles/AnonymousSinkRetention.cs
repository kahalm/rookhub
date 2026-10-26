using RookHub.Api.Data;

namespace RookHub.Api.Services.Puzzles;

/// <summary>
/// Die vier anonymen Versuchs-Senken ohne Frist (Codereview 2026-09-29 A2-012, Entscheidung 2026-10-25): anonyme
/// Punktepartien (<c>GuessSessions</c> samt <c>GuessMoves</c>), Buch-/Tagespuzzle-Versuche (<c>BookPuzzleAttempts</c>),
/// Versuche an geteilten Puzzles (<c>SharedPuzzleAttempts</c>, Kennung <c>s:…</c>) und Puzzle-Versuche
/// (<c>PuzzleAttempts</c>). Ihre Deckel „je Session" banden nichts, weil die Session-Id frei wählbar ist. Jetzt: älter als
/// <see cref="RetentionOptions.AnonymousDays"/> weg, und je Senke höchstens <see cref="RetentionOptions.AnonymousCap"/> Zeilen
/// (älteste zuerst). Zeilen angemeldeter Nutzer bleiben immer.
///
/// <para>Folge: die Zahl anonymer Löser eines Tagespuzzles (<c>AnonymousSolvedCount</c>) sinkt für Puzzles, die älter als die
/// Frist sind — bewusst in Kauf genommen.</para>
/// </summary>
public static class AnonymousSinkRetention
{
    /// <summary>Ein Durchlauf über alle vier Senken; liefert die Zahl gelöschter Zeilen (Kopfzeilen, ohne GuessMoves).</summary>
    public static async Task<int> PruneAsync(AppDbContext db, DateTime nowUtc, RetentionOptions o,
        int chunkSize = RetentionDelete.ChunkSize, CancellationToken ct = default)
    {
        var cutoff = nowUtc.AddDays(-o.AnonymousDays);
        var removed = 0;

        var guess = db.GuessSessions.Where(s => s.UserId == null);
        Task DropMoves(IReadOnlyList<int> ids) =>
            RetentionDelete.AllAsync(db, db.GuessMoves.Where(m => ids.Contains(m.GuessSessionId)), chunkSize, ct);
        removed += await RetentionDelete.AllAsync(db, guess.Where(s => (s.FinishedAt ?? s.StartedAt) < cutoff), chunkSize, ct, DropMoves);
        removed += await RetentionDelete.OverCapAsync(db, guess, s => s.FinishedAt ?? s.StartedAt, o.AnonymousCap, chunkSize, ct, DropMoves);

        var book = db.BookPuzzleAttempts.Where(a => a.UserId == null);
        removed += await RetentionDelete.AllAsync(db, book.Where(a => a.AttemptedAt < cutoff), chunkSize, ct);
        removed += await RetentionDelete.OverCapAsync(db, book, a => a.AttemptedAt, o.AnonymousCap, chunkSize, ct);

        var shared = db.SharedPuzzleAttempts.Where(a => a.IdentityKey.StartsWith("s:"));
        removed += await RetentionDelete.AllAsync(db, shared.Where(a => a.CreatedAt < cutoff), chunkSize, ct);
        removed += await RetentionDelete.OverCapAsync(db, shared, a => a.CreatedAt, o.AnonymousCap, chunkSize, ct);

        var puzzle = db.PuzzleAttempts.Where(a => a.UserId == null);
        removed += await RetentionDelete.AllAsync(db, puzzle.Where(a => a.AttemptedAt < cutoff), chunkSize, ct);
        removed += await RetentionDelete.OverCapAsync(db, puzzle, a => a.AttemptedAt, o.AnonymousCap, chunkSize, ct);
        return removed;
    }
}
