using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : BaseApiController
{
    private readonly AdminService _admin;
    private readonly BookAdminService _bookAdmin;
    private readonly PuzzleService _puzzleService;
    private readonly PgnImportService _pgnImportService;
    private readonly IConfiguration _config;

    public AdminController(AdminService admin, BookAdminService bookAdmin, PuzzleService puzzleService, PgnImportService pgnImportService, IConfiguration config)
    {
        _admin = admin;
        _bookAdmin = bookAdmin;
        _puzzleService = puzzleService;
        _pgnImportService = pgnImportService;
        _config = config;
    }

    /// <summary>Konfigurationswerte fürs Admin-UI (z. B. Kibana-Link aus dem Server-Env).</summary>
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var kibanaUrl = (_config["Kibana:Url"] ?? string.Empty).TrimEnd('/');
        return Ok(new { kibanaUrl });
    }

    // ---- Benutzer ---------------------------------------------------------

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (items, totalCount, resolvedPage, resolvedPageSize) = await _admin.GetUsersAsync(search, page, pageSize);
        return Ok(new { items, totalCount, page = resolvedPage, pageSize = resolvedPageSize });
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        try
        {
            await _admin.DeleteUserAsync(id, GetUserId());
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // Verbleibende FK-Referenzen mit Restrict (statt Cascade) wuerden sonst als
            // unbehandelter 500 enden -> klare 409-Antwort.
            return Conflict(new { message = "Benutzer konnte nicht geloescht werden: es bestehen noch abhaengige Referenzen." });
        }
    }

    [HttpPost("users/{id}/toggle-admin")]
    public async Task<IActionResult> ToggleAdmin(int id)
    {
        try { return Ok(await _admin.ToggleAdminAsync(id, GetUserId())); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    // ---- Puzzles ----------------------------------------------------------

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
    public async Task<IActionResult> GetPuzzleCount() => Ok(new { count = await _admin.GetPuzzleCountAsync() });

    [HttpDelete("puzzles")]
    public async Task<IActionResult> ClearPuzzles()
    {
        await _admin.ClearPuzzlesAsync();
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
            result.TotalInvalid += item.Invalid;
        }
        return Ok(result);
    }

    [HttpGet("books")]
    public async Task<IActionResult> GetBooks() => Ok(await _bookAdmin.GetBooksAsync());

    /// <summary>Gruppen-Ids, die dieses Buch als Kurs sehen dürfen.</summary>
    [HttpGet("books/{id}/groups")]
    public async Task<IActionResult> GetBookGroups(int id)
    {
        try { return Ok(await _bookAdmin.GetBookGroupsAsync(id)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Setzt die vollständige Gruppen-Freigabe eines Buchs (ersetzt bestehende Einträge).</summary>
    [HttpPut("books/{id}/groups")]
    public async Task<IActionResult> SetBookGroups(int id, [FromBody] SetBookGroupsDto dto)
    {
        try { return Ok(await _bookAdmin.SetBookGroupsAsync(id, dto)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPut("books/{id}")]
    public async Task<IActionResult> UpdateBook(int id, [FromBody] UpdateBookDto dto)
    {
        try { return Ok(await _bookAdmin.UpdateBookAsync(id, dto)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpDelete("books/{id}")]
    public async Task<IActionResult> DeleteBook(int id)
    {
        try { await _bookAdmin.DeleteBookAsync(id); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}
