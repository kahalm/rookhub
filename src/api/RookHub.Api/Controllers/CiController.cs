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
    public async Task<IActionResult> Report([FromBody] CiBuildReportDto dto, [FromHeader(Name = "X-Build-Report-Key")] string? key)
    {
        var secret = _config["CI:BuildReportSecret"];
        if (string.IsNullOrEmpty(secret) || !FixedTimeEquals(key, secret))
            return Unauthorized();
        if (dto is null || string.IsNullOrWhiteSpace(dto.Repo))
            return BadRequest();
        await _github.ReportBuildAsync(dto.Repo.Trim(), dto.Sha, dto.Ref);
        return NoContent();
    }

    /// <summary>GitHub-<c>workflow_run</c>-Webhook (Push-Modell): meldet Start/Ende jedes Workflow-Laufs,
    /// damit die CI-Seite in Echtzeit aktualisiert, statt die GitHub-API zu pollen. Einmal je Repo unter
    /// Settings → Webhooks eintragen (Payload-URL = diese Route, Content-Type application/json, „Workflow
    /// runs"-Event, Secret == <c>CI:GithubWebhookSecret</c> bzw. Fallback <c>CI:BuildReportSecret</c>).
    /// Verifiziert GitHubs HMAC-SHA256-Signatur (<c>X-Hub-Signature-256</c>).</summary>
    [HttpPost("gh-webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> GithubWebhook()
    {
        using var reader = new StreamReader(Request.Body, System.Text.Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        var secret = _config["CI:GithubWebhookSecret"];
        if (string.IsNullOrEmpty(secret)) secret = _config["CI:BuildReportSecret"];
        if (string.IsNullOrEmpty(secret)) return Unauthorized();
        if (!VerifyGithubSignature(secret, body, Request.Headers["X-Hub-Signature-256"].FirstOrDefault()))
            return Unauthorized();

        // Nur workflow_run-Events verarbeiten (ping u. a. bestätigen wir stumm mit 204).
        if (Request.Headers["X-GitHub-Event"].FirstOrDefault() != "workflow_run")
            return NoContent();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("workflow_run", out var wr)) return NoContent();
            var repo = root.TryGetProperty("repository", out var repoEl)
                       && repoEl.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(repo)) return NoContent();
            var run = ParseWorkflowRun(wr);
            if (run != null) _github.ReportRun(repo!, run);
        }
        catch (System.Text.Json.JsonException) { return BadRequest(); }
        return NoContent();
    }

    /// <summary>Baut aus dem <c>workflow_run</c>-Objekt einen <see cref="CiRunDto"/>. Tag-Läufe (head_branch
    /// null) bekommen keinen Ref — den liefert der reguläre GitHub-Poll (Tag-Name via sha-Map) nach.</summary>
    private static CiRunDto? ParseWorkflowRun(System.Text.Json.JsonElement wr)
    {
        if (!wr.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id)) return null;
        string? Str(string k) => wr.TryGetProperty(k, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() : null;
        DateTime Dt(string k) => wr.TryGetProperty(k, out var e) && e.TryGetDateTime(out var d) ? d : DateTime.UtcNow;
        var name = Str("name") ?? "";
        var title = Str("display_title");
        if (string.IsNullOrWhiteSpace(title)) title = name;
        var branch = Str("head_branch") ?? "";
        var runNumber = wr.TryGetProperty("run_number", out var rn) && rn.TryGetInt32(out var rnv) ? rnv : 0;
        string? actor = wr.TryGetProperty("actor", out var a) && a.TryGetProperty("login", out var al) ? al.GetString() : null;
        return new CiRunDto(id, name, title!, branch, Str("event") ?? "", Str("status") ?? "",
            Str("conclusion"), runNumber, Dt("created_at"), Dt("updated_at"), Str("html_url") ?? "",
            actor, Str("head_sha"), string.IsNullOrEmpty(branch) ? null : branch, false);
    }

    /// <summary>Verifiziert GitHubs <c>X-Hub-Signature-256</c>. HMAC-Berechnung bewusst über den
    /// geteilten <see cref="Services.SchachBotWebhookService.ComputeHmacHex"/> (eine Implementierung
    /// im Repo, wie in BotStatsController — CLAUDE.md-Konvention) statt einer eigenen Kopie.</summary>
    private static bool VerifyGithubSignature(string secret, string body, string? header)
    {
        if (string.IsNullOrEmpty(header) || !header.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            return false;
        var computed = Services.SchachBotWebhookService.ComputeHmacHex(secret, body);
        return FixedTimeEquals(computed, header.Substring("sha256=".Length).ToLowerInvariant());
    }

    /// <summary>Konstant-zeitiger String-Vergleich (Shared-Key/Signatur-Checks).</summary>
    private static bool FixedTimeEquals(string? a, string? b)
    {
        if (a is null || b is null) return false;
        var ba = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
