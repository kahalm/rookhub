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
        var puzzle = await _db.BookPuzzles
            .Include(bp => bp.Book)
            .FirstOrDefaultAsync(bp => bp.Id == id);
        if (puzzle == null)
            return NotFound(new { message = "Book puzzle not found." });
        return Ok(MapToDto(puzzle));
    }

    /// <summary>
    /// Liefert ein zufälliges Buch-Puzzle aus dem gewünschten Pool.
    /// pool=random|blind → echtes Zufallspuzzle; pool=daily → deterministisch pro UTC-Tag
    /// (alle bekommen am selben Tag dasselbe Puzzle). exclude=id,id schließt IDs aus.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("random")]
    public async Task<IActionResult> GetRandom([FromQuery] string pool = "random", [FromQuery] string? exclude = null)
    {
        pool = (pool ?? "random").Trim().ToLowerInvariant();
        if (pool != "random" && pool != "daily" && pool != "blind")
            return BadRequest(new { message = "pool must be one of: random, daily, blind." });

        var query = _db.BookPuzzles.Include(bp => bp.Book).Where(bp => bp.Book != null);
        query = pool switch
        {
            "daily" => query.Where(bp => bp.Book!.ForDaily),
            "blind" => query.Where(bp => bp.Book!.ForBlind),
            _ => query.Where(bp => bp.Book!.ForRandom),
        };

        if (!string.IsNullOrWhiteSpace(exclude))
        {
            var excludeIds = exclude.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
                .Where(v => v.HasValue).Select(v => v!.Value).ToList();
            if (excludeIds.Count > 0)
                query = query.Where(bp => !excludeIds.Contains(bp.Id));
        }

        var count = await query.CountAsync();
        if (count == 0)
            return NotFound(new { message = $"No book puzzle available for pool '{pool}'." });

        int index;
        if (pool == "daily")
        {
            // Deterministisch: Tagesnummer (UTC) modulo Pool-Größe → gemeinsames Tagespuzzle.
            var dayNumber = (long)(DateTime.UtcNow.Date - DateTime.UnixEpoch).TotalDays;
            index = (int)(((dayNumber % count) + count) % count);
        }
        else
        {
            index = Random.Shared.Next(count);
        }

        // FirstOrDefault statt First: schrumpft der Pool zwischen CountAsync und hier
        // (paralleler Import/Delete), zeigt Skip(index) sonst ins Leere -> FirstAsync
        // wuerfe einen unbehandelten 500 statt eines sauberen 404.
        var puzzle = await query.OrderBy(bp => bp.Id).Skip(index).FirstOrDefaultAsync();
        if (puzzle == null)
            return NotFound(new { message = $"No book puzzle available for pool '{pool}'." });
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

        // Pro Dateiname ein Book sicherstellen (find-or-create) und BookId setzen, damit
        // auch via Legacy-JSON-Import angelegte Puzzles in den Pools (GetRandom) und in der
        // Admin-Bücher-Liste erscheinen.
        var now = DateTime.UtcNow;
        var bookIds = new Dictionary<string, int>();

        async Task<int> EnsureBookAsync(string fileName)
        {
            if (bookIds.TryGetValue(fileName, out var cached))
                return cached;
            var book = await _db.Books.FirstOrDefaultAsync(b => b.FileName == fileName);
            if (book == null)
            {
                book = new Book
                {
                    FileName = fileName,
                    DisplayName = Services.PgnImportService.CleanDisplayName(fileName),
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                _db.Books.Add(book);
                await _db.SaveChangesAsync();
            }
            bookIds[fileName] = book.Id;
            return book.Id;
        }

        var toAdd = new List<BookPuzzle>();
        var skipped = 0;

        foreach (var dto in puzzles)
        {
            if (existingLineIds.Contains(dto.LineId))
            {
                skipped++;
                continue;
            }

            var fileName = (dto.BookFileName ?? string.Empty).Trim();
            if (fileName.Length == 0) { skipped++; continue; }     // kein leerer BookFileName
            if (fileName.Length > 200) fileName = fileName[..200];  // FileName/BookFileName sind varchar(200)

            var bookId = await EnsureBookAsync(fileName);
            toAdd.Add(new BookPuzzle
            {
                LineId = dto.LineId,
                BookFileName = fileName,
                BookId = bookId,
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
        StartPly = bp.StartPly,
        Title = bp.Title,
        Chapter = bp.Chapter,
        Comment = bp.Comment,
        // Metadaten bevorzugt vom Buch (admin-gepflegt), sonst vom Puzzle.
        Difficulty = bp.Book?.Difficulty ?? bp.Difficulty,
        BookRating = bp.Book?.Rating ?? bp.BookRating,
        Tags = bp.Book?.Tags ?? bp.Tags
    };
}
