using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Kurse" = importierte Bücher, die ein User puzzleweise durcharbeitet (Fortschritt user-bezogen
/// in der DB; Modus sequential/random bestimmt nur die Reihenfolge). Geschäftslogik vormals inline
/// im CourseController. Sichtbarkeit/Zugriff wird je Buch erzwungen — kein Zugriff → 404 via
/// <see cref="KeyNotFoundException"/> (Controller bildet auf HTTP ab). `isAdmin` reicht der Controller
/// herein (HTTP-Concern).
/// </summary>
public class CourseService
{
    private readonly AppDbContext _db;
    private readonly ILogger<CourseService> _logger;

    public CourseService(AppDbContext db, ILogger<CourseService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private static string NormalizeMode(string? mode) =>
        (mode ?? string.Empty).Trim().ToLowerInvariant() == "random" ? "random" : "sequential";

    private static int Percent(int solved, int total) =>
        total <= 0 ? 0 : (int)Math.Round(100.0 * Math.Min(solved, total) / total);

    /// <summary>Darf der User dieses (existierende) Buch als Kurs sehen/bearbeiten?</summary>
    public async Task<bool> CanAccessAsync(int userId, int bookId, bool isAdmin)
    {
        if (!await _db.Books.AnyAsync(b => b.Id == bookId)) return false;
        if (isAdmin) return true;
        return await _db.BookGroupAccesses.AnyAsync(a => a.BookId == bookId &&
            _db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == a.GroupId));
    }

    private async Task EnsureAccessAsync(int userId, int bookId, bool isAdmin)
    {
        if (!await CanAccessAsync(userId, bookId, isAdmin))
            throw new KeyNotFoundException("Book not found.");
    }

    /// <summary>Alle Puzzles eines (zugänglichen) Buchs am Stück — für das Offline-Speichern.</summary>
    public async Task<List<BookPuzzleDto>> GetAllPuzzlesAsync(int userId, int bookId, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);
        var puzzles = await _db.BookPuzzles
            .Include(bp => bp.Book)
            .Where(bp => bp.BookId == bookId)
            .OrderBy(bp => bp.Id)
            .ToListAsync();
        return puzzles.Select(BookPuzzleService.MapToDto).ToList();
    }

    /// <summary>Sichtbare Bücher als Kurse inkl. Fortschritt des Users (Admin: alle).</summary>
    public async Task<List<CourseListItemDto>> GetCoursesAsync(int userId, bool isAdmin)
    {
        IQueryable<Book> booksQuery = _db.Books;
        if (!isAdmin)
        {
            booksQuery = booksQuery.Where(b => _db.BookGroupAccesses.Any(a => a.BookId == b.Id &&
                _db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == a.GroupId)));
        }

        var books = await booksQuery
            .OrderBy(b => b.DisplayName)
            .Select(b => new
            {
                b.Id, b.FileName, b.DisplayName, b.Difficulty, b.Rating, b.Tags, b.Description,
                PuzzleCount = b.Puzzles.Count()
            })
            .ToListAsync();

        var solvedByBook = await _db.CoursePuzzleResults
            .Where(cr => cr.UserId == userId)
            .GroupBy(cr => cr.BookId)
            .Select(g => new { BookId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BookId, x => x.Count);

        var modeByBook = await _db.CourseProgresses
            .Where(cp => cp.UserId == userId)
            .ToDictionaryAsync(cp => cp.BookId, cp => cp.LastMode);

        return books.Select(b =>
        {
            var solved = solvedByBook.TryGetValue(b.Id, out var c) ? c : 0;
            return new CourseListItemDto
            {
                BookId = b.Id,
                FileName = b.FileName,
                DisplayName = b.DisplayName,
                Difficulty = b.Difficulty,
                Rating = b.Rating,
                Tags = b.Tags,
                Description = b.Description,
                PuzzleCount = b.PuzzleCount,
                SolvedCount = Math.Min(solved, b.PuzzleCount),
                ProgressPercent = Percent(solved, b.PuzzleCount),
                LastMode = modeByBook.TryGetValue(b.Id, out var m) ? m : null,
            };
        }).ToList();
    }

    /// <summary>Hat der User Zugriff auf mindestens einen Kurs? (Basis für die Menü-Sichtbarkeit.)</summary>
    public async Task<bool> HasAnyAccessAsync(int userId, bool isAdmin)
    {
        if (isAdmin)
            return await _db.Books.AnyAsync();
        return await _db.BookGroupAccesses.AnyAsync(a =>
            _db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == a.GroupId));
    }

    /// <summary>
    /// Nächstes ungelöstes Puzzle des Kurses. sequential: Buchreihenfolge (Id), mit <paramref name="after"/>
    /// das nächste danach; random: zufällig, <paramref name="exclude"/> vermeidet direkte Wiederholung.
    /// Aktualisiert den zuletzt genutzten Modus.
    /// </summary>
    public async Task<CourseNextPuzzleDto> GetNextAsync(int userId, int bookId, string mode, int? after, int? exclude, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);

        mode = NormalizeMode(mode);
        await UpsertProgressAsync(userId, bookId, mode);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Race: zwei (fast) gleichzeitige .../next-Aufrufe legen den CourseProgress parallel an
            // (Unique (UserId, BookId)). Der LastMode-Upsert ist nur Nebeneffekt → Konflikt verwerfen.
            _db.ChangeTracker.Clear();
        }

        var total = await _db.BookPuzzles.CountAsync(bp => bp.BookId == bookId);
        var solvedCount = await _db.CoursePuzzleResults.CountAsync(cr => cr.UserId == userId && cr.BookId == bookId);

        // Ungelöste Puzzles des Buchs (NOT EXISTS statt großer IN-Liste).
        IQueryable<BookPuzzle> unsolved = _db.BookPuzzles
            .Include(bp => bp.Book)
            .Where(bp => bp.BookId == bookId &&
                !_db.CoursePuzzleResults.Any(cr => cr.UserId == userId && cr.BookPuzzleId == bp.Id));

        BookPuzzle? puzzle;
        if (mode == "random")
        {
            var pool = exclude.HasValue ? unsolved.Where(bp => bp.Id != exclude.Value) : unsolved;
            var count = await pool.CountAsync();
            if (count == 0 && exclude.HasValue)
            {
                // Nur noch das ausgeschlossene Puzzle übrig — dann doch dieses zeigen.
                pool = unsolved;
                count = await pool.CountAsync();
            }
            puzzle = count == 0
                ? null
                : await pool.OrderBy(bp => bp.Id).Skip(Random.Shared.Next(count)).FirstOrDefaultAsync();
        }
        else
        {
            puzzle = null;
            if (after.HasValue)
                puzzle = await unsolved.Where(bp => bp.Id > after.Value).OrderBy(bp => bp.Id).FirstOrDefaultAsync();
            puzzle ??= await unsolved.OrderBy(bp => bp.Id).FirstOrDefaultAsync();
        }

        return new CourseNextPuzzleDto
        {
            Puzzle = puzzle == null ? null : BookPuzzleService.MapToDto(puzzle),
            SolvedCount = solvedCount,
            Total = total,
            Completed = puzzle == null,
        };
    }

    /// <summary>Zeichnet einen Lösungsversuch auf. Bei Solved wird das Puzzle (idempotent) als gelöst markiert.</summary>
    public async Task<CourseProgressDto> RecordResultAsync(int userId, int bookId, RecordCourseResultDto dto, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);

        var belongsToBook = await _db.BookPuzzles.AnyAsync(bp => bp.Id == dto.BookPuzzleId && bp.BookId == bookId);
        if (!belongsToBook)
            throw new KeyNotFoundException("Puzzle does not belong to this book.");

        var solvedAt = DateTime.UtcNow;
        var timeSeconds = Math.Clamp(dto.TimeSeconds, 0, 86400);
        var startedAt = solvedAt.AddSeconds(-timeSeconds);
        _logger.LogInformation(
            "CoursePuzzleAttempt: User {UserId} {Result} course-puzzle {PuzzleId} in book {BookId} StartedAt={StartedAt:o} SolvedAt={SolvedAt:o} in {TimeSeconds}s",
            userId, dto.Solved ? "solved" : "failed", dto.BookPuzzleId, bookId, startedAt, solvedAt, timeSeconds);

        // Solve in EIGENEM SaveChanges aufzeichnen — damit ein späterer CourseProgress-Konflikt
        // (paralleler Erstinsert) die gültige Lösung NICHT mit zurückrollt (sonst stiller Solve-Verlust).
        if (dto.Solved)
        {
            var already = await _db.CoursePuzzleResults
                .AnyAsync(cr => cr.UserId == userId && cr.BookPuzzleId == dto.BookPuzzleId);
            if (!already)
            {
                _db.CoursePuzzleResults.Add(new CoursePuzzleResult
                {
                    UserId = userId,
                    BookId = bookId,
                    BookPuzzleId = dto.BookPuzzleId,
                    SolvedAt = solvedAt,
                });
                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Race: paralleles Aufzeichnen desselben Puzzles → Unique (UserId, BookPuzzleId). Idempotent.
                    _db.ChangeTracker.Clear();
                }
            }
        }

        // Fortschritt/LastMode getrennt upserten; ein paralleler Erstinsert (Unique (UserId, BookId)) ist hier unkritisch.
        await UpsertProgressAsync(userId, bookId, dto.Mode);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
        }

        return await BuildProgressAsync(userId, bookId);
    }

    /// <summary>Setzt den Fortschritt eines Kurses zurück (löscht alle gelösten Markierungen).</summary>
    public async Task<CourseProgressDto> ResetAsync(int userId, int bookId, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);

        _db.CoursePuzzleResults.RemoveRange(
            _db.CoursePuzzleResults.Where(cr => cr.UserId == userId && cr.BookId == bookId));
        await _db.SaveChangesAsync();

        return await BuildProgressAsync(userId, bookId);
    }

    private async Task UpsertProgressAsync(int userId, int bookId, string? mode)
    {
        var progress = await _db.CourseProgresses
            .FirstOrDefaultAsync(cp => cp.UserId == userId && cp.BookId == bookId);
        var now = DateTime.UtcNow;
        if (progress == null)
        {
            _db.CourseProgresses.Add(new CourseProgress
            {
                UserId = userId,
                BookId = bookId,
                LastMode = mode == null ? null : NormalizeMode(mode),
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            if (mode != null) progress.LastMode = NormalizeMode(mode);
            progress.UpdatedAt = now;
        }
    }

    private async Task<CourseProgressDto> BuildProgressAsync(int userId, int bookId)
    {
        var total = await _db.BookPuzzles.CountAsync(bp => bp.BookId == bookId);
        var solved = await _db.CoursePuzzleResults.CountAsync(cr => cr.UserId == userId && cr.BookId == bookId);
        var lastMode = await _db.CourseProgresses
            .Where(cp => cp.UserId == userId && cp.BookId == bookId)
            .Select(cp => cp.LastMode)
            .FirstOrDefaultAsync();

        return new CourseProgressDto
        {
            BookId = bookId,
            SolvedCount = Math.Min(solved, total),
            Total = total,
            ProgressPercent = Percent(solved, total),
            Completed = total > 0 && solved >= total,
            LastMode = lastMode,
        };
    }
}
