using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Der Leser von außen (0.687.0, <see cref="ScoresheetScanService.External"/>): der Watcher auf dem Server holt wartende
/// Formular-Einlesungen (RookHub UND LeagueHub), liest die Fotos selbst und gibt die Lesung zurück — statt des Modells
/// über den Anthropic-Schlüssel. Dieselben Leute wie beim Admin-Tab „Uploads" (messages.admin).
/// </summary>
[ApiController]
[Route("api/admin/scoresheets")]
[HasPermission(Permissions.MessagesAdmin)]
public class AdminScoresheetController(ScoresheetScanService scans) : BaseApiController
{
    /// <summary>Wartende Einlesungen, älteste zuerst (ohne Fotos).</summary>
    [HttpGet("pending")]
    public async Task<ActionResult<List<ExternalPendingScanDto>>> Pending(CancellationToken ct) =>
        Ok(await scans.PendingForExternalAsync(ct));

    /// <summary>Foto einer Seite (ab 1).</summary>
    [HttpGet("{id:int}/photo")]
    public async Task<IActionResult> Photo(int id, [FromQuery] int page = 1, CancellationToken ct = default) =>
        await scans.PhotoForExternalAsync(id, page, ct) is { } p ? File(p.Data, p.ContentType) : NotFound();

    /// <summary>Die Lesung übernehmen (Form der Modell-Antwort im Feld <c>transcription</c>) → die Einlesung; 400
    /// <c>reason</c> ∈ notPending/invalidTranscription, 404 unbekannt.</summary>
    [HttpPost("{id:int}/reading")]
    public async Task<IActionResult> Reading(int id, [FromBody] ExternalReadingRequest req, CancellationToken ct)
    {
        var (scan, reason) = await scans.ProcessReadingAsync(id, req.Transcription.GetRawText(), ct);
        return reason switch
        {
            null => Ok(scan),
            "notFound" => NotFound(),
            _ => BadRequest(new { reason }),
        };
    }

    /// <summary>Der Leser fängt an: wartend → läuft (die Seite zeigt dann „wird gelesen"). 204; 400 <c>notPending</c>, 404.</summary>
    [HttpPost("{id:int}/start")]
    public async Task<IActionResult> Start(int id, CancellationToken ct) =>
        await scans.StartExternalAsync(id, ct) switch
        {
            null => NoContent(),
            "notFound" => NotFound(),
            var reason => BadRequest(new { reason }),
        };

    /// <summary>Als gescheitert abschließen (<c>reason</c> ∈ unreadable/noMoves/failed) — der Hochladende bekommt die
    /// übliche Glocke „konnte nicht gelesen werden".</summary>
    [HttpPost("{id:int}/fail")]
    public async Task<IActionResult> Fail(int id, [FromBody] ExternalFailRequest req, CancellationToken ct) =>
        await scans.FailExternalAsync(id, req.Reason, ct) switch
        {
            null => NoContent(),
            "notFound" => NotFound(),
            var reason => BadRequest(new { reason }),
        };

    public sealed record ExternalReadingRequest(System.Text.Json.JsonElement Transcription);
    public sealed record ExternalFailRequest(string? Reason);
}
