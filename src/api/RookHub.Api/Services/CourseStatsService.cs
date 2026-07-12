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
        (page, pageSize) = Paging.Normalize(page, pageSize);

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
        // Themen: Buch-Puzzles haben KEINE normalisierte Tag-Tabelle (PuzzleTags gilt nur für
        // Standard-Puzzles), daher bleibt der Split des leerzeichen-/kommagetrennten Tags-Strings
        // in-memory. Aber nur die dafür nötigen Spalten {Solved, Tags} laden (nicht mehr Rating +
        // AttemptedAt je Zeile) — Bänder und Aktivität kommen jetzt als server-seitige Aggregate.
        var themeRows = await _db.CourseAttempts
            .Where(a => a.UserId == userId && a.BookPuzzle!.Tags != null && a.BookPuzzle.Tags != "")
            .Select(a => new { a.Solved, Tags = a.BookPuzzle!.Tags })
            .ToListAsync();
        var themeAgg = new Dictionary<string, (int attempts, int solved)>();
        foreach (var r in themeRows)
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

        // Rating-Bänder (200er-Schritte) — server-seitig, nur Versuche mit gesetztem BookRating.
        var ratingBands = (await _db.CourseAttempts
            .Where(a => a.UserId == userId && a.BookPuzzle!.BookRating != null)
            .GroupBy(a => a.BookPuzzle!.BookRating!.Value / 200)
            .Select(g => new { Bucket = g.Key, Attempts = g.Count(), Solved = g.Count(x => x.Solved) })
            .ToListAsync())
            .OrderBy(b => b.Bucket)
            .Select(b => new RatingBandStatDto { From = b.Bucket * 200, To = b.Bucket * 200 + 199, Attempts = b.Attempts, Solved = b.Solved })
            .ToList();

        // Aktivität pro Tag (letzte 365 Tage) — server-seitig via GROUP BY CAST(AttemptedAt AS date).
        var since = DateTime.UtcNow.Date.AddDays(-364);
        var activity = (await _db.CourseAttempts
            .Where(a => a.UserId == userId && a.AttemptedAt >= since)
            .GroupBy(a => a.AttemptedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync())
            .OrderBy(x => x.Day)
            .Select(x => new ActivityDayDto { Date = x.Day.ToString("yyyy-MM-dd"), Count = x.Count })
            .ToList();

        return new PuzzleBreakdownDto { Themes = themes, RatingBands = ratingBands, Activity = activity };
    }
}
