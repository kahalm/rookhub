using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Controllers.Games;

/// <summary>
/// „Teilen-Link → volle Seite“ (Wunsch 2026-11-22, <see cref="ShareLinkTargets"/>): die Teilen-Seite fragt für einen ANGEMELDETEN, ob er
/// dasselbe auf dem normalen Weg sehen darf, und leitet dann um. Nur angemeldet (Gäste 401, sie fragen gar nicht erst).
/// </summary>
[ApiController]
[Route("api/share-links")]
[Authorize]
public class ShareLinkTargetController(ShareLinkTargets targets, PermissionResolver? permissions = null) : BaseApiController
{
    /// <summary><c>?kind=g|ga|cg|s|lp|tl|r&amp;token=…&amp;id=…</c> (<c>id</c> nur bei <c>s</c>, dem Liga-Link <c>/s/{token}/club-games/{id}</c>)
    /// → <c>{ path }</c> der normalen Seite ohne Unterseite, z. B. <c>/club-games/241</c>. 404 für ALLES andere (unbekannter oder
    /// widerrufener Link, kein Zugriff, unbekannte Art) — ohne Unterschied, damit die Antwort nichts über fremde Partien verrät.</summary>
    [HttpGet("target")]
    public async Task<IActionResult> Target([FromQuery] string? kind, [FromQuery] string? token, [FromQuery] int? id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var canViewLeague = kind is "cg" or "s" or "lp" && await HasPermissionAsync(permissions, Permissions.LeagueView);
        var path = await targets.ResolveAsync(GetUserId(), IsAdmin, canViewLeague, kind, token, id, ct);
        return path is null ? NotFound() : Ok(new { path });
    }
}
