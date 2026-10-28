using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Ebooks;

namespace RookHub.Api.Controllers.Ebooks;

/// <summary>
/// Externe Diagramm-Worker (0.870.0): ein Rechner außerhalb des Servers holt wartende Bücher, erkennt die Diagramme mit
/// <c>tools/DiagramCli</c> und schickt das Ergebnis zurück (<see cref="EbookDiagramWorkerService"/>). Angemeldet mit einem
/// API-Token mit Scope <c>diagrams</c>, den nur Nutzer mit <c>ebooks.manage</c> anlegen dürfen (Profil → API-Tokens); der
/// Scope-Zaun lässt so einen Token nur hierher. Ein Browser-Login eines Admins kommt zum Ausprobieren auch durch.
/// Doku: <c>docs/api/ebooks.md</c> → „Externe Diagramm-Worker“.
/// </summary>
[ApiController]
[Route("api/diagram-worker")]
[HasPermission(Permissions.EbooksManage)]
[RequireTokenScope(ApiTokenService.DiagramScope, Message = "API token scope 'diagrams' required")]
public class DiagramWorkerController : BaseApiController
{
    private readonly EbookDiagramWorkerService _service;

    public DiagramWorkerController(EbookDiagramWorkerService service) => _service = service;

    /// <summary>Besitzer der Leihe: je API-Token (ein Token = ein Worker), sonst der angemeldete Nutzer.</summary>
    internal string Owner => User.FindFirst(ApiTokenAuthenticationHandler.TokenIdClaim)?.Value is { } tokenId
        ? "pat:" + tokenId
        : "user:" + GetUserId();

    /// <summary>Nächstes wartendes Buch leihen → 200 Auftrag, 204 wenn nichts wartet.</summary>
    [HttpPost("claim")]
    public async Task<ActionResult<DiagramWorkerJobDto>> Claim([FromBody] DiagramWorkerClaimDto? dto, CancellationToken ct)
    {
        var job = await _service.ClaimAsync(Owner, dto?.Worker, ct);
        return job == null ? NoContent() : Ok(job);
    }

    /// <summary>Datei des Auftrags herunterladen (Bereichsanfragen erlaubt). Leihe weg → 409.</summary>
    [HttpGet("jobs/{jobId:guid}/files/{fileId:int}")]
    public async Task<IActionResult> Download(Guid jobId, int fileId, CancellationToken ct)
    {
        var (path, _) = await _service.FileAsync(jobId, Owner, fileId, ct);
        return PhysicalFile(path, "application/octet-stream", enableRangeProcessing: true);
    }

    /// <summary>Lebenszeichen: verlängert die Leihe → <c>{ leaseUntil }</c>. Leihe weg → 409.</summary>
    [HttpPost("jobs/{jobId:guid}/heartbeat")]
    public async Task<IActionResult> Heartbeat(Guid jobId, CancellationToken ct)
        => Ok(new { leaseUntil = await _service.HeartbeatAsync(jobId, Owner, ct) });

    /// <summary>Buch zurückgeben (mit <c>error</c>: externe Worker lassen es liegen, der Server macht es selbst) → 204.</summary>
    [HttpPost("jobs/{jobId:guid}/release")]
    public async Task<IActionResult> Release(Guid jobId, [FromBody] DiagramWorkerReleaseDto? dto, CancellationToken ct)
    {
        await _service.ReleaseAsync(jobId, Owner, dto?.Error, ct);
        return NoContent();
    }

    /// <summary>Ergebnis abliefern → 200 <c>{ ebookId, state, diagramCount }</c>; ungültig → 400, Leihe weg → 409.</summary>
    [HttpPost("jobs/{jobId:guid}/result")]
    [RequestSizeLimit(128L * 1024 * 1024)]
    public async Task<ActionResult<DiagramWorkerResultAcceptedDto>> SubmitResult(Guid jobId, [FromBody] DiagramWorkerResultDto dto, CancellationToken ct)
        => Ok(await _service.SubmitAsync(jobId, Owner, dto, ct));
}
