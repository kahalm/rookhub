using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Controllers.League;

/// <summary>
/// Warteschlange der Partie-Zusammenfassungen (0.913.0, <see cref="ClubGameSummaries"/>) für den Formular-Watcher
/// (<c>~/claude/formulare-bot/watch.sh</c>, Schiene <c>summary</c>, Konto <c>claude-prod</c>). Nur Admins. Die Daten der
/// Partie liest der Watcher über die normalen Wege (<c>GET api/league/club/games/{id}</c>, <c>…/evals</c>,
/// <c>…/comparisons</c> mit <c>?club=</c>).
/// </summary>
[ApiController]
[Route("api/admin/club-game-summaries")]
[HasPermission(Permissions.AdminOnly)]
public class AdminClubGameSummaryController(ClubGameSummaries summaries) : BaseApiController
{
    /// <summary>Offene Aufträge, älteste zuerst → <c>[{ clubGameId, clubId, requestedAt, requestedByUserId }]</c>.</summary>
    [HttpGet]
    public async Task<IActionResult> Queue(CancellationToken ct) => Ok(await summaries.QueueAsync(ct));

    /// <summary>Auftrag übernehmen; 409, wenn er nicht (mehr) offen ist.</summary>
    [HttpPost("{gameId:int}/start")]
    public async Task<IActionResult> Start(int gameId, CancellationToken ct) => Result(await summaries.StartAsync(gameId, ct));

    public sealed record CompleteRequest(string? Markdown, string? Model);

    /// <summary>Fertigen Text abliefern (Markdown, siehe docs/api/partie-zusammenfassung.md); 400 leer oder zu lang.</summary>
    [HttpPut("{gameId:int}")]
    public async Task<IActionResult> Complete(int gameId, [FromBody] CompleteRequest body, CancellationToken ct) =>
        Result(await summaries.CompleteAsync(gameId, body?.Markdown, body?.Model, ct));

    public sealed record FailRequest(string? Error);

    /// <summary>Fehlschlag melden; eine ältere fertige Fassung bleibt lesbar.</summary>
    [HttpPut("{gameId:int}/fail")]
    public async Task<IActionResult> Fail(int gameId, [FromBody] FailRequest? body, CancellationToken ct) =>
        Result(await summaries.FailAsync(gameId, body?.Error, ct));

    private IActionResult Result(ClubGameSummaries.Outcome o) => o switch
    {
        ClubGameSummaries.Outcome.Ok => NoContent(),
        ClubGameSummaries.Outcome.NotFound => NotFound(),
        ClubGameSummaries.Outcome.Conflict => Conflict(),
        _ => BadRequest(new { reason = "invalid" }),
    };
}
