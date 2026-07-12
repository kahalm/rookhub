using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>Admin-Rollenverwaltung (RBAC): Rollen + Permissions CRUD und Rollen-Zuweisung an Nutzer.
/// Gated über <see cref="Permissions.RolesManage"/> (standardmäßig nur die admin-Rolle).</summary>
[ApiController]
[Route("api/admin/roles")]
[HasPermission(Permissions.RolesManage)]
public class RolesAdminController : BaseApiController
{
    private readonly RoleAdminService _roles;
    public RolesAdminController(RoleAdminService roles) => _roles = roles;

    /// <summary>Alle Rollen inkl. Permissions + Mitgliederzahl.</summary>
    [HttpGet]
    public async Task<ActionResult<List<RoleDto>>> List() => Ok(await _roles.ListAsync());

    /// <summary>Alle im Code definierten Permission-Schlüssel (für die Auswahl in der UI).</summary>
    [HttpGet("permissions")]
    public ActionResult<IReadOnlyList<string>> AllPermissions() => Ok(_roles.AllPermissions());

    [HttpPost]
    public async Task<ActionResult<RoleDto>> Create([FromBody] CreateRoleDto dto)
    {
        try { return Ok(await _roles.CreateAsync(dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<RoleDto>> Update(int id, [FromBody] UpdateRoleDto dto)
    {
        try { return Ok(await _roles.UpdateAsync(id, dto)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try { await _roles.DeleteAsync(id); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Rollen-Ids eines Users (für die Zuweisungs-UI).</summary>
    [HttpGet("/api/admin/users/{userId:int}/roles")]
    public async Task<ActionResult<UserRolesDto>> GetUserRoles(int userId)
    {
        try { return Ok(await _roles.GetUserRolesAsync(userId)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Setzt die (Nicht-Admin-)Rollen eines Users auf genau diese Menge.</summary>
    [HttpPut("/api/admin/users/{userId:int}/roles")]
    public async Task<IActionResult> SetUserRoles(int userId, [FromBody] SetUserRolesDto dto)
    {
        try { await _roles.SetUserRolesAsync(userId, dto); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}
