using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Controllers.Games;

/// <summary>Kopien von Vereinspartien in „Meine Partien" zu Verweisen umstellen (0.924.0, <see cref="ClubGameReferences"/>). Nur Admins.</summary>
[ApiController]
[Route("api/admin/club-game-references")]
[HasPermission(Permissions.AdminOnly)]
public class AdminClubGameReferencesController(ClubGameReferences references) : BaseApiController
{
    /// <summary>Probelauf: ändert nichts → <c>{ applied: false, convert, waiting, done, diverged, analysesDeleted, items[] }</c>.</summary>
    [HttpGet]
    public async Task<IActionResult> DryRun(CancellationToken ct) => Ok(await references.RunAsync(apply: false, ct));

    /// <summary>Umstellen; Antwort wie der Probelauf mit <c>applied: true</c>.</summary>
    [HttpPost("apply")]
    public async Task<IActionResult> Apply(CancellationToken ct) => Ok(await references.RunAsync(apply: true, ct));
}
