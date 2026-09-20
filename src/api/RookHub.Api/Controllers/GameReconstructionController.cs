using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// „Partie rekonstruieren": eine Partie aus Bruchstücken (Zugfolgen + Stellungen) zusammentragen.
/// Alles gehört dem angemeldeten Nutzer; jede Änderung antwortet mit der ganzen Rekonstruktion,
/// weil die Auswertung der Kette von allen Teilen zusammen abhängt.
/// </summary>
[ApiController]
[Route("api/reconstructions")]
[Authorize]
public class GameReconstructionController : BaseApiController
{
    private readonly GameReconstructionService _service;
    public GameReconstructionController(GameReconstructionService service) => _service = service;

    /// <summary>Eigene Rekonstruktionen (zuletzt geänderte zuerst).</summary>
    [HttpGet]
    public async Task<ActionResult<List<ReconstructionListItemDto>>> List(CancellationToken ct)
        => Ok(await _service.ListAsync(GetUserId(), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ReconstructionDetailDto>> Get(int id, CancellationToken ct)
    {
        var dto = await _service.GetAsync(GetUserId(), id, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>Neue (leere) Rekonstruktion. 400 mit <c>reason: too-many</c> am Deckel.</summary>
    [HttpPost]
    public async Task<ActionResult<ReconstructionDetailDto>> Create([FromBody] ReconstructionHeadRequest req, CancellationToken ct)
    {
        try { return Ok(await _service.CreateAsync(GetUserId(), req, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { reason = ex.Message }); }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ReconstructionDetailDto>> UpdateHead(int id, [FromBody] ReconstructionHeadRequest req, CancellationToken ct)
    {
        var dto = await _service.UpdateHeadAsync(GetUserId(), id, req, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => await _service.DeleteAsync(GetUserId(), id, ct) ? NoContent() : NotFound();

    /// <summary>Teil anhängen. 400 mit <c>reason</c> ∈ <c>invalid-fen</c>/<c>no-moves</c>/<c>too-many-parts</c>.</summary>
    [HttpPost("{id:int}/parts")]
    public async Task<ActionResult<ReconstructionDetailDto>> AddPart(int id, [FromBody] ReconstructionPartRequest req, CancellationToken ct)
    {
        try
        {
            var dto = await _service.AddPartAsync(GetUserId(), id, req, ct);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (ArgumentException ex) { return BadRequest(new { reason = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { reason = ex.Message }); }
    }

    /// <summary>Reihenfolge setzen (Literal-Route VOR <c>{partId}</c>).</summary>
    [HttpPut("{id:int}/parts/order")]
    public async Task<ActionResult<ReconstructionDetailDto>> Reorder(int id, [FromBody] ReconstructionOrderRequest req, CancellationToken ct)
    {
        var dto = await _service.ReorderAsync(GetUserId(), id, req.PartIds ?? new List<int>(), ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpPut("{id:int}/parts/{partId:int}")]
    public async Task<ActionResult<ReconstructionDetailDto>> UpdatePart(int id, int partId, [FromBody] ReconstructionPartRequest req, CancellationToken ct)
    {
        try
        {
            var dto = await _service.UpdatePartAsync(GetUserId(), id, partId, req, ct);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (ArgumentException ex) { return BadRequest(new { reason = ex.Message }); }
    }

    [HttpDelete("{id:int}/parts/{partId:int}")]
    public async Task<ActionResult<ReconstructionDetailDto>> DeletePart(int id, int partId, CancellationToken ct)
    {
        var dto = await _service.DeletePartAsync(GetUserId(), id, partId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

}
