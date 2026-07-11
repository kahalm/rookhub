using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>Effektive Menü-Sichtbarkeit für den aktuellen Aufrufer (auch anonym).</summary>
[ApiController]
[Route("api/menu")]
public class MenuController : BaseApiController
{
    private readonly MenuVisibilityService _menu;
    public MenuController(MenuVisibilityService menu) => _menu = menu;

    /// <summary>Liste der sichtbaren Menü-Keys für den (ggf. nicht eingeloggten) Aufrufer.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get()
    {
        int? userId = GetUserIdOrNull();
        var isAdmin = User.IsInRole("Admin");
        return Ok(await _menu.GetVisibleKeysAsync(userId, isAdmin));
    }
}
