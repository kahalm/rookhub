using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Kalkulations-Modus über Buch-Stellungen: der Nutzer sieht nur die Stellung (FEN + optionaler
/// Kommentar) und legt am eingefrorenen Brett seinen eigenen Analysebaum an. Es gibt hier keine
/// Lösung — die gespeicherte Zugfolge einer Buchlinie wird von diesen Endpoints NICHT
/// ausgeliefert (siehe <see cref="CalculationService"/>). Zugriff je Buch wie im Kurs
/// (<see cref="CourseAccess"/>); kein Zugriff → 404.
/// </summary>
[ApiController]
[Route("api/calculations")]
[Authorize]
public class CalculationController : BaseApiController
{
    private readonly CalculationService _service;

    public CalculationController(CalculationService service) => _service = service;

    /// <summary>Kopf + Stellungsliste eines Buchs (leicht: ohne FEN/Kommentar/Züge), inkl.
    /// „schon bearbeitet"-Markierung je Stellung.</summary>
    [HttpGet("books/{bookId}")]
    public async Task<ActionResult<CalcBookDto>> GetBook(int bookId, CancellationToken ct)
    {
        try { return Ok(await _service.GetBookAsync(GetUserId(), bookId, IsAdmin, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Eine Stellung inkl. eigenem Analysebaum.</summary>
    [HttpGet("positions/{bookPuzzleId}")]
    public async Task<ActionResult<CalcPositionDto>> GetPosition(int bookPuzzleId, CancellationToken ct)
    {
        try { return Ok(await _service.GetPositionAsync(GetUserId(), bookPuzzleId, IsAdmin, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Eigenen Analysebaum zu einer Stellung speichern (Upsert).</summary>
    [HttpPut("positions/{bookPuzzleId}")]
    public async Task<ActionResult<CalcTreeSavedDto>> SaveTree(int bookPuzzleId, [FromBody] SaveCalcTreeDto dto,
        CancellationToken ct)
    {
        try { return Ok(await _service.SaveTreeAsync(GetUserId(), bookPuzzleId, dto, IsAdmin, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Eigenen Analysebaum zu einer Stellung verwerfen (idempotent).</summary>
    [HttpDelete("positions/{bookPuzzleId}")]
    public async Task<IActionResult> DeleteTree(int bookPuzzleId, CancellationToken ct)
    {
        try { await _service.DeleteTreeAsync(GetUserId(), bookPuzzleId, IsAdmin, ct); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}
