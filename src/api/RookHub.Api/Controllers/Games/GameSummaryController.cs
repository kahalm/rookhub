using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Exceptions;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Controllers.Games;

/// <summary>Zusammenfassung und Fehler-Vergleiche einer EIGENEN Partie (0.923.0, <see cref="OwnGameSummaries"/>). Nur der Besitzer.</summary>
[ApiController]
[Route("api/games")]
[Authorize]
public class GameSummaryController(OwnGameSummaries summaries) : BaseApiController
{
    /// <summary>Stand → <c>{ status, markdown, doneAt, requestedAt, stale, model, error, readiness, source, quotaLeft, quotaLimit }</c>; 404 unbekannt/fremd.</summary>
    [HttpGet("{id:int}/summary")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) =>
        await summaries.GetAsync(GetUserId(), id, IsAdmin, ct) is { } dto ? Ok(dto) : NotFound();

    /// <summary>Teilen-Link <c>/g/{token}</c>, ohne Anmeldung, nur lesend → <c>{ status: done|none, markdown, doneAt, stale, model }</c>; 404 unbekannter Link.
    /// Kein eigener Rate-Limiter: derselbe globale je IP wie die übrigen <c>shared/{token}</c>-Endpunkte.</summary>
    [HttpGet("shared/{token}/summary")]
    [AllowAnonymous]
    public async Task<IActionResult> GetShared(string token, CancellationToken ct) =>
        await summaries.GetSharedAsync(token, ct) is { } dto ? Ok(dto) : NotFound();

    public sealed record RequestBody(string? Lang);

    /// <summary>Anfordern → Stand; 404 unbekannt/fremd, 429 <c>code summary_quota</c> Tageskontingent verbraucht,
    /// 409 <c>summary_analysis_unavailable</c>, 400 <c>summary_unsupported_start</c>.</summary>
    [HttpPost("{id:int}/summary")]
    public async Task<IActionResult> Request(int id, [FromBody] RequestBody? body, CancellationToken ct)
    {
        var (dto, error) = await summaries.RequestAsync(GetUserId(), id, IsAdmin, body?.Lang, ct);
        return error switch
        {
            OwnGameSummaries.RequestError.None => Ok(dto),
            OwnGameSummaries.RequestError.NotFound => NotFound(),
            OwnGameSummaries.RequestError.QuotaExceeded => StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Daily summary quota used up.", code = ApiErrorCodes.SummaryQuota }),
            OwnGameSummaries.RequestError.UnsupportedStart => BadRequest(new { message = "Only games from the starting position.", code = ApiErrorCodes.SummaryUnsupportedStart }),
            _ => Conflict(new { message = "No analysis available.", code = ApiErrorCodes.SummaryAnalysisUnavailable }),
        };
    }

    /// <summary>Zugvergleiche zu den Fehlern → <c>[{ ply, playedUci, analysisBestUci, comparison }]</c>; 404 unbekannt/fremd.</summary>
    [HttpGet("{id:int}/comparisons")]
    public async Task<IActionResult> Comparisons(int id, CancellationToken ct) =>
        await summaries.ComparisonsAsync(GetUserId(), id, ct) is { } list ? Ok(list) : NotFound();

    /// <summary>EIN Zugvergleich der Partie (volle Vergleichsseite); 404 unbekannt/fremd.</summary>
    [HttpGet("{id:int}/comparisons/{comparisonId:int}")]
    public async Task<IActionResult> Comparison(int id, int comparisonId, CancellationToken ct) =>
        await summaries.ComparisonAsync(GetUserId(), id, comparisonId, ct) is { } dto ? Ok(dto) : NotFound();
}
