using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>Admin-Rollenverwaltung (RBAC): Rollen + Permissions CRUD und Rollen-Zuweisung an Nutzer.
/// Gated über <see cref="Permissions.RolesManage"/> (standardmäßig nur die admin-Rolle).
/// Fehlerfälle (404/400 mit <c>{ message }</c>) wirft der Dienst als Domänen-Ausnahmen, der globale
/// DomainExceptionFilter übersetzt sie — hier wird nichts gefangen.</summary>
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
        => Ok(await _roles.CreateAsync(dto));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<RoleDto>> Update(int id, [FromBody] UpdateRoleDto dto)
        => Ok(await _roles.UpdateAsync(id, dto));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _roles.DeleteAsync(id);
        return NoContent();
    }

    /// <summary>Rollen-Ids eines Users (für die Zuweisungs-UI).</summary>
    [HttpGet("/api/admin/users/{userId:int}/roles")]
    public async Task<ActionResult<UserRolesDto>> GetUserRoles(int userId)
        => Ok(await _roles.GetUserRolesAsync(userId));

    /// <summary>Rollen einer Gruppe — alle Mitglieder haben sie (0.589.0).</summary>
    [HttpGet("/api/admin/groups/{groupId:int}/roles")]
    public async Task<ActionResult<GroupRolesDto>> GetGroupRoles(int groupId)
        => Ok(await _roles.GetGroupRolesAsync(groupId));

    /// <summary>Setzt die (Nicht-Admin-)Rollen einer Gruppe; 400 für „Everyone".</summary>
    [HttpPut("/api/admin/groups/{groupId:int}/roles")]
    public async Task<IActionResult> SetGroupRoles(int groupId, [FromBody] SetUserRolesDto dto)
    {
        await _roles.SetGroupRolesAsync(groupId, dto);
        return NoContent();
    }

    /// <summary>Setzt die (Nicht-Admin-)Rollen eines Users auf genau diese Menge.</summary>
    [HttpPut("/api/admin/users/{userId:int}/roles")]
    public async Task<IActionResult> SetUserRoles(int userId, [FromBody] SetUserRolesDto dto)
    {
        await _roles.SetUserRolesAsync(userId, dto);
        return NoContent();
    }
}
