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

    /// <summary>Ein einzelnes Repo frisch (ungecacht) — für den „👁 beobachten"-Schnell-Poll (10 s) der
    /// CI-Seite, damit nur DIESES Repo häufig abgefragt wird statt der ganzen Übersicht. 404 bei
    /// unbekanntem Repo / fehlendem Token.</summary>
    [HttpGet("runs/{repo}")]
    public async Task<ActionResult<CiRepoDto>> Repo(string repo, CancellationToken ct)
    {
        var dto = await _github.GetRepoAsync(repo, ct);
        return dto is null ? NotFound() : Ok(dto);
    }
}

/// <summary>Service-to-service-Endpoint (kein Admin): ein Stack, den rookhub nicht per HTTP erreichen
/// kann (z. B. log-watcher in eigenem Docker-Netz), meldet hier beim Start seine laufende Build-SHA/Ref.
/// Auth via Shared-Secret-Header <c>X-Build-Report-Key</c> (== <c>CI:BuildReportSecret</c>).</summary>
[ApiController]
[Route("api/ci")]
public class CiBuildReportController : ControllerBase
{
    private readonly GithubActionsService _github;
    private readonly IConfiguration _config;
    public CiBuildReportController(GithubActionsService github, IConfiguration config)
    {
        _github = github;
        _config = config;
    }

    [HttpPost("build-report")]
    [AllowAnonymous]
    public IActionResult Report([FromBody] CiBuildReportDto dto, [FromHeader(Name = "X-Build-Report-Key")] string? key)
    {
        var secret = _config["CI:BuildReportSecret"];
        if (string.IsNullOrEmpty(secret) || !FixedTimeEquals(key, secret))
            return Unauthorized();
        if (dto is null || string.IsNullOrWhiteSpace(dto.Repo))
            return BadRequest();
        _github.ReportBuild(dto.Repo.Trim(), dto.Sha, dto.Ref);
        return NoContent();
    }

    private static bool FixedTimeEquals(string? a, string? b)
    {
        if (a is null || b is null) return false;
        var ba = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
