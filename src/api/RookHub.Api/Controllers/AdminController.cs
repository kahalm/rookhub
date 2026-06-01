using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly PuzzleService _puzzleService;
    private readonly PgnImportService _pgnImportService;

    public AdminController(AppDbContext db, PuzzleService puzzleService, PgnImportService pgnImportService)
    {
        _db = db;
        _puzzleService = puzzleService;
        _pgnImportService = pgnImportService;
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var query = _db.AppUsers.AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            if (search.Length > 100) search = search[..100];
            query = query.Where(u => u.Username.Contains(search) || u.Email.Contains(search));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                IsAdmin = u.IsAdmin,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync();

        return Ok(new { items, totalCount, page, pageSize });
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var currentUserId = GetUserId();
        if (id == currentUserId)
            return BadRequest(new { message = "Cannot delete yourself." });

        var user = await _db.AppUsers.FindAsync(id);
        if (user == null)
            return NotFound();

        // Remove friendships first (Restrict delete behavior)
        var friendships = await _db.Friendships
            .Where(f => f.RequesterId == id || f.AddresseeId == id)
            .ToListAsync();
        _db.Friendships.RemoveRange(friendships);

        _db.AppUsers.Remove(user);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Verbleibende FK-Referenzen mit Restrict (statt Cascade) wuerden sonst als
            // unbehandelter 500 enden -> klare 409-Antwort.
            return Conflict(new { message = "Benutzer konnte nicht geloescht werden: es bestehen noch abhaengige Referenzen." });
        }

        return NoContent();
    }

    [HttpPost("users/{id}/toggle-admin")]
    public async Task<IActionResult> ToggleAdmin(int id)
    {
        var currentUserId = GetUserId();
        if (id == currentUserId)
            return BadRequest(new { message = "Cannot toggle your own admin status." });

        var user = await _db.AppUsers.FindAsync(id);
        if (user == null)
            return NotFound();

        user.IsAdmin = !user.IsAdmin;
        await _db.SaveChangesAsync();

        return Ok(new AdminUserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            IsAdmin = user.IsAdmin,
            CreatedAt = user.CreatedAt
        });
    }

    [HttpPost("puzzles/import")]
    [RequestSizeLimit(500 * 1024 * 1024)]
    public async Task<IActionResult> ImportPuzzles(
        IFormFile file,
        [FromQuery] int? minRating,
        [FromQuery] int? maxRating,
        [FromQuery] int? maxCount,
        CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });

        if (file.Length > 500 * 1024 * 1024)
            return BadRequest(new { message = "File exceeds 500 MB limit." });

        using var stream = file.OpenReadStream();
        var imported = await _puzzleService.ImportFromCsvAsync(stream, minRating, maxRating, maxCount, ct);
        return Ok(new { imported });
    }

    [HttpGet("puzzles/count")]
    public async Task<IActionResult> GetPuzzleCount()
    {
        var count = await _db.Puzzles.CountAsync();
        return Ok(new { count });
    }

    [HttpDelete("puzzles")]
    public async Task<IActionResult> ClearPuzzles()
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        await _db.PuzzleAttempts.ExecuteDeleteAsync();
        await _db.Puzzles.ExecuteDeleteAsync();
        await tx.CommitAsync();
        return NoContent();
    }

    // ---- Buch-Puzzles: Upload + Verwaltung -------------------------------

    /// <summary>Lädt eine oder mehrere PGN-Dateien als Bücher hoch (serverseitiges Parsing).</summary>
    [HttpPost("books/import")]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<IActionResult> ImportBooks([FromForm] List<IFormFile> files, CancellationToken ct)
    {
        if (files == null || files.Count == 0)
            return BadRequest(new { message = "No files provided." });

        var result = new BookImportResultDto();
        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            string text;
            using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
                text = await reader.ReadToEndAsync(ct);

            var item = await _pgnImportService.ImportFileAsync(file.FileName, text, ct);
            result.Books.Add(item);
            result.TotalImported += item.Imported;
            result.TotalSkipped += item.Skipped;
        }
        return Ok(result);
    }

    [HttpGet("books")]
    public async Task<IActionResult> GetBooks()
    {
        var books = await _db.Books
            .OrderBy(b => b.DisplayName)
            .Select(b => new BookDto
            {
                Id = b.Id,
                FileName = b.FileName,
                DisplayName = b.DisplayName,
                Difficulty = b.Difficulty,
                Rating = b.Rating,
                MinElo = b.MinElo,
                MaxElo = b.MaxElo,
                Tags = b.Tags,
                Description = b.Description,
                ForDaily = b.ForDaily,
                ForRandom = b.ForRandom,
                ForBlind = b.ForBlind,
                PuzzleCount = b.Puzzles.Count(),
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt,
            })
            .ToListAsync();
        return Ok(books);
    }

    [HttpPut("books/{id}")]
    public async Task<IActionResult> UpdateBook(int id, [FromBody] UpdateBookDto dto)
    {
        var book = await _db.Books.FindAsync(id);
        if (book == null)
            return NotFound(new { message = "Book not found." });

        if (dto.DisplayName != null) book.DisplayName = dto.DisplayName;
        if (dto.Difficulty != null) book.Difficulty = dto.Difficulty;
        if (dto.Rating.HasValue) book.Rating = dto.Rating;
        if (dto.Tags != null) book.Tags = dto.Tags;
        if (dto.Description != null) book.Description = dto.Description;
        if (dto.ForDaily.HasValue) book.ForDaily = dto.ForDaily.Value;
        if (dto.ForRandom.HasValue) book.ForRandom = dto.ForRandom.Value;
        if (dto.ForBlind.HasValue) book.ForBlind = dto.ForBlind.Value;
        book.MinElo = dto.MinElo;
        book.MaxElo = dto.MaxElo;
        book.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var count = await _db.BookPuzzles.CountAsync(bp => bp.BookId == id);
        return Ok(new BookDto
        {
            Id = book.Id,
            FileName = book.FileName,
            DisplayName = book.DisplayName,
            Difficulty = book.Difficulty,
            Rating = book.Rating,
            MinElo = book.MinElo,
            MaxElo = book.MaxElo,
            Tags = book.Tags,
            Description = book.Description,
            ForDaily = book.ForDaily,
            ForRandom = book.ForRandom,
            ForBlind = book.ForBlind,
            PuzzleCount = count,
            CreatedAt = book.CreatedAt,
            UpdatedAt = book.UpdatedAt,
        });
    }

    [HttpDelete("books/{id}")]
    public async Task<IActionResult> DeleteBook(int id)
    {
        var book = await _db.Books.FindAsync(id);
        if (book == null)
            return NotFound(new { message = "Book not found." });

        // Zugehörige Puzzles explizit entfernen (FK-Cascade greift bei InMemory nicht).
        var puzzles = _db.BookPuzzles.Where(bp => bp.BookId == id);
        _db.BookPuzzles.RemoveRange(puzzles);
        _db.Books.Remove(book);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
