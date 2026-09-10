using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Partie-Analysen: ein PGN einwerfen und jede Stellung von der Hintergrund-Engine durchrechnen
/// lassen. Vorstufe der Punktepartie und für sich nützlich — bisher ging nur Stellung für Stellung
/// (<see cref="AnalysisJobController"/>).
/// </summary>
[ApiController]
[Route("api/game-analyses")]
[Authorize]
public class GameAnalysisController : BaseApiController
{
    private readonly GameAnalysisService _service;

    public GameAnalysisController(GameAnalysisService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<List<GameAnalysisDto>>> List(CancellationToken ct)
        => Ok(await _service.ListAsync(GetUserId(), ct));

    /// <summary>
    /// Der kuratierte Bestand: Partien, die JEDER als Punktepartie spielen darf — auch ohne
    /// Anmeldung. Bewusst OHNE die Zugliste (die steckt nur im Detail-Abruf, und der bleibt auf
    /// eigene Analysen beschraenkt): hier gibt es Kopfdaten und Fortschritt, nicht die Partie.
    /// Literal-Route VOR <c>{id:int}</c>.
    /// </summary>
    [HttpGet("public")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<List<GameAnalysisDto>>> ListPublic(CancellationToken ct)
        => Ok(await _service.ListPublicAsync(ct));

    /// <summary>Partie in den kuratierten Bestand aufnehmen bzw. herausnehmen — Besitzer der
    /// Analyse oder Admin. Aendert NICHTS an der Analyse selbst, nur daran, wer sie spielen
    /// darf.</summary>
    [HttpPut("{id:int}/public")]
    public async Task<ActionResult<object>> SetPublic(int id, [FromBody] SetGameAnalysisPublicRequest req,
        CancellationToken ct)
    {
        var result = await _service.SetPublicAsync(GetUserId(), IsAdmin, id, req.IsPublic, ct);
        return result is null
            ? NotFound(new { message = "Analysis not found." })
            : Ok(new { isPublic = result.Value });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GameAnalysisDto>> Get(int id, CancellationToken ct)
    {
        var dto = await _service.GetAsync(GetUserId(), id, ct);
        return dto is null ? NotFound(new { message = "Analysis not found." }) : Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<GameAnalysisDto>> Create([FromBody] CreateGameAnalysisRequest req, CancellationToken ct)
    {
        try
        {
            return Ok(await _service.CreateAsync(GetUserId(), req, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // z. B. keine Hintergrund-Engine hinterlegt — der Nutzer soll das lesen, nicht raten.
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => await _service.DeleteAsync(GetUserId(), id, ct)
            ? NoContent()
            : NotFound(new { message = "Analysis not found." });
}
