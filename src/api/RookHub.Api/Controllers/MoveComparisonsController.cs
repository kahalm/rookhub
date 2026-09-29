using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>„Züge vergleichen" (0.602.0): 2–4 Kandidatenzüge einer Stellung durchrechnen lassen und erklären, warum der
/// beste besser ist — siehe <see cref="MoveComparisonService"/>.</summary>
[ApiController]
[Route("api/move-comparisons")]
[Authorize]
public class MoveComparisonsController : BaseApiController
{
    private readonly MoveComparisonService _comparisons;

    public MoveComparisonsController(MoveComparisonService comparisons) { _comparisons = comparisons; }

    /// <summary>Steht eine Engine bereit (eigene oder Haus), werden Begründungen geschrieben, wie viele laufen schon?
    /// Literal-Route vor <c>{id:int}</c>.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<MoveComparisonStatusDto>> Status(CancellationToken ct)
        => Ok(await _comparisons.StatusAsync(GetUserId(), ct));

    [HttpGet]
    public async Task<ActionResult<List<MoveComparisonSummaryDto>>> List(CancellationToken ct)
        => Ok(await _comparisons.ListAsync(GetUserId(), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MoveComparisonDto>> Get(int id, CancellationToken ct)
    {
        var dto = await _comparisons.GetAsync(GetUserId(), id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>Anlegen → der Vergleich mit Status <c>candidates</c>. 400 mit <c>reason</c> ∈ invalid-fen / game-over /
    /// invalid-move / too-few-moves / too-many-moves / too-many-open / no-engine / too-many-jobs.</summary>
    [HttpPost]
    public async Task<ActionResult<MoveComparisonDto>> Create([FromBody] CreateMoveComparisonRequest request, CancellationToken ct)
    {
        try { return Ok(await _comparisons.CreateAsync(GetUserId(), request, ct)); }
        catch (MoveComparisonException ex) { return BadRequest(new { reason = ex.Reason, message = ex.Message }); }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => await _comparisons.DeleteAsync(GetUserId(), id, ct) ? NoContent() : NotFound();
}
