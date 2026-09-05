using Microsoft.AspNetCore.Authorization;
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
