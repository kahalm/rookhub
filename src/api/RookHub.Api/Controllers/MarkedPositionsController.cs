using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// „+"-Markierungen besonders guter Stellungen (0.749.0): aus Partie-Analyse, Analysebrett und Fehler-Training. Nur die
/// eigenen; ein späteres Feature bietet sie allen zum Nachspielen an.
/// </summary>
[ApiController]
[Route("api/positions/marked")]
[Authorize]
public class MarkedPositionsController : BaseApiController
{
    private readonly MarkedPositionService _service;

    public MarkedPositionsController(MarkedPositionService service) => _service = service;

    /// <summary>Alle eigenen Markierungen, neueste zuerst.</summary>
    [HttpGet]
    public async Task<ActionResult<List<MarkedPositionDto>>> List(CancellationToken ct)
        => Ok(await _service.ListAsync(GetUserId(), ct));

    /// <summary>Stellung markieren (idempotent). 400 <c>{ reason }</c>: <c>invalidFen</c> oder <c>limit</c>.</summary>
    [HttpPost]
    public async Task<ActionResult<MarkedPositionDto>> Mark([FromBody] MarkPositionInputDto body, CancellationToken ct)
    {
        if (body is null) return BadRequest(new { reason = "invalidFen" });
        var (mark, error) = await _service.MarkAsync(GetUserId(), body, ct);
        return mark == null ? BadRequest(new { reason = error }) : Ok(mark);
    }

    /// <summary>Markierung zurücknehmen (<c>?fen=</c>, Zugzähler egal). 404, wenn die Stellung nicht markiert war.</summary>
    [HttpDelete]
    public async Task<IActionResult> Unmark([FromQuery] string fen, CancellationToken ct)
        => await _service.UnmarkAsync(GetUserId(), fen, ct) ? NoContent() : NotFound();
}
