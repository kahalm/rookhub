using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;
using RookHub.Api.Services.Games;
using RookHub.Api.DTOs.Games;

namespace RookHub.Api.Controllers.Games;

/// <summary>
/// „+"-Markierungen besonders guter Stellungen (0.749.0): aus Partie-Analyse, Analysebrett und Fehler-Training. Nur die
/// eigenen; ein späteres Feature bietet sie allen zum Nachspielen an. Seit 0.847.0 auch ohne Anmeldung — dann zählt die
/// anonyme Kennung aus <c>X-Visitor-Id</c>, und <c>POST claim-session</c> holt die Markierungen beim Anmelden ins Konto.
/// </summary>
[ApiController]
[Route("api/positions/marked")]
[Authorize]
public class MarkedPositionsController : BaseApiController
{
    private readonly MarkedPositionService _service;

    public MarkedPositionsController(MarkedPositionService service) => _service = service;

    /// <summary>Angemeldet = Konto, sonst die anonyme Kennung aus <c>X-Visitor-Id</c> (ungültige Form = keine).</summary>
    private MarkCaller Caller => User?.Identity?.IsAuthenticated == true
        ? GetUserId()
        : MarkCaller.Anonymous(Request?.Headers["X-Visitor-Id"].FirstOrDefault());

    /// <summary>Alle eigenen Markierungen, neueste zuerst.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<MarkedPositionDto>>> List(CancellationToken ct)
        => Ok(await _service.ListAsync(Caller, ct));

    /// <summary>Stellung markieren (idempotent). 400 <c>{ reason }</c>: <c>invalidFen</c>, <c>limit</c> oder
    /// <c>noVisitor</c> (ohne Anmeldung und ohne gültige <c>X-Visitor-Id</c>).</summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<MarkedPositionDto>> Mark([FromBody] MarkPositionInputDto body, CancellationToken ct)
    {
        if (body is null) return BadRequest(new { reason = "invalidFen" });
        var (mark, error) = await _service.MarkAsync(Caller, body, ct);
        return mark == null ? BadRequest(new { reason = error }) : Ok(mark);
    }

    /// <summary>Markierung zurücknehmen (<c>?fen=</c>, Zugzähler egal). 404, wenn die Stellung nicht markiert war.</summary>
    [HttpDelete]
    [AllowAnonymous]
    public async Task<IActionResult> Unmark([FromQuery] string fen, CancellationToken ct)
        => await _service.UnmarkAsync(Caller, fen, ct) ? NoContent() : NotFound();

    /// <summary>Schlagworte und Kommentar einer markierten Stellung setzen (0.862.0). 404 = nicht markiert,
    /// 400 <c>{ reason: "invalid" }</c> bei zu vielen/zu langen Schlagworten oder zu langem Kommentar.</summary>
    [HttpPut("notes")]
    [AllowAnonymous]
    public async Task<ActionResult<MarkedPositionDto>> SetNotes([FromBody] MarkedPositionNotesDto body, CancellationToken ct)
    {
        if (body is null) return NotFound();
        var (mark, error) = await _service.SetNotesAsync(Caller, body, ct);
        return mark != null ? Ok(mark) : error == "invalid" ? BadRequest(new { reason = error }) : NotFound();
    }

    /// <summary>Die Markierungen dieser anonymen Kennung ins Konto übernehmen (beim Anmelden) → <c>{ claimed }</c>.</summary>
    [HttpPost("claim-session")]
    public async Task<IActionResult> ClaimSession([FromBody] ClaimMarkedSessionDto dto, CancellationToken ct)
        => Ok(new { claimed = await _service.ClaimAnonymousAsync(GetUserId(), dto?.SessionId, ct) });
}
