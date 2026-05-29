using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/book-puzzles")]
public class BookPuzzleController : BaseApiController
{
    private readonly AppDbContext _db;

    public BookPuzzleController(AppDbContext db) => _db = db;

    [AllowAnonymous]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var puzzle = await _db.BookPuzzles.FindAsync(id);
        if (puzzle == null)
            return NotFound(new { message = "Book puzzle not found." });
        return Ok(MapToDto(puzzle));
    }

    [AllowAnonymous]
    [HttpGet("by-line-id")]
    public async Task<IActionResult> GetByLineId([FromQuery] string lineId)
    {
        if (string.IsNullOrWhiteSpace(lineId))
            return BadRequest(new { message = "lineId is required." });

        if (lineId.Length > 300)
            lineId = lineId[..300];

        var puzzle = await _db.BookPuzzles
            .Where(bp => bp.LineId == lineId)
            .Select(bp => new { bp.Id })
            .FirstOrDefaultAsync();

        if (puzzle == null)
            return NotFound(new { message = "Book puzzle not found for given lineId." });
        return Ok(new { id = puzzle.Id });
    }

    [AllowAnonymous]
    [HttpGet("books")]
    public async Task<IActionResult> GetBooks()
    {
        var books = await _db.BookPuzzles
            .GroupBy(bp => bp.BookFileName)
            .Select(g => new BookInfoDto
            {
                BookFileName = g.Key,
                Difficulty = g.First().Difficulty,
                BookRating = g.First().BookRating,
                Tags = g.First().Tags,
                PuzzleCount = g.Count()
            })
            .OrderBy(b => b.BookFileName)
            .ToListAsync();

        return Ok(books);
    }

    [HttpPost("/api/admin/book-puzzles/import")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Import([FromBody] List<BookPuzzleImportDto> puzzles)
    {
        if (puzzles == null || puzzles.Count == 0)
            return BadRequest(new { message = "No puzzles provided." });

        if (puzzles.Count > 10_000)
            return BadRequest(new { message = "Maximum 10000 puzzles per import." });

        var existingLineIds = await _db.BookPuzzles
            .Select(bp => bp.LineId)
            .ToHashSetAsync();

        var toAdd = new List<BookPuzzle>();
        var skipped = 0;

        foreach (var dto in puzzles)
        {
            if (existingLineIds.Contains(dto.LineId))
            {
                skipped++;
                continue;
            }

            toAdd.Add(new BookPuzzle
            {
                LineId = dto.LineId,
                BookFileName = dto.BookFileName,
                Round = dto.Round,
                Fen = dto.Fen,
                Moves = dto.Moves,
                Title = dto.Title,
                Chapter = dto.Chapter,
                Comment = dto.Comment,
                Difficulty = dto.Difficulty,
                BookRating = dto.BookRating,
                Tags = dto.Tags
            });
            existingLineIds.Add(dto.LineId);
        }

        if (toAdd.Count > 0)
        {
            _db.BookPuzzles.AddRange(toAdd);
            await _db.SaveChangesAsync();
        }

        return Ok(new { imported = toAdd.Count, skipped });
    }

    private static BookPuzzleDto MapToDto(BookPuzzle bp) => new()
    {
        Id = bp.Id,
        LineId = bp.LineId,
        BookFileName = bp.BookFileName,
        Round = bp.Round,
        Fen = bp.Fen,
        Moves = bp.Moves,
        Title = bp.Title,
        Chapter = bp.Chapter,
        Comment = bp.Comment,
        Difficulty = bp.Difficulty,
        BookRating = bp.BookRating,
        Tags = bp.Tags
    };
}
