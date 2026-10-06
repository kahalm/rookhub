using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>„Tiefe Analyse" aus dem ⋮-Menü der Partieseite (0.686.0) — nur Vereinsmitglieder (<c>league.view</c>, live).</summary>
[ApiController]
[Route("api/deep-analysis")]
[Authorize]
public class DeepAnalysisController(DeepAnalysisService service, PermissionResolver? permissions = null) : BaseApiController
{
    [HttpPost]
    public async Task<ActionResult<DeepAnalysisDto>> Start([FromBody] DeepAnalysisRequest? req, CancellationToken ct)
    {
        var allowed = User.IsInRole("Admin") || (permissions != null
            ? (await permissions.GetAsync(GetUserId(), ct)).Has(Permissions.LeagueView)
            : User.HasClaim(PermissionAuthorizationHandler.PermissionClaimType, Permissions.LeagueView));
        if (!allowed) return Forbid();
        try
        {
            return Ok(await service.StartAsync(GetUserId(), req?.Fen, ct));
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
