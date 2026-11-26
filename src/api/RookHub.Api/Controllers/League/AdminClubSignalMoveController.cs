using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Services.Club;

namespace RookHub.Api.Controllers.League;

/// <summary>
/// Reparatur: Signal-Königszug des Siegers am Ende gespeicherter Vereinspartien entfernen (1.022.0,
/// <see cref="ClubSignalMoveRepair"/>, Regel <see cref="Services.Games.SignalKingMove"/>). Nur Admins, über alle Vereine.
/// <c>dryRun</c> ist VORGABE (<c>true</c>): nur auflisten; mit <c>dryRun=false</c> über den normalen Korrekturweg.
/// </summary>
[ApiController]
[Route("api/admin/club-games/signal-moves")]
[HasPermission(Permissions.AdminOnly)]
public class AdminClubSignalMoveController(ClubSignalMoveRepair repair) : BaseApiController
{
    /// <summary>→ <c>{ dryRun, scanned, games: [{ clubGameId, clubId, white, black, result, plies, move, reason, applied,
    /// summaryRequested, error }] }</c>. <c>id</c> = nur diese Vereinspartie.</summary>
    [HttpPost]
    public async Task<IActionResult> Run([FromQuery] bool dryRun = true, [FromQuery] int? id = null, CancellationToken ct = default) =>
        Ok(await repair.RunAsync(GetUserId(), dryRun, id, ct));
}
