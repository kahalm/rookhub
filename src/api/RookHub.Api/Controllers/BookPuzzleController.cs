using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/book-puzzles")]
[Authorize]   // secure by default; öffentliche Endpoints sind explizit mit [AllowAnonymous] markiert
public class BookPuzzleController : BaseApiController
{
    private readonly BookPuzzleService _service;

    public BookPuzzleController(BookPuzzleService service) => _service = service;

    [AllowAnonymous]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var dto = await _service.GetByIdAsync(id);
        return dto == null ? NotFound(new { message = "Book puzzle not found." }) : Ok(dto);
    }

    [AllowAnonymous]
    [HttpGet("{id}/next")]
    public async Task<IActionResult> GetNextInBook(int id)
    {
        try { return Ok(await _service.GetNextInBookAsync(id)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [AllowAnonymous]
    [HttpGet("{id}/random")]
    public async Task<IActionResult> GetRandomInBook(int id)
    {
        try { return Ok(await _service.GetRandomInBookAsync(id)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [Authorize]
    [HttpPost("{id}/attempt")]
    public async Task<IActionResult> RecordAttempt(int id, [FromBody] RecordBookAttemptDto dto)
    {
        try { await _service.RecordAttemptAsync(id, GetUserId(), dto); return Ok(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [AllowAnonymous]
    [HttpPost("{id}/attempt/anonymous")]
    public async Task<IActionResult> RecordAnonymousAttempt(int id, [FromBody] RecordAnonymousBookAttemptDto dto)
    {
        try { await _service.RecordAnonymousAttemptAsync(id, dto); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [AllowAnonymous]
    [HttpGet("{id}/results")]
    public async Task<ActionResult<BookPuzzleResultsDto>> GetResults(int id, [FromQuery] string? since = null)
        => Ok(await _service.GetResultsAsync(id, since));

    [AllowAnonymous]
    [HttpGet("random")]
    public async Task<IActionResult> GetRandom([FromQuery] string pool = "random", [FromQuery] string? exclude = null, [FromQuery] int? bookId = null)
    {
        try { return Ok(await _service.GetRandomAsync(pool, exclude, bookId)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>
    /// Tagespuzzle fuer ein bestimmtes UTC-Datum. <paramref name="date"/> als <c>yyyyMMdd</c>
    /// oder das Literal <c>today</c>. Zukuenftige Daten geben 400 zurueck; fuer heute und
    /// vergangene Tage wird ggf. on-demand eine Zuordnung angelegt und persistiert.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("daily/{date}")]
    public async Task<IActionResult> GetDaily(string date)
    {
        DateOnly parsed;
        if (string.Equals(date, "today", StringComparison.OrdinalIgnoreCase))
        {
            parsed = DateOnly.FromDateTime(DateTime.UtcNow);
        }
        else if (!DateOnly.TryParseExact(date, "yyyyMMdd", null,
                     System.Globalization.DateTimeStyles.None, out parsed))
        {
            return BadRequest(new { message = "date must be yyyyMMdd or 'today'." });
        }

        try { return Ok(await _service.GetOrAssignDailyAsync(parsed)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [AllowAnonymous]
    [HttpGet("by-line-id")]
    public async Task<IActionResult> GetByLineId([FromQuery] string lineId)
    {
        try { return Ok(new { id = await _service.GetIdByLineIdAsync(lineId) }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [AllowAnonymous]
    [HttpGet("books")]
    public async Task<IActionResult> GetBooks() => Ok(await _service.GetBooksAsync());

    [HttpPost("/api/admin/book-puzzles/import")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Import([FromBody] List<BookPuzzleImportDto> puzzles)
    {
        try
        {
            var (imported, skipped) = await _service.ImportAsync(puzzles);
            return Ok(new { imported, skipped });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>
    /// Admin: Tagespuzzle eines UTC-Datums neu generieren. Datum/Link bleiben gleich, nur das
    /// dahinterliegende Puzzle wechselt; das bisherige wird ausgemustert (nie wieder Daily/Random/Blind).
    /// <paramref name="date"/> als <c>yyyyMMdd</c> oder das Literal <c>today</c>.
    /// </summary>
    [HttpPost("/api/admin/book-puzzles/daily/{date}/regenerate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RegenerateDaily(string date)
    {
        DateOnly parsed;
        if (string.Equals(date, "today", StringComparison.OrdinalIgnoreCase))
        {
            parsed = DateOnly.FromDateTime(DateTime.UtcNow);
        }
        else if (!DateOnly.TryParseExact(date, "yyyyMMdd", null,
                     System.Globalization.DateTimeStyles.None, out parsed))
        {
            return BadRequest(new { message = "date must be yyyyMMdd or 'today'." });
        }

        try { return Ok(await _service.RegenerateDailyAsync(parsed)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}
