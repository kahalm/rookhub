using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// „Katalog": ein Besitzer (aktuell nur Admins) gibt Usern/Gruppen die Liste seiner Kurse+Repertoires
/// frei; berechtigte Viewer sehen sie und fordern einzelne Items an, der Besitzer genehmigt/lehnt ab.
/// Besitzer-Endpoints (grants/requests) sind admin-only (erster Schritt).
/// </summary>
[ApiController]
[Route("api/catalog")]
[Authorize]
public class CatalogController : BaseApiController
{
    private readonly CatalogService _service;

    public CatalogController(CatalogService service) => _service = service;

    // ---- Viewer ----

    /// <summary>Ob der aufrufende User überhaupt einen Katalog sehen darf (Menü/Route-Gate).</summary>
    [HttpGet("access")]
    public async Task<ActionResult<CatalogAccessDto>> Access()
        => Ok(new CatalogAccessDto { HasAccess = await _service.HasAccessAsync(GetUserId(), IsAdmin) });

    /// <summary>Die für den Viewer sichtbaren Katalog-Items (aller Besitzer, die ihm Zugriff gaben).</summary>
    [HttpGet]
    public async Task<ActionResult<List<CatalogItemDto>>> Get()
        => Ok(await _service.GetCatalogAsync(GetUserId()));

    /// <summary>Fordert ein Item an. 404, wenn es nicht existiert / kein Katalog-Zugriff besteht.</summary>
    [HttpPost("request")]
    public async Task<IActionResult> Request([FromBody] CatalogRequestInputDto dto)
    {
        try { return Ok(new { status = await _service.RequestAsync(GetUserId(), dto.ItemType, dto.ItemId) }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    // ---- Besitzer (admin-only, erster Schritt) ----

    [HttpGet("grants")]
    public async Task<ActionResult<CatalogGrantsDto>> GetGrants()
        => IsAdmin ? Ok(await _service.GetGrantsAsync(GetUserId())) : Forbid();

    [HttpPut("grants")]
    public async Task<ActionResult<CatalogGrantsDto>> SetGrants([FromBody] CatalogGrantsDto dto)
        => IsAdmin ? Ok(await _service.SetGrantsAsync(GetUserId(), dto.UserIds, dto.GroupIds)) : Forbid();

    [HttpGet("requests")]
    public async Task<ActionResult<List<CatalogRequestDto>>> GetRequests()
        => IsAdmin ? Ok(await _service.GetPendingRequestsAsync(GetUserId())) : Forbid();

    [HttpPost("requests/{id}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        if (!IsAdmin) return Forbid();
        try { await _service.ApproveAsync(GetUserId(), id, IsAdmin); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("requests/{id}/decline")]
    public async Task<IActionResult> Decline(int id)
    {
        if (!IsAdmin) return Forbid();
        try { await _service.DeclineAsync(GetUserId(), id); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}
