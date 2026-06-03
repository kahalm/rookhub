using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/book-puzzles")]
[Authorize]   // secure by default; öffentliche Endpoints sind explizit mit [AllowAnonymous] markiert
public class BookPuzzleController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly ILogger<BookPuzzleController> _logger;

    public BookPuzzleController(AppDbContext db, ILogger<BookPuzzleController> logger)
    {
        _db = db;
        _logger = logger;
    }

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

    /// <summary>Nächstes Puzzle im selben Buch (Id-Reihenfolge = Buchreihenfolge); am Ende wieder das erste.</summary>
    [AllowAnonymous]
    [HttpGet("{id}/next")]
    public async Task<IActionResult> GetNextInBook(int id)
    {
        var current = await _db.BookPuzzles.FirstOrDefaultAsync(bp => bp.Id == id);
        if (current == null)
            return NotFound(new { message = "Book puzzle not found." });

        var siblings = BookSiblings(current).Include(bp => bp.Book);
        var next = await siblings.Where(bp => bp.Id > current.Id).OrderBy(bp => bp.Id).FirstOrDefaultAsync()
                   ?? await siblings.OrderBy(bp => bp.Id).FirstOrDefaultAsync();   // am Ende → erstes (Loop)
        return next == null ? NotFound(new { message = "No puzzles in book." }) : Ok(MapToDto(next));
    }

    /// <summary>Zufälliges Puzzle aus demselben Buch (möglichst nicht das aktuelle).</summary>
    [AllowAnonymous]
    [HttpGet("{id}/random")]
    public async Task<IActionResult> GetRandomInBook(int id)
    {
        var current = await _db.BookPuzzles.FirstOrDefaultAsync(bp => bp.Id == id);
        if (current == null)
            return NotFound(new { message = "Book puzzle not found." });

        var others = BookSiblings(current).Where(bp => bp.Id != current.Id);
        var count = await others.CountAsync();
        if (count == 0)
            return Ok(MapToDto(await BookSiblings(current).Include(bp => bp.Book).FirstAsync(bp => bp.Id == current.Id)));
        var pick = await others.Include(bp => bp.Book).OrderBy(bp => bp.Id).Skip(Random.Shared.Next(count)).FirstAsync();
        return Ok(MapToDto(pick));
    }

    /// <summary>Puzzles desselben Buchs (per BookId; Fallback BookFileName für Altbestand ohne BookId).</summary>
    private IQueryable<BookPuzzle> BookSiblings(BookPuzzle current) =>
        current.BookId != null
            ? _db.BookPuzzles.Where(bp => bp.BookId == current.BookId)
            : _db.BookPuzzles.Where(bp => bp.BookFileName == current.BookFileName);

    /// <summary>Zeichnet einen Lösungsversuch des eingeloggten Users an einem Buch-Puzzle auf
    /// (für die Tagespuzzle-Visualisierung auf Discord).</summary>
    [Authorize]
    [HttpPost("{id}/attempt")]
    public async Task<IActionResult> RecordAttempt(int id, [FromBody] RecordBookAttemptDto dto)
    {
        if (!await _db.BookPuzzles.AnyAsync(bp => bp.Id == id))
            return NotFound(new { message = "Book puzzle not found." });

        var userId = GetUserId();
        var solvedAt = DateTime.UtcNow;
        var timeSeconds = Math.Clamp(dto.TimeSeconds, 0, 86400);
        var startedAt = solvedAt.AddSeconds(-timeSeconds);

        _db.BookPuzzleAttempts.Add(new BookPuzzleAttempt
        {
            BookPuzzleId = id,
            UserId = userId,
            Solved = dto.Solved,
            TimeSeconds = timeSeconds,
            AttemptedAt = solvedAt
        });
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "BookPuzzleAttempt: User {UserId} {Result} book-puzzle {PuzzleId} StartedAt={StartedAt:o} SolvedAt={SolvedAt:o} in {TimeSeconds}s",
            userId, dto.Solved ? "solved" : "failed", id, startedAt, solvedAt, timeSeconds);
        return Ok();
    }

    private static readonly System.Text.RegularExpressions.Regex _SessionId =
        new(@"^[a-fA-F0-9\-]{1,36}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Anonymer (nicht eingeloggter) Lösungsversuch — zählt fürs Tagespuzzle mit,
    /// erscheint aber namenlos. Nur Solves werden erfasst, je (Puzzle, Session) genau einmal
    /// (gegen Spam + saubere Zählung).</summary>
    [AllowAnonymous]
    [HttpPost("{id}/attempt/anonymous")]
    public async Task<IActionResult> RecordAnonymousAttempt(int id, [FromBody] RecordAnonymousBookAttemptDto dto)
    {
        if (!_SessionId.IsMatch(dto.SessionId ?? ""))
            return BadRequest(new { message = "Invalid sessionId." });
        if (!await _db.BookPuzzles.AnyAsync(bp => bp.Id == id))
            return NotFound(new { message = "Book puzzle not found." });

        if (dto.Solved)
        {
            var exists = await _db.BookPuzzleAttempts.AnyAsync(
                a => a.BookPuzzleId == id && a.AnonymousSessionId == dto.SessionId && a.Solved);
            if (!exists)
            {
                var solvedAt = DateTime.UtcNow;
                var timeSeconds = Math.Clamp(dto.TimeSeconds, 0, 86400);
                _db.BookPuzzleAttempts.Add(new BookPuzzleAttempt
                {
                    BookPuzzleId = id,
                    AnonymousSessionId = dto.SessionId,
                    Solved = true,
                    TimeSeconds = timeSeconds,
                    AttemptedAt = solvedAt,
                });
                await _db.SaveChangesAsync();
                _logger.LogInformation(
                    "BookPuzzleAttempt: Anonymous solved book-puzzle {PuzzleId} StartedAt={StartedAt:o} SolvedAt={SolvedAt:o} in {TimeSeconds}s",
                    id, solvedAt.AddSeconds(-timeSeconds), solvedAt, timeSeconds);
            }
        }
        return Ok();
    }

    /// <summary>
    /// Aggregierte Ergebnisse zu einem Buch-Puzzle (für die Tagespuzzle-Anzeige): wer hat gelöst
    /// (je User dedupliziert, mit Discord-Verknüpfung sofern vorhanden) + Versuchs-/Lösungszähler.
    /// `since` (ISO-UTC) grenzt optional auf einen Zeitraum ein (z. B. seit dem Tagespuzzle-Post).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{id}/results")]
    public async Task<ActionResult<BookPuzzleResultsDto>> GetResults(int id, [FromQuery] string? since = null)
    {
        var q = _db.BookPuzzleAttempts.Where(a => a.BookPuzzleId == id);
        if (DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var sinceUtc))
            q = q.Where(a => a.AttemptedAt >= sinceUtc);

        // Eingeloggte: je User aggregieren (nur skalare Aggregate → EF-übersetzbar).
        var perUser = await q.Where(a => a.UserId != null)
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key!.Value, SolvedCount = g.Count(a => a.Solved) })
            .ToListAsync();

        // Anonyme: nur gelöste werden anonym erfasst → distinct Sessions = anonyme Löser.
        var anonymousSolvedCount = await q.Where(a => a.AnonymousSessionId != null && a.Solved)
            .Select(a => a.AnonymousSessionId).Distinct().CountAsync();
        var anonymousAttempts = await q.Where(a => a.AnonymousSessionId != null)
            .Select(a => a.AnonymousSessionId).Distinct().CountAsync();

        var userIds = perUser.Select(u => u.UserId).ToList();
        var names = await _db.AppUsers.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username);
        var profiles = await _db.UserProfiles.Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId);

        var solvers = perUser
            .Where(u => u.SolvedCount > 0)
            .Select(u =>
            {
                profiles.TryGetValue(u.UserId, out var prof);
                names.TryGetValue(u.UserId, out var uname);
                return new BookSolverDto
                {
                    Name = prof?.DisplayName ?? uname ?? $"#{u.UserId}",
                    DiscordId = prof?.DiscordId,
                    DiscordUsername = prof?.DiscordUsername
                };
            })
            .OrderBy(s => s.Name)
            .ToList();

        return Ok(new BookPuzzleResultsDto
        {
            SolvedCount = solvers.Count,
            AnonymousSolvedCount = anonymousSolvedCount,
            AttemptCount = perUser.Count + anonymousAttempts,
            Solvers = solvers
        });
    }

    /// <summary>
    /// Liefert ein zufälliges Buch-Puzzle aus dem gewünschten Pool.
    /// pool=random|blind → echtes Zufallspuzzle; pool=daily → deterministisch pro UTC-Tag
    /// (alle bekommen am selben Tag dasselbe Puzzle). exclude=id,id schließt IDs aus.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("random")]
    public async Task<IActionResult> GetRandom([FromQuery] string pool = "random", [FromQuery] string? exclude = null, [FromQuery] int? bookId = null)
    {
        pool = (pool ?? "random").Trim().ToLowerInvariant();
        if (pool != "random" && pool != "daily" && pool != "blind")
            return BadRequest(new { message = "pool must be one of: random, daily, blind." });

        var query = _db.BookPuzzles.Include(bp => bp.Book).Where(bp => bp.Book != null);
        if (bookId.HasValue)
            // Explizite Buchwahl überschreibt den Pool-Filter: irgendein Puzzle aus diesem Buch.
            query = query.Where(bp => bp.BookId == bookId.Value);
        else
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
                BookId = g.Max(bp => bp.BookId),
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

    internal static BookPuzzleDto MapToDto(BookPuzzle bp) => new()
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
