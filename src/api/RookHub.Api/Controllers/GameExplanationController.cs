using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// „Warum war das ein Fehler?" (0.534.0) — Erklärungen zu den Fehlern einer gespeicherten Partie. Lesen darf der
/// Besitzer und (über den Teilen-Link) jeder; ERZEUGEN nur der Besitzer. Eigene Klasse unter derselben Route wie
/// <see cref="GamesController"/>, damit dessen Abhängigkeiten nicht wachsen.
/// </summary>
[ApiController]
[Route("api/games")]
[Authorize]
public class GameExplanationController : BaseApiController
{
    private readonly GameMoveExplanationService _service;

    public GameExplanationController(GameMoveExplanationService service) => _service = service;

    [HttpGet("{id:int}/explanations")]
    public async Task<ActionResult<GameExplanationsDto>> Get(int id, [FromQuery] string? lang)
        => Ok(await _service.GetAsync(await _service.AnalysisOfOwnGameAsync(GetUserId(), id), lang ?? "en", owner: true));

    /// <summary>Erzeugen anstoßen (Hintergrund). 404 ohne eigene Partie, 409 ohne verknüpfte fertige Analyse,
    /// 503 ohne Modell auf eigener Hardware.</summary>
    [HttpPost("{id:int}/explanations")]
    public async Task<ActionResult<GameExplanationsDto>> Generate(int id, [FromQuery] string? lang)
    {
        if (!_service.Available)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { reason = "notConfigured" });
        var analysisId = await _service.AnalysisOfOwnGameAsync(GetUserId(), id);
        var state = await _service.GetAsync(analysisId, lang ?? "en", owner: true);
        if (analysisId is not int aid) return NotFound();
        if (!state.CanGenerate && !state.Running) return Conflict(new { reason = "noAnalysis" });
        _service.Start(aid, lang ?? "en");
        return Ok(await _service.GetAsync(aid, lang ?? "en", owner: true));
    }

    [AllowAnonymous]
    [HttpGet("shared/{token}/explanations")]
    public async Task<ActionResult<GameExplanationsDto>> GetShared(string token, [FromQuery] string? lang)
        => Ok(await _service.GetAsync(await _service.AnalysisOfSharedGameAsync(token), lang ?? "en", owner: false));
}
