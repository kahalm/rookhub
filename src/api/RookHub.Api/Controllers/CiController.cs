using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>Admin-CI-Übersicht: letzte GitHub-Actions-Läufe der beteiligten Repos.</summary>
[ApiController]
[Route("api/admin/ci")]
[Authorize(Roles = "Admin")]
public class CiController : BaseApiController
{
    private readonly GithubActionsService _github;
    public CiController(GithubActionsService github) => _github = github;

    /// <summary>Die letzten 5 Workflow-Läufe je beteiligtem Repo (server-seitig kurz gecacht).</summary>
    [HttpGet("runs")]
    public async Task<ActionResult<CiOverviewDto>> Runs(CancellationToken ct)
        => Ok(await _github.GetOverviewAsync(ct));
}
