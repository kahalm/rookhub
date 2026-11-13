using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using RookHub.Api.DTOs.Games;
using RookHub.Api.Services.League;

namespace RookHub.Api.Controllers.League;

/// <summary>
/// LeagueHub — Vereinsübersicht und interessante Stellungen der Vereinspartien (0.848.0, Wunsch 2026-10-26). Lesen mit
/// <see cref="Permissions.LeagueView"/>, im Verein von <c>?club=</c> (<see cref="LeagueClubResolver"/>). Regeln:
/// <see cref="LeagueClubOverview"/>, <see cref="ClubGameMarks"/>.
/// </summary>
[ApiController]
[Route("api/league/club")]
[Authorize]
public class LeagueClubOverviewController(LeagueClubResolver clubs, LeagueClubOverview overview, ClubGameMarks marks,
    LeagueClubService club, MarkedPositionService markedPositions) : BaseApiController
{
    private async Task<IActionResult> WithClubAsync(CancellationToken ct, Func<LeagueClub, Task<IActionResult>> run, int? preferred = null)
    {
        var (c, error) = await LeagueClubAsync(clubs, ct, preferred);
        return c is null ? error! : await run(c);
    }

    /// <summary>Alle Begegnungen der eigenen Mannschaften der laufenden Saison mit Brettern und Partien → <c>{ season, leagues[{ tnr,
    /// name, rounds[{ number, date, matches[LineupMatch] }] }], marks{ clubGameId: Zahl markierter Stellungen } }</c>.</summary>
    [HttpGet("overview")]
    [HasPermission(Permissions.LeagueView)]
    public Task<IActionResult> Overview(CancellationToken ct) => WithClubAsync(ct, async c => Ok(await overview.BuildAsync(c, GetUserId(), ct)));

    /// <summary>Interessante Stellungen einer Vereinspartie → <c>[{ fen, positionKey, ply?, variationFromPly?, variationUci?, count }]</c>
    /// (Hauptlinie mit <c>ply</c> = Sprungmarke, sonst aus einer Variante); 404 unbekannt oder aus einem anderen Verein.</summary>
    [HttpGet("games/{id:int}/marks")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Marks(int id, CancellationToken ct) =>
        await WithClubAsync(ct, async c => await marks.ForGameAsync(c, id, GetUserId(), ct) is { } m ? Ok(m) : NotFound(),
            await club.ClubOfGameAsync(id, ct));

    /// <summary>Eigene Schlagworte und Bemerkung zu einer interessanten Stellung dieser Vereinspartie setzen (0.862.0) — die
    /// Stellung wird dabei auch selbst markiert → die Stellungen neu wie <c>GET …/marks</c>. 404 Partie unbekannt/fremd,
    /// 400 <c>{ reason }</c> (<c>invalid</c>, <c>invalidFen</c>, <c>limit</c>).</summary>
    [HttpPut("games/{id:int}/marks/notes")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> SetMarkNotes(int id, [FromBody] MarkedPositionNotesDto body, CancellationToken ct) =>
        await WithClubAsync(ct, async c =>
        {
            if (body is null) return BadRequest(new { reason = "invalid" });
            var (ok, error) = await marks.SetNotesAsync(c, id, GetUserId(), body, markedPositions, ct);
            if (!ok) return error == "notFound" ? NotFound() : BadRequest(new { reason = error });
            return Ok(await marks.ForGameAsync(c, id, GetUserId(), ct));
        }, await club.ClubOfGameAsync(id, ct));

    /// <summary>PGN + schlanke Bewertungen der analysierten Partien unter <c>?ids=1,2,…</c> (höchstens
    /// <see cref="ClubGameReader.MaxReviewInputs"/>) → <c>{ clubGameId: { pgn, evals } }</c>, für die „!!"-Marker der Übersicht
    /// (0.849.0). Partien ohne Analyse oder aus einem anderen Verein fehlen; 400 bei zu vielen Ids.</summary>
    [HttpGet("games/reviews")]
    [HasPermission(Permissions.LeagueView)]
    public Task<IActionResult> Reviews([FromQuery] string? ids, CancellationToken ct) => WithClubAsync(ct, async c =>
    {
        var list = IdList(ids);
        if (list.Count > ClubGameReader.MaxReviewInputs) return BadRequest(new { message = "tooMany" });
        return Ok(await club.ReviewInputsAsync(c, list, ct));
    });

    /// <summary>Zahl der markierten Stellungen je Vereinspartie unter <c>?ids=1,2,…</c> (höchstens
    /// <see cref="ClubGameMarks.MaxCountIds"/>) → <c>{ clubGameId: n }</c>, für die „◆"-Marker in den Aufstellungen einer Runde
    /// (0.850.0). Ohne Markierung, fremd oder unbekannt fehlt die Id; 400 bei zu vielen Ids.</summary>
    [HttpGet("games/marks")]
    [HasPermission(Permissions.LeagueView)]
    public Task<IActionResult> MarkCounts([FromQuery] string? ids, CancellationToken ct) => WithClubAsync(ct, async c =>
    {
        var list = IdList(ids);
        if (list.Count > ClubGameMarks.MaxCountIds) return BadRequest(new { message = "tooMany" });
        return Ok(await marks.CountsForClubAsync(c, list, ct));
    });

    /// <summary>Zugvergleiche „Fehlzug gegen besten Zug" zu den Fehlern einer Vereinspartie (0.908.0, <see cref="ClubGameComparisons"/>)
    /// → <c>[{ ply, playedUci, analysisBestUci, comparison }]</c> nach Halbzug (leer = keine/noch keine); 404 unbekannt oder aus
    /// einem anderen Verein.</summary>
    [HttpGet("games/{id:int}/comparisons")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Comparisons(int id, [FromServices] ClubGameComparisons comparisons, CancellationToken ct) =>
        await WithClubAsync(ct, async c => await comparisons.ForGameAsync(c, id, ct) is { } list ? Ok(list) : NotFound(),
            await club.ClubOfGameAsync(id, ct));

    /// <summary>EIN Zugvergleich dieser Vereinspartie (volle Vergleichsseite, auch während er rechnet) → wie
    /// <c>GET /api/move-comparisons/{id}</c>; 404 Partie/Vergleich unbekannt oder fremd.</summary>
    [HttpGet("games/{id:int}/comparisons/{comparisonId:int}")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Comparison(int id, int comparisonId, [FromServices] ClubGameComparisons comparisons, CancellationToken ct) =>
        await WithClubAsync(ct, async c => await comparisons.GetAsync(c, id, comparisonId, ct) is { } dto ? Ok(dto) : NotFound(),
            await club.ClubOfGameAsync(id, ct));

    /// <summary>Zusammenfassung der Vereinspartie (0.913.0, <see cref="ClubGameSummaries"/>) → <c>{ status, markdown, doneAt,
    /// requestedAt, stale, model, error }</c>, <c>status</c> = <c>none</c> ohne Anforderung; 404 unbekannt oder fremd.</summary>
    [HttpGet("games/{id:int}/summary")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Summary(int id, [FromServices] ClubGameSummaries summaries, CancellationToken ct) =>
        await WithClubAsync(ct, async c => await summaries.GetAsync(c, id, ct) is { } dto ? Ok(dto) : NotFound(),
            await club.ClubOfGameAsync(id, ct));

    /// <summary>Empfohlene Übungen zur Vereinspartie (0.960.0, <see cref="RookHub.Api.Services.Puzzles.GameExercises"/>) → <c>{ groups }</c>;
    /// Rechte wie <c>GET …/summary</c>, 404 unbekannt oder fremd.</summary>
    [HttpGet("games/{id:int}/summary/exercises")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> SummaryExercises(int id, [FromServices] ClubGameSummaries summaries,
        [FromServices] RookHub.Api.Services.Puzzles.GameExercises exercises, CancellationToken ct) =>
        await WithClubAsync(ct, async c => await summaries.GetAsync(c, id, ct) is null ? NotFound() : Ok(await exercises.ForClubGameAsync(id, ct, await EbookViewerAsync())),
            await club.ClubOfGameAsync(id, ct));

    /// <summary>Zusammenfassung anfordern bzw. neu anfordern — nur Admins (Wunsch 2026-11-08: „auf Wunsch vom Admin");
    /// geschrieben wird vom Watcher. → wie <c>GET …/summary</c>; 403 kein Admin, 404 unbekannt oder fremd.</summary>
    [HttpPost("games/{id:int}/summary")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> RequestSummary(int id, [FromServices] ClubGameSummaries summaries, CancellationToken ct)
    {
        if (!IsAdmin) return Forbid();
        return await WithClubAsync(ct, async c => await summaries.RequestAsync(c, id, GetUserId(), ct) is { } dto ? Ok(dto) : NotFound(),
            await club.ClubOfGameAsync(id, ct));
    }

    /// <summary>Teilen-Link der Vereinspartie (<see cref="ClubGameLinks"/>) → <c>{ token }</c>, <c>null</c> = nicht geteilt; 404 unbekannt oder fremd.
    /// Recht wie der Knopf „Partie teilen" einer Ligapartie: wer die Vereinspartie lesen darf.</summary>
    [HttpGet("games/{id:int}/share")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Share(int id, [FromServices] ClubGameLinks links, CancellationToken ct) =>
        await WithClubAsync(ct, async c => await links.TokenAsync(c, id, ct) is (true, var token) ? Ok(new { token }) : NotFound(),
            await club.ClubOfGameAsync(id, ct));

    /// <summary>Teilen-Link anlegen (ein schon bestehender bleibt) → <c>{ token }</c>; 404 unbekannt oder fremd.</summary>
    [HttpPost("games/{id:int}/share")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> CreateShare(int id, [FromServices] ClubGameLinks links, CancellationToken ct) =>
        await WithClubAsync(ct, async c => await links.CreateAsync(c, id, ct) is { } token ? Ok(new { token }) : NotFound(),
            await club.ClubOfGameAsync(id, ct));

    /// <summary>Teilen-Link widerrufen → 204; 404 unbekannt oder fremd.</summary>
    [HttpDelete("games/{id:int}/share")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> RevokeShare(int id, [FromServices] ClubGameLinks links, CancellationToken ct) =>
        await WithClubAsync(ct, async c => await links.RevokeAsync(c, id, ct) ? NoContent() : NotFound(),
            await club.ClubOfGameAsync(id, ct));

    private static List<int> IdList(string? ids) =>
        (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).Distinct().ToList();
}
