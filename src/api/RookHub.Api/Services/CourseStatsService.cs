using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// Aggregierte Kurs-Statistik des Users über ALLE Kurse (Pendant zu <see cref="PuzzleStatsService"/>,
/// aber auf <c>CourseAttempt</c> statt <c>PuzzleAttempt</c> und ohne Elo — Kurs-Puzzles haben kein
/// User-Elo). Aus <see cref="CourseService"/> ausgegliedert; rein lesend auf <see cref="AppDbContext"/>
/// und unabhängig von der Kurs-Fortschritts-/Nächstes-Puzzle-Logik.
/// </summary>
public class CourseStatsService
{
    private readonly AppDbContext _db;

    public CourseStatsService(AppDbContext db) => _db = db;

    /// <summary>Aggregierte Kurs-Statistik des Users über ALLE Kurse (Quelle: append-only
    /// <see cref="Models.CourseAttempt"/>). Streaks aus den letzten 1000 Versuchen, wie bei Standard-Puzzles.</summary>
    public async Task<CourseStatsDto> GetStatsAsync(int userId)
    {
        var totalAttempts = await _db.CourseAttempts.CountAsync(a => a.UserId == userId);
        if (totalAttempts == 0)
            return new CourseStatsDto();

        var solved = await _db.CourseAttempts.CountAsync(a => a.UserId == userId && a.Solved);
        var accuracy = (double)solved / totalAttempts * 100;

        var recentResults = await _db.CourseAttempts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AttemptedAt)
            .Take(1000)
            .Select(a => a.Solved)
            .ToListAsync();

        var currentStreak = 0;
        foreach (var s in recentResults)
        {
            if (s) currentStreak++;
            else break;
        }

        var bestStreak = 0;
        var streak = 0;
        foreach (var s in recentResults)
        {
            if (s) { streak++; bestStreak = Math.Max(bestStreak, streak); }
            else streak = 0;
        }

        return new CourseStatsDto
        {
            TotalAttempts = totalAttempts,
            Solved = solved,
            Accuracy = Math.Round(accuracy, 1),
            CurrentStreak = currentStreak,
            BestStreak = bestStreak,
        };
    }

    /// <summary>Paginierte Kurs-Versuchs-History des Users (neueste zuerst), inkl. Buch-Puzzle-Infos.</summary>
    public async Task<List<CourseAttemptDto>> GetHistoryAsync(int userId, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        return await _db.CourseAttempts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AttemptedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(a => a.BookPuzzle)
            .Select(a => new CourseAttemptDto
            {
                BookPuzzleId = a.BookPuzzleId,
                LineId = a.BookPuzzle!.LineId,
                Title = a.BookPuzzle.Title,
                BookFileName = a.BookPuzzle.BookFileName,
                BookRating = a.BookPuzzle.BookRating,
                Difficulty = a.BookPuzzle.Difficulty,
                Solved = a.Solved,
                TimeSeconds = a.TimeSeconds,
                AttemptedAt = a.AttemptedAt,
            })
            .ToListAsync();
    }

    /// <summary>Aufschlüsselung der Kurs-Versuche nach Tag/Thema, Rating-Band und Aktivität
    /// (gleiche Form wie bei Standard-Puzzles, daher <see cref="PuzzleBreakdownDto"/>). Themen aus
    /// <c>BookPuzzle.Tags</c> (leerzeichen-/kommagetrennt), Bänder aus <c>BookPuzzle.BookRating</c>.</summary>
    public async Task<PuzzleBreakdownDto> GetBreakdownAsync(int userId)
    {
        var rows = await _db.CourseAttempts
            .Where(a => a.UserId == userId)
            .Select(a => new { a.Solved, a.AttemptedAt, Rating = a.BookPuzzle!.BookRating, Tags = a.BookPuzzle.Tags })
            .ToListAsync();

        // Themen (Buch-Tags sind leerzeichen- oder kommagetrennt im Tags-String).
        var themeAgg = new Dictionary<string, (int attempts, int solved)>();
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.Tags)) continue;
            foreach (var theme in r.Tags.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var (att, sol) = themeAgg.TryGetValue(theme, out var v) ? v : (0, 0);
                themeAgg[theme] = (att + 1, sol + (r.Solved ? 1 : 0));
            }
        }
        var themes = themeAgg
            .Select(kv => new ThemeStatDto { Theme = kv.Key, Attempts = kv.Value.attempts, Solved = kv.Value.solved })
            .OrderByDescending(t => t.Attempts).ThenBy(t => t.Theme)
            .Take(20).ToList();

        // Rating-Bänder (200er-Schritte) — nur Versuche mit gesetztem BookRating.
        var bandAgg = new Dictionary<int, (int attempts, int solved)>();
        foreach (var r in rows)
        {
            if (r.Rating == null) continue;
            var bucket = (r.Rating.Value / 200) * 200;
            var (att, sol) = bandAgg.TryGetValue(bucket, out var v) ? v : (0, 0);
            bandAgg[bucket] = (att + 1, sol + (r.Solved ? 1 : 0));
        }
        var ratingBands = bandAgg
            .OrderBy(kv => kv.Key)
            .Select(kv => new RatingBandStatDto { From = kv.Key, To = kv.Key + 199, Attempts = kv.Value.attempts, Solved = kv.Value.solved })
            .ToList();

        // Aktivität pro Tag (letzte 365 Tage).
        var since = DateTime.UtcNow.Date.AddDays(-364);
        var activity = rows
            .Where(r => r.AttemptedAt.Date >= since)
            .GroupBy(r => r.AttemptedAt.Date)
            .OrderBy(g => g.Key)
            .Select(g => new ActivityDayDto { Date = g.Key.ToString("yyyy-MM-dd"), Count = g.Count() })
            .ToList();

        return new PuzzleBreakdownDto { Themes = themes, RatingBands = ratingBands, Activity = activity };
    }
}
