using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.League;

namespace RookHub.Api.Controllers;

/// <summary>
/// LeagueHub — Aufstellungen je Runde + erste Züge je Partie (2026-10-08). Lesen mit <see cref="Permissions.LeagueView"/>,
/// Züge eintragen mit <see cref="Permissions.LeagueContribute"/> (wie Vereinspartien anlegen); Verwalter
/// (<see cref="Permissions.LeagueManage"/>, live) dürfen jede Paarung und fremde Einträge ändern. Jede Anfrage im Verein von
/// <c>?club=</c> (<see cref="LeagueClubResolver"/>). Regeln: <see cref="LeagueGameMoves"/>.
/// </summary>
[ApiController]
[Route("api/league")]
[Authorize]
public class LeagueLineupsController(LeagueGameMoves moves, LeagueClubResolver clubs, PermissionResolver? permissions = null)
    : BaseApiController
{
    public sealed class MovesRequest
    {
        /// <summary>SAN mit Leerzeichen, gern mit Zugnummern oder deutschen Figurenbuchstaben; leer = löschen.</summary>
        public string? Moves { get; set; }
    }

    private async Task<IActionResult> WithClubAsync(CancellationToken ct, Func<LeagueClub, Task<IActionResult>> run)
    {
        var (club, error) = await LeagueClubAsync(clubs, ct);
        return club is null ? error! : await run(club);
    }

    /// <summary>Alle Begegnungen der Runde mit ihren Brettpaarungen und den hinterlegten Zügen → <c>{ tnr, round, date, canEdit,
    /// matches[{ matchNo, home, away, homePts, awayPts, own, boards[{ board, homePlayer, homeTitle, homeElo, awayPlayer, awayTitle,
    /// awayElo, homeColor, result, forfeit, moves, canEditMoves }] }] }</c>; 404, wenn es die Runde nicht gibt.</summary>
    [HttpGet("{tnr:int}/round/{round:int}/lineups")]
    [HasPermission(Permissions.LeagueView)]
    public Task<IActionResult> Lineups(int tnr, int round, CancellationToken ct) => WithClubAsync(ct, async club =>
    {
        var contribute = await HasPermissionAsync(permissions, Permissions.LeagueContribute);
        var manage = await HasPermissionAsync(permissions, Permissions.LeagueManage);
        return await moves.LineupsAsync(club, tnr, round, GetUserId(), contribute, manage, ct) is { } l ? Ok(l) : NotFound();
    });

    /// <summary>Die ersten Züge einer Partie speichern (leer = löschen) → <c>{ moves }</c> (englische SAN). 404 ohne Paarung,
    /// 403 <c>foreignMatch</c>/<c>notYours</c>, 400 <c>noGame</c>/<c>tooLong</c>/<c>illegal</c> (mit <c>move</c> + <c>ply</c>).</summary>
    [HttpPut("{tnr:int}/round/{round:int}/match/{matchNo:int}/board/{board:int}/moves")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> SaveMoves(int tnr, int round, int matchNo, int board, [FromBody] MovesRequest? req,
        CancellationToken ct) => WithClubAsync(ct, async club =>
        Answer(await moves.SaveAsync(club, GetUserId(), await HasPermissionAsync(permissions, Permissions.LeagueManage),
            tnr, round, matchNo, board, req?.Moves, ct)));

    /// <summary>Die Züge einer Partie löschen → 204 (auch wenn keine da waren); 404 ohne Paarung, 403 wie beim Speichern.</summary>
    [HttpDelete("{tnr:int}/round/{round:int}/match/{matchNo:int}/board/{board:int}/moves")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> DeleteMoves(int tnr, int round, int matchNo, int board, CancellationToken ct) =>
        WithClubAsync(ct, async club =>
        {
            var r = await moves.SaveAsync(club, GetUserId(), await HasPermissionAsync(permissions, Permissions.LeagueManage),
                tnr, round, matchNo, board, null, ct);
            return r.Reason is null ? NoContent() : Answer(r);
        });

    private IActionResult Answer(LeagueGameMoves.SaveResult r) => r.Reason switch
    {
        null => Ok(new { moves = r.Moves }),
        "notFound" => NotFound(),
        "foreignMatch" or "notYours" => StatusCode(StatusCodes.Status403Forbidden, new { reason = r.Reason }),
        _ => BadRequest(new { reason = r.Reason, move = r.Move, ply = r.Ply }),
    };
}
