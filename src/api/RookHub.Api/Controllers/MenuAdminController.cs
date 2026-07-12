using Microsoft.AspNetCore.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>Admin: Sichtbarkeit der Menüeinträge konfigurieren.</summary>
[ApiController]
[Route("api/admin/menu")]
[HasPermission(Permissions.MenuManage)]
public class MenuAdminController : BaseApiController
{
    private readonly MenuVisibilityService _menu;
    public MenuAdminController(MenuVisibilityService menu) => _menu = menu;

    /// <summary>Vollständige Konfiguration aller bekannten Menüeinträge (inkl. Defaults).</summary>
    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await _menu.GetConfigAsync());

    /// <summary>Konfiguration setzen. Unbekannte Keys werden ignoriert.</summary>
    [HttpPut]
    public async Task<IActionResult> Put([FromBody] List<MenuItemConfigDto> items)
    {
        await _menu.SaveConfigAsync(items ?? new List<MenuItemConfigDto>());
        return Ok(await _menu.GetConfigAsync());
    }
}
