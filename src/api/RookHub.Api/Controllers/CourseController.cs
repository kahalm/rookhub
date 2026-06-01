using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Controllers;

/// <summary>
/// „Kurse" = importierte Bücher, die ein User puzzleweise durcharbeitet. Pro Buch gibt es einen
/// (geteilten) Fortschritt = gelöste Puzzles / Gesamtzahl; der Modus (sequential/random) bestimmt
/// nur die Reihenfolge. Fortschritt ist user-bezogen und liegt komplett in der DB.
/// Aktuell admin-only — die Sichtbarkeit lässt sich später auf Gruppen erweitern.
/// </summary>
[ApiController]
[Route("api/courses")]
[Authorize(Roles = "Admin")]
public class CourseController : BaseApiController
{
    private readonly AppDbContext _db;

    public CourseController(AppDbContext db) => _db = db;

    private static string NormalizeMode(string? mode) =>
        (mode ?? string.Empty).Trim().ToLowerInvariant() == "random" ? "random" : "sequential";

    private static int Percent(int solved, int total) =>
        total <= 0 ? 0 : (int)Math.Round(100.0 * Math.Min(solved, total) / total);

    /// <summary>Alle Bücher als Kurse inkl. Fortschritt des aktuellen Users.</summary>
    [HttpGet]
    public async Task<IActionResult> GetCourses()
    {
        var userId = GetUserId();

        var books = await _db.Books
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

        var result = books.Select(b =>
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

        return Ok(result);
    }

    /// <summary>
    /// Nächstes ungelöstes Puzzle des Kurses. sequential: Buchreihenfolge (Id), mit <paramref name="after"/>
    /// das nächste danach (für „Überspringen"). random: zufällig, <paramref name="exclude"/> vermeidet direkte
    /// Wiederholung. Aktualisiert den zuletzt genutzten Modus.
    /// </summary>
    [HttpGet("{bookId}/next")]
    public async Task<IActionResult> GetNext(
        int bookId,
        [FromQuery] string mode = "sequential",
        [FromQuery] int? after = null,
        [FromQuery] int? exclude = null)
    {
        var userId = GetUserId();
        if (!await _db.Books.AnyAsync(b => b.Id == bookId))
            return NotFound(new { message = "Book not found." });

        mode = NormalizeMode(mode);
        await UpsertProgressAsync(userId, bookId, mode);
        await _db.SaveChangesAsync();

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

        return Ok(new CourseNextPuzzleDto
        {
            Puzzle = puzzle == null ? null : BookPuzzleController.MapToDto(puzzle),
            SolvedCount = solvedCount,
            Total = total,
            Completed = puzzle == null,
        });
    }

    /// <summary>Zeichnet einen Lösungsversuch auf. Bei Solved wird das Puzzle (idempotent) als gelöst markiert.</summary>
    [HttpPost("{bookId}/results")]
    public async Task<IActionResult> RecordResult(int bookId, [FromBody] RecordCourseResultDto dto)
    {
        var userId = GetUserId();
        if (!await _db.Books.AnyAsync(b => b.Id == bookId))
            return NotFound(new { message = "Book not found." });

        var belongsToBook = await _db.BookPuzzles.AnyAsync(bp => bp.Id == dto.BookPuzzleId && bp.BookId == bookId);
        if (!belongsToBook)
            return NotFound(new { message = "Puzzle does not belong to this book." });

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
                    SolvedAt = DateTime.UtcNow,
                });
            }
        }

        await UpsertProgressAsync(userId, bookId, dto.Mode);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Race: paralleles Aufzeichnen desselben Puzzles -> Unique-Index (UserId, BookPuzzleId).
            // Idempotent behandeln: Fortschritt unten frisch aus der DB lesen.
        }

        return Ok(await BuildProgressAsync(userId, bookId));
    }

    /// <summary>Setzt den Fortschritt eines Kurses zurück (löscht alle gelösten Markierungen).</summary>
    [HttpPost("{bookId}/reset")]
    public async Task<IActionResult> Reset(int bookId)
    {
        var userId = GetUserId();
        if (!await _db.Books.AnyAsync(b => b.Id == bookId))
            return NotFound(new { message = "Book not found." });

        _db.CoursePuzzleResults.RemoveRange(
            _db.CoursePuzzleResults.Where(cr => cr.UserId == userId && cr.BookId == bookId));
        await _db.SaveChangesAsync();

        return Ok(await BuildProgressAsync(userId, bookId));
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
