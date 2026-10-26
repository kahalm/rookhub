using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
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
    LeagueClubService club) : BaseApiController
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
        await WithClubAsync(ct, async c => await marks.ForGameAsync(c, id, ct) is { } m ? Ok(m) : NotFound(),
            await club.ClubOfGameAsync(id, ct));
}
