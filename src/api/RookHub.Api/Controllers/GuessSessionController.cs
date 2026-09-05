using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Punktepartie: eine analysierte Partie Zug für Zug erraten. Die Wertung passiert serverseitig
/// (<see cref="GuessScoring"/>) — der Client bekommt die Fortsetzung erst als Antwort auf seinen
/// Rateversuch, nie vorher.
/// </summary>
[ApiController]
[Route("api/guess-sessions")]
[Authorize]
public class GuessSessionController : BaseApiController
{
    private readonly GuessSessionService _service;

    public GuessSessionController(GuessSessionService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<List<GuessSessionDto>>> List(CancellationToken ct)
        => Ok(await _service.ListAsync(GetUserId(), ct));

    [HttpPost]
    public async Task<ActionResult<GuessSessionDto>> Start([FromBody] CreateGuessSessionRequest req, CancellationToken ct)
    {
        try { return Ok(await _service.StartAsync(GetUserId(), req, ct)); }
        catch (KeyNotFoundException) { return NotFound(new { message = "Analysis not found." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GuessSessionDto>> Get(int id, CancellationToken ct)
    {
        var dto = await _service.GetAsync(GetUserId(), id, ct);
        return dto is null ? NotFound(new { message = "Session not found." }) : Ok(dto);
    }

    /// <summary>Zug raten. Leeres <c>uci</c> = passen: 0 Punkte, aber keine Strafe.</summary>
    [HttpPost("{id:int}/guess")]
    public async Task<ActionResult<GuessResultDto>> Guess(int id, [FromBody] GuessMoveRequest req, CancellationToken ct)
    {
        try { return Ok(await _service.GuessAsync(GetUserId(), id, req, ct)); }
        catch (KeyNotFoundException) { return NotFound(new { message = "Session not found." }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("{id:int}/review")]
    public async Task<ActionResult<List<GuessReviewMoveDto>>> Review(int id, CancellationToken ct)
    {
        var rows = await _service.ReviewAsync(GetUserId(), id, ct);
        return rows is null ? NotFound(new { message = "Session not found." }) : Ok(rows);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => await _service.DeleteAsync(GetUserId(), id, ct)
            ? NoContent()
            : NotFound(new { message = "Session not found." });
}
