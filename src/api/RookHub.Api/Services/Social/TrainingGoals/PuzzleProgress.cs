using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services.Social.TrainingGoals;

/// <summary>
/// Quelle „randomPuzzle": Standard-Puzzle, Tages-/Einzel-Buch-Puzzle, Endlos-Sitzungen und Wochenpost-Versuche.
/// <see cref="LoadAsync"/> fragt ab, <see cref="Merge"/> führt rein zusammen.
/// </summary>
internal static class PuzzleProgress
{
    public sealed record StandardRow(DateTime At, int Seconds, string? Themes);
    public sealed record BookRow(DateTime At, int Seconds, string? Tags, string? Chapter);
    public sealed record TimedRow(DateTime At, int Seconds);
    public sealed record Rows(List<StandardRow> Standard, List<BookRow> Book, List<TimedRow> Endless, List<TimedRow> WeeklyPost);

    public static async Task AddAsync(AppDbContext db, int userId, DateTime windowStartUtc, TrainingDays days)
        => Merge(await LoadAsync(db, userId, windowStartUtc), days);

    public static async Task<Rows> LoadAsync(AppDbContext db, int userId, DateTime windowStartUtc)
    {
        var standard = await (
            from a in db.PuzzleAttempts.AsNoTracking()
            where a.UserId == userId && a.AttemptedAt >= windowStartUtc
            join p in db.Puzzles.AsNoTracking() on a.PuzzleId equals p.Id
            select new StandardRow(a.AttemptedAt, a.TimeSpentSeconds, p.Themes)).ToListAsync();

        var book = await (
            from a in db.BookPuzzleAttempts.AsNoTracking()
            where a.UserId == userId && a.AttemptedAt >= windowStartUtc
            join bp in db.BookPuzzles.AsNoTracking() on a.BookPuzzleId equals bp.Id
            select new BookRow(a.AttemptedAt, a.TimeSeconds, bp.Tags, bp.Chapter)).ToListAsync();

        var endless = await db.EndlessSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.CreatedAt >= windowStartUtc)
            .Select(s => new TimedRow(s.CreatedAt, s.DurationSeconds)).ToListAsync();

        var weekly = await db.WeeklyPostAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.AttemptedAt >= windowStartUtc)
            .Select(a => new TimedRow(a.AttemptedAt, a.TimeSeconds)).ToListAsync();

        return new Rows(standard, book, endless, weekly);
    }

    public static void Merge(Rows rows, TrainingDays days)
    {
        // Standard-Puzzle-Versuche — Thema aus Lichess-Themes (Phase, sonst Taktik).
        foreach (var a in rows.Standard)
            days.AddTime(a.At, a.Seconds, TrainingCaps.PerPuzzleCapSeconds, TrainingSource.RandomPuzzle,
                TrainingThemes.PhaseFromText(a.Themes) ?? TrainingTheme.Tactics);

        // Tages-/Einzel-Buch-Puzzle — Thema aus BookPuzzle-Tags/Chapter, sonst Taktik.
        foreach (var a in rows.Book)
            days.AddTime(a.At, a.Seconds, TrainingCaps.PerPuzzleCapSeconds, TrainingSource.RandomPuzzle,
                TrainingThemes.PhaseFromText(a.Tags, a.Chapter) ?? TrainingTheme.Tactics);

        // Endlos-Sessions — inhaltlich Taktik.
        foreach (var s in rows.Endless)
            days.AddTime(s.At, s.Seconds, TrainingCaps.PerSessionCapSeconds, TrainingSource.RandomPuzzle, TrainingTheme.Tactics);

        // Wochenpost-Puzzle-Versuche — der Kurator legt sie als Taktik-Set an, daher fest als Taktik verbucht
        // (kein Kind-/Tag-Routing). Cap wie Standard-/Buch-Puzzle.
        foreach (var a in rows.WeeklyPost)
            days.AddTime(a.At, a.Seconds, TrainingCaps.PerPuzzleCapSeconds, TrainingSource.RandomPuzzle, TrainingTheme.Tactics);
    }
}
