using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Aggregierte Kurs-Statistik des Users über ALLE Kurse (Pendant zu <see cref="PuzzleStatsService"/>,
/// aber auf <c>CourseAttempt</c> statt <c>PuzzleAttempt</c> und ohne Elo — Kurs-Puzzles haben kein
/// User-Elo). Aus <see cref="CourseService"/> ausgegliedert; rein lesend auf <see cref="AppDbContext"/>
/// und unabhängig von der Kurs-Fortschritts-/Nächstes-Puzzle-Logik.
/// <para>Die EF-Abfragen sind eigen (andere Tabellen/Joins), die RECHNUNG danach nicht: Serien,
/// Trefferquote, Bänder, Aktivität und Themen-Top-20 kommen aus <see cref="AttemptStats"/>.</para>
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
        // Spielweise: nur ausdrücklich als „easy" markierte Versuche zählen, alles andere (inkl.
        // Altbestand ohne Modus) ist „training" — gleiche Regel wie bei Wochenpost/Standard-Puzzles.
        var easyCount = await _db.CourseAttempts.CountAsync(a => a.UserId == userId && a.Mode == SolveMode.Easy);
        var (trainingCount, easy) = SolveMode.Split(totalAttempts, easyCount);

        var recentResults = await _db.CourseAttempts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AttemptedAt)
            .Take(1000)
            .Select(a => a.Solved)
            .ToListAsync();
        var (currentStreak, bestStreak) = AttemptStats.Streaks(recentResults);

        return new CourseStatsDto
        {
            TotalAttempts = totalAttempts,
            Solved = solved,
            Accuracy = AttemptStats.Accuracy(solved, totalAttempts),
            CurrentStreak = currentStreak,
            BestStreak = bestStreak,
            TrainingCount = trainingCount,
            EasyCount = easy,
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
                Mode = a.Mode,
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
        var themes = AttemptStats.TopThemes(themeAgg
            .Select(kv => new ThemeStatDto { Theme = kv.Key, Attempts = kv.Value.attempts, Solved = kv.Value.solved }));

        // Rating-Bänder (200er-Schritte) — server-seitig, nur Versuche mit gesetztem BookRating.
        var ratingBands = AttemptStats.RatingBands((await _db.CourseAttempts
            .Where(a => a.UserId == userId && a.BookPuzzle!.BookRating != null)
            .GroupBy(a => a.BookPuzzle!.BookRating!.Value / 200)
            .Select(g => new { Bucket = g.Key, Attempts = g.Count(), Solved = g.Count(x => x.Solved) })
            .ToListAsync())
            .Select(b => (b.Bucket, b.Attempts, b.Solved)));

        // Aktivität pro Tag (letzte 365 Tage) — server-seitig via GROUP BY CAST(AttemptedAt AS date).
        var since = AttemptStats.ActivityWindowStart(DateTime.UtcNow);
        var activity = AttemptStats.Activity((await _db.CourseAttempts
            .Where(a => a.UserId == userId && a.AttemptedAt >= since)
            .GroupBy(a => a.AttemptedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync())
            .Select(x => (x.Day, x.Count)));

        return new PuzzleBreakdownDto { Themes = themes, RatingBands = ratingBands, Activity = activity };
    }
}
