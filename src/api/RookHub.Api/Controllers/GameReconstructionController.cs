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

    /// <summary>
    /// Die ganze Partie hinter einem Teilen-Link — ohne Anmeldung, wie bei <c>/g/{token}</c>.
    /// Steht VOR <c>{id:int}</c>, kollidiert damit aber ohnehin nicht (Token ist keine Zahl).
    /// </summary>
    [HttpGet("shared/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<SharedReconstructionDto>> Shared(string token, CancellationToken ct)
    {
        var dto = await _service.GetSharedAsync(token, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>Öffentlichen Link einschalten (idempotent) → <c>{ shareToken }</c>.</summary>
    [HttpPost("{id:int}/share")]
    public async Task<ActionResult<ReconstructionShareDto>> Share(int id, CancellationToken ct)
    {
        var token = await _service.ShareAsync(GetUserId(), id, ct);
        return token == null ? NotFound() : Ok(new ReconstructionShareDto { ShareToken = token });
    }

    /// <summary>Link abschalten — ein späteres Teilen erzeugt ein anderes Token.</summary>
    [HttpDelete("{id:int}/share")]
    public async Task<IActionResult> Unshare(int id, CancellationToken ct)
        => await _service.UnshareAsync(GetUserId(), id, ct) ? NoContent() : NotFound();

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

    /// <summary>
    /// „Lücke schließen": sucht die Züge, die von der Stellung am Ende des vorigen Teils zu diesem
    /// Teil führen. Immer 200 — dass es keinen Weg gibt (bzw. das Budget nicht reichte), ist eine
    /// AUSKUNFT im <c>reason</c> und kein Fehler des Aufrufers.
    /// </summary>
    [HttpPost("{id:int}/parts/{partId:int}/gap")]
    public async Task<ActionResult<ReconstructionGapResultDto>> SolveGap(int id, int partId, [FromBody] ReconstructionGapRequest? req, CancellationToken ct)
    {
        var dto = await _service.SolveGapAsync(GetUserId(), id, partId, req?.MaxPlies, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// „Lücke schließen": sucht die Wege und SETZT sie als Vorschläge in die Liste (vor
    /// <paramref name="partId"/>). Vorschläge zählen nicht zur Partie — sie sind zum Durchsehen da.
    /// Antwort enthält die ganze Rekonstruktion samt Vorschlägen und den Grund, falls nichts kam.
    /// </summary>
    [HttpPost("{id:int}/parts/{partId:int}/gap/propose")]
    public async Task<ActionResult<ReconstructionGapProposalDto>> ProposeGap(int id, int partId, [FromBody] ReconstructionGapRequest? req, CancellationToken ct)
    {
        var dto = await _service.ProposeGapAsync(GetUserId(), id, partId, req?.MaxPlies, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>Die Vorschläge vor diesem Teil verwerfen (idempotent).</summary>
    [HttpPost("{id:int}/parts/{partId:int}/gap/discard")]
    public async Task<ActionResult<ReconstructionDetailDto>> DiscardProposals(int id, int partId, CancellationToken ct)
    {
        var dto = await _service.DiscardProposalsAsync(GetUserId(), id, partId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Eine Stellung aus einem Vorschlag übernehmen („die stimmt") bzw. ihre korrigierte Fassung:
    /// sie kommt als eigenes Teil vor <paramref name="partId"/> und teilt die Lücke in zwei.
    /// 400 mit <c>reason: invalid-fen</c>/<c>too-many-parts</c>.
    /// </summary>
    [HttpPost("{id:int}/parts/{partId:int}/gap/waypoint")]
    public async Task<ActionResult<ReconstructionDetailDto>> AddWaypoint(int id, int partId, [FromBody] ReconstructionWaypointRequest req, CancellationToken ct)
    {
        try
        {
            var dto = await _service.AddWaypointAsync(GetUserId(), id, partId, req?.Fen, req?.Certain ?? true, ct);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (ArgumentException ex) { return BadRequest(new { reason = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { reason = ex.Message }); }
    }

    /// <summary>
    /// Einen gefundenen Weg übernehmen: die Züge werden als eigenes Teil VOR <paramref name="partId"/>
    /// eingesetzt, beide schließen danach nahtlos an. 400 mit <c>reason</c> ∈ <c>does-not-fit</c>/
    /// <c>no-gap</c>/<c>no-previous</c>/<c>no-anchor</c>/<c>no-moves</c>/<c>target-not-a-position</c>.
    /// </summary>
    [HttpPost("{id:int}/parts/{partId:int}/gap/apply")]
    public async Task<ActionResult<ReconstructionDetailDto>> ApplyGap(int id, int partId, [FromBody] ReconstructionGapApplyRequest req, CancellationToken ct)
    {
        try
        {
            var dto = await _service.ApplyGapAsync(GetUserId(), id, partId, req?.Moves, ct);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (ArgumentException ex) { return BadRequest(new { reason = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { reason = ex.Message }); }
    }
}
