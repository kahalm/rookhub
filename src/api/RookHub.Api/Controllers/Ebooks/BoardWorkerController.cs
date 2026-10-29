using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Ebooks;

namespace RookHub.Api.Controllers.Ebooks;

/// <summary>
/// Externe Brett-Ausschneider (0.874.0): derselbe Worker und derselbe Token (Scope <c>diagrams</c>) wie
/// <see cref="DiagramWorkerController"/> — ein Rechner leiht einen Auftrag, lädt das PDF, schneidet die Bretter aus und liefert
/// <c>cells.npz</c>, <c>prep.json</c> und die Bilder als ZIP (<see cref="BoardCutJobService"/>). Doku: <c>docs/api/ebooks.md</c>
/// → „Brett-Ausschneide-Aufträge“.
/// </summary>
[ApiController]
[Route("api/board-worker")]
[HasPermission(Permissions.EbooksManage)]
[RequireTokenScope(ApiTokenService.DiagramScope, Message = "API token scope 'diagrams' required")]
public class BoardWorkerController : BaseApiController
{
    /// <summary>Obergrenze eines Ergebnisses (Riesenbände: einige Tausend Bretter als PNG).</summary>
    public const long MaxResultBytes = 1024L * 1024 * 1024;

    private readonly BoardCutJobService _service;

    public BoardWorkerController(BoardCutJobService service) => _service = service;

    /// <summary>Besitzer der Leihe: je API-Token (ein Token = ein Worker), sonst der angemeldete Nutzer.</summary>
    internal string Owner => User.FindFirst(ApiTokenAuthenticationHandler.TokenIdClaim)?.Value is { } tokenId
        ? "pat:" + tokenId
        : "user:" + GetUserId();

    /// <summary>Ältesten wartenden Auftrag leihen → 200 Auftrag, 204 wenn nichts wartet.</summary>
    [HttpPost("claim")]
    public async Task<ActionResult<BoardWorkerJobDto>> Claim([FromBody] BoardWorkerClaimDto? dto, CancellationToken ct)
    {
        var job = await _service.ClaimAsync(Owner, dto?.Worker, ct);
        return job == null ? NoContent() : Ok(job);
    }

    /// <summary>Das PDF des Auftrags (Bereichsanfragen erlaubt). Leihe weg → 409.</summary>
    [HttpGet("jobs/{jobId:guid}/file")]
    public async Task<IActionResult> Download(Guid jobId, CancellationToken ct)
        => PhysicalFile(await _service.FileAsync(jobId, Owner, ct), "application/pdf", enableRangeProcessing: true);

    [HttpPost("jobs/{jobId:guid}/heartbeat")]
    public async Task<IActionResult> Heartbeat(Guid jobId, CancellationToken ct)
        => Ok(new { leaseUntil = await _service.HeartbeatAsync(jobId, Owner, ct) });

    /// <summary>Zurückgeben: ohne <c>error</c> wieder frei, mit <c>error</c> fehlgeschlagen → 204.</summary>
    [HttpPost("jobs/{jobId:guid}/release")]
    public async Task<IActionResult> Release(Guid jobId, [FromBody] BoardWorkerReleaseDto? dto, CancellationToken ct)
    {
        await _service.ReleaseAsync(jobId, Owner, dto?.Error, ct);
        return NoContent();
    }

    /// <summary>Ergebnis abliefern (multipart: <c>cells</c> = cells.npz, <c>prep</c> = prep.json, <c>crops</c> = ZIP der
    /// <c>&lt;Diagramm-Id&gt;.png</c>) → 200; ungültig → 400, Leihe weg → 409.</summary>
    [HttpPost("jobs/{jobId:guid}/result")]
    [RequestSizeLimit(MaxResultBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxResultBytes)]
    public async Task<ActionResult<BoardWorkerResultAcceptedDto>> SubmitResult(Guid jobId, IFormFile cells, IFormFile prep, IFormFile crops,
        CancellationToken ct)
    {
        await using var c = cells.OpenReadStream();
        await using var p = prep.OpenReadStream();
        await using var z = crops.OpenReadStream();
        return Ok(await _service.SubmitAsync(jobId, Owner, c, p, z, ct));
    }
}
