using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// „Katalog": ein Besitzer (Recht <c>catalog.manage</c>, Admins immer) gibt Usern/Gruppen die Liste seiner
/// Kurse+Repertoires frei; berechtigte Viewer sehen sie und fordern einzelne Items an, der Besitzer genehmigt/lehnt ab.
/// Besitzer-Endpoints (grants/requests) verlangen <c>catalog.manage</c> — bis Codereview F5-005 hingen sie am
/// Admin-Flag, und das vergebbare Recht wirkte nirgends.
/// </summary>
[ApiController]
[Route("api/catalog")]
[Authorize]
public class CatalogController : BaseApiController
{
    private readonly CatalogService _service;
    private readonly PermissionResolver? _permissions;

    public CatalogController(CatalogService service, PermissionResolver? permissions = null)
    {
        _service = service;
        _permissions = permissions;
    }

    private Task<bool> CanManageAsync() => HasPermissionAsync(_permissions, Permissions.CatalogManage);

    // ---- Viewer ----

    /// <summary>Ob der aufrufende User überhaupt einen Katalog sehen darf (Menü/Route-Gate).</summary>
    [HttpGet("access")]
    public async Task<ActionResult<CatalogAccessDto>> Access()
        => Ok(new CatalogAccessDto { HasAccess = await _service.HasAccessAsync(GetUserId(), await CanManageAsync()) });

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

    // ---- Besitzer (catalog.manage) ----

    [HttpGet("grants")]
    public async Task<ActionResult<CatalogGrantsDto>> GetGrants()
        => await CanManageAsync() ? Ok(await _service.GetGrantsAsync(GetUserId())) : Forbid();

    [HttpPut("grants")]
    public async Task<ActionResult<CatalogGrantsDto>> SetGrants([FromBody] CatalogGrantsDto dto)
        => await CanManageAsync() ? Ok(await _service.SetGrantsAsync(GetUserId(), dto.UserIds, dto.GroupIds)) : Forbid();

    [HttpGet("requests")]
    public async Task<ActionResult<List<CatalogRequestDto>>> GetRequests()
        => await CanManageAsync() ? Ok(await _service.GetPendingRequestsAsync(GetUserId())) : Forbid();

    [HttpPost("requests/{id}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        if (!await CanManageAsync()) return Forbid();
        // Die Freigabe des Katalogs plus diese Genehmigung SIND die Zustimmung — geteilt wird wie bisher auch an
        // Nicht-Freunde (vorher stand hier IsAdmin, das an dieser Stelle immer true war).
        try { await _service.ApproveAsync(GetUserId(), id, isAdmin: true); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("requests/{id}/decline")]
    public async Task<IActionResult> Decline(int id)
    {
        if (!await CanManageAsync()) return Forbid();
        try { await _service.DeclineAsync(GetUserId(), id); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}
