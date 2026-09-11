using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Punktepartie OHNE Anmeldung — derselbe Dienst, nur ein anderer Besitzer
/// (<see cref="GuessOwner.ForAnonymous"/>). Spielbar ist hier ausschliesslich der kuratierte
/// Bestand (<c>GameAnalysis.IsPublic</c>); an einer fremden privaten Analyse kommt niemand vorbei,
/// weil <see cref="GuessSessionService.StartAsync"/> anonym gar nicht nach einem Besitzer fragt.
///
/// <para><b>Warum ein eigener Controller</b> und nicht <c>[AllowAnonymous]</c> an den bestehenden
/// Routen: der Rate-Limiter und die Pruefung der Sitzungskennung gehoeren an EINE Stelle und
/// duerfen die angemeldeten Aufrufe nicht mitbremsen. Dasselbe Muster wie bei den anonymen
/// Endless-/Puzzle-Endpunkten.</para>
///
/// <para><b>Die eiserne Regel gilt unveraendert:</b> der Fortschritt liegt in der Sitzung am
/// SERVER, nicht im Browser. Ein Client, der selbst mitzaehlt, muesste sagen koennen, bei welchem
/// Halbzug er steht — und koennte damit jeden Zug der Partie einzeln abfragen. Der Partiezug kommt
/// deshalb auch hier erst als ANTWORT auf den Rateversuch.</para>
/// </summary>
[ApiController]
[Route("api/guess-sessions/anonymous")]
[AllowAnonymous]
[EnableRateLimiting("anonymous-puzzle")]
public class GuessSessionAnonymousController : ControllerBase
{
    private static readonly Regex SessionIdPattern = new(ValidationConstants.SessionIdPattern, RegexOptions.Compiled);

    private readonly GuessSessionService _service;

    public GuessSessionAnonymousController(GuessSessionService service) => _service = service;

    /// <summary>
    /// Kennung pruefen und in einen Besitzer verwandeln. Die Mindestlaenge ist keine Formsache:
    /// anonyme Durchlaeufe sind NUR ueber diese Kennung getrennt, ein kurzer oder erratbarer Wert
    /// waere der Weg in fremde Sitzungen (siehe <see cref="ValidationConstants.SessionIdPattern"/>).
    /// </summary>
    private static bool TryOwner(string? sessionId, out GuessOwner owner)
    {
        owner = default;
        if (string.IsNullOrWhiteSpace(sessionId) || !SessionIdPattern.IsMatch(sessionId)) return false;
        owner = GuessOwner.ForAnonymous(sessionId);
        return true;
    }

    private ActionResult BadSessionId() => BadRequest(new { message = "Invalid session ID." });

    [HttpGet]
    public async Task<ActionResult<List<GuessSessionDto>>> List([FromQuery] string? sessionId, CancellationToken ct)
        => TryOwner(sessionId, out var owner) ? Ok(await _service.ListAsync(owner, ct)) : BadSessionId();

    [HttpPost]
    public async Task<ActionResult<GuessSessionDto>> Start([FromBody] CreateAnonymousGuessSessionRequest req,
        CancellationToken ct, [FromQuery] string? lang = null)
    {
        if (!TryOwner(req.SessionId, out var owner)) return BadSessionId();
        try { return Ok(await _service.StartAsync(owner, req, ct, lang)); }
        catch (KeyNotFoundException) { return NotFound(new { message = "Analysis not found." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GuessSessionDto>> Get(int id, [FromQuery] string? sessionId,
        CancellationToken ct, [FromQuery] string? lang = null)
    {
        if (!TryOwner(sessionId, out var owner)) return BadSessionId();
        var dto = await _service.GetAsync(owner, id, ct, lang);
        return dto is null ? NotFound(new { message = "Session not found." }) : Ok(dto);
    }

    /// <summary>Zug raten. Leeres <c>uci</c> = passen: 0 Punkte, aber keine Strafe.</summary>
    [HttpPost("{id:int}/guess")]
    public async Task<ActionResult<GuessResultDto>> Guess(int id, [FromBody] AnonymousGuessMoveRequest req,
        CancellationToken ct, [FromQuery] string? lang = null)
    {
        if (!TryOwner(req.SessionId, out var owner)) return BadSessionId();
        try { return Ok(await _service.GuessAsync(owner, id, req, ct, lang)); }
        catch (KeyNotFoundException) { return NotFound(new { message = "Session not found." }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("{id:int}/review")]
    public async Task<ActionResult<List<GuessReviewMoveDto>>> Review(int id, [FromQuery] string? sessionId,
        CancellationToken ct)
    {
        if (!TryOwner(sessionId, out var owner)) return BadSessionId();
        var rows = await _service.ReviewAsync(owner, id, ct);
        return rows is null ? NotFound(new { message = "Session not found." }) : Ok(rows);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] string? sessionId, CancellationToken ct)
    {
        if (!TryOwner(sessionId, out var owner)) return BadSessionId();
        return await _service.DeleteAsync(owner, id, ct)
            ? NoContent()
            : NotFound(new { message = "Session not found." });
    }
}
