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
    private async Task<bool> AllowedAsync(CancellationToken ct) =>
        User.IsInRole("Admin") || (permissions != null
            ? (await permissions.GetAsync(GetUserId(), ct)).Has(Permissions.LeagueView)
            : User.HasClaim(PermissionAuthorizationHandler.PermissionClaimType, Permissions.LeagueView));

    /// <summary>Die eigenen tiefen Analysen (0.690.0) — ohne Recht eine leere Liste, die Partieseite fragt jeden.</summary>
    [HttpGet]
    public async Task<ActionResult<List<AnalysisJobDto>>> List(CancellationToken ct)
        => await AllowedAsync(ct) ? Ok(await service.ListAsync(GetUserId(), ct)) : Ok(new List<AnalysisJobDto>());

    [HttpPost]
    public async Task<ActionResult<DeepAnalysisDto>> Start([FromBody] DeepAnalysisRequest? req, CancellationToken ct)
    {
        if (!await AllowedAsync(ct)) return Forbid();
        try
        {
            return Ok(await service.StartAsync(GetUserId(), req?.Fen, ct));
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
