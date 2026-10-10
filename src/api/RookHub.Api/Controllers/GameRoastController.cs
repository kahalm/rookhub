using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>„Roast my game" (0.535.0) — nur der Besitzer der Partie. Eigene Klasse unter derselben Route wie
/// <see cref="GamesController"/>.</summary>
[ApiController]
[Route("api/games")]
[Authorize]
public class GameRoastController : BaseApiController
{
    private readonly GameRoastService _service;

    public GameRoastController(GameRoastService service) => _service = service;

    [HttpGet("{id:int}/roasts")]
    public async Task<ActionResult<GameRoastsDto>> Get(int id, [FromQuery] string? lang)
    {
        var dto = await _service.GetAsync(GetUserId(), id, lang);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>Den Roast zum öffentlichen Teilen freigeben (0.742.0) → <c>{ roastId }</c> für <c>/g/{token}?roast={roastId}</c>;
    /// 404 bei fremder Partie oder unbekanntem Roast.</summary>
    [HttpPost("{id:int}/roasts/share")]
    public async Task<IActionResult> Share(int id, [FromQuery] string? style, [FromQuery] string? lang, CancellationToken ct)
    {
        var roastId = await _service.SetSharedAsync(GetUserId(), id, style, lang, true, ct);
        return roastId is int r ? Ok(new { roastId = r }) : NotFound();
    }

    /// <summary>Freigabe zurücknehmen (idempotent) — der geteilte Link zeigt danach die Partie ohne Roast.</summary>
    [HttpDelete("{id:int}/roasts/share")]
    public async Task<IActionResult> Unshare(int id, [FromQuery] string? style, [FromQuery] string? lang, CancellationToken ct)
        => await _service.SetSharedAsync(GetUserId(), id, style, lang, false, ct) is null ? NotFound() : NoContent();

    /// <summary>Ein freigegebener Roast hinter dem Teilen-Link — ohne Anmeldung; 404, wenn er nicht (mehr) freigegeben ist.</summary>
    [HttpGet("shared/{token}/roasts/{roastId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<GameRoastDto>> GetShared(string token, int roastId, CancellationToken ct)
    {
        var dto = await _service.GetSharedAsync(token, roastId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>Würfeln (ersetzt den vorigen Text desselben Stils). Absagen mit <c>reason</c>: 503 notConfigured, 404 notFound, 409 noAnalysis, 400 invalidStyle, 429 dailyLimit,
    /// 502 failed.</summary>
    [HttpPost("{id:int}/roasts")]
    public async Task<ActionResult<GameRoastDto>> Roast(int id, [FromQuery] string? style, [FromQuery] string? lang,
        CancellationToken ct)
    {
        var result = await _service.RoastAsync(GetUserId(), id, style, lang, ct);
        if (result.Roast != null) return Ok(result.Roast);
        var body = new { reason = result.Reason };
        return result.Reason switch
        {
            "notConfigured" => StatusCode(StatusCodes.Status503ServiceUnavailable, body),
            "notFound" => NotFound(body),
            "noAnalysis" => Conflict(body),
            "invalidStyle" => BadRequest(body),
            "dailyLimit" => StatusCode(StatusCodes.Status429TooManyRequests, body),
            _ => StatusCode(StatusCodes.Status502BadGateway, body),
        };
    }
}
