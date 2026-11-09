using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Controllers.Games;

/// <summary>Warteschlange der Zusammenfassungen eigener Partien (0.923.0) für den Formular-Watcher, wie
/// <c>api/admin/club-game-summaries</c>, dazu die Lese-Wege der Partie. Nur Admins.</summary>
[ApiController]
[Route("api/admin/own-game-summaries")]
[Authorize(Roles = "Admin")]
public class AdminOwnGameSummaryController(OwnGameSummaries summaries) : BaseApiController
{
    /// <summary>Offene Aufträge → <c>[{ savedGameId, userId, requestedAt }]</c>.</summary>
    [HttpGet]
    public async Task<IActionResult> Queue(CancellationToken ct) => Ok(await summaries.QueueAsync(ct));

    [HttpPost("{id:int}/start")]
    public async Task<IActionResult> Start(int id, CancellationToken ct) => Result(await summaries.StartAsync(id, ct));

    public sealed record CompleteRequest(string? Markdown, string? Model);

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Complete(int id, [FromBody] CompleteRequest body, CancellationToken ct) =>
        Result(await summaries.CompleteAsync(id, body?.Markdown, body?.Model, ct));

    public sealed record FailRequest(string? Error);

    [HttpPut("{id:int}/fail")]
    public async Task<IActionResult> Fail(int id, [FromBody] FailRequest? body, CancellationToken ct) =>
        Result(await summaries.FailAsync(id, body?.Error, ct));

    /// <summary>Partie in der Form von <c>GET api/league/club/games/{id}</c>, soweit der Watcher sie liest.</summary>
    [HttpGet("{id:int}/game")]
    public async Task<IActionResult> Game(int id, CancellationToken ct) =>
        await summaries.WatcherGameAsync(id, ct) is { } g ? Ok(g) : NotFound();

    [HttpGet("{id:int}/evals")]
    public async Task<IActionResult> Evals(int id, CancellationToken ct) =>
        await summaries.WatcherEvalsAsync(id, ct) is { } e ? Ok(e) : NotFound();

    [HttpGet("{id:int}/comparisons")]
    public async Task<IActionResult> Comparisons(int id, CancellationToken ct) =>
        await summaries.ComparisonsAsync(null, id, ct) is { } c ? Ok(c) : NotFound();

    private IActionResult Result(OwnGameSummaries.Outcome o) => o switch
    {
        OwnGameSummaries.Outcome.Ok => NoContent(),
        OwnGameSummaries.Outcome.NotFound => NotFound(),
        OwnGameSummaries.Outcome.Conflict => Conflict(),
        _ => BadRequest(new { reason = "invalid" }),
    };
}
