using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Services.League;

namespace RookHub.Api.Controllers.League;

/// <summary>Öffentliche Ansicht EINER geteilten Ligapartie (<see cref="LeagueGameShares"/>), ohne Anmeldung, nur lesend. Eigener Pfad
/// statt <c>/api/league/s/{token}</c>: dort hängen an einem Token die Begegnung, Karten und Upload.</summary>
[ApiController]
[Route("api/league/s-game")]
[AllowAnonymous]
[EnableRateLimiting("anonymous-tournament")]
public class LeagueGameShareController(LeagueGameShares shares) : ControllerBase
{
    /// <summary>Die Partie des Links → <see cref="LeagueGameShares.SharedGame"/>; 404 = ungültig oder abgelaufen.</summary>
    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token, CancellationToken ct) =>
        await shares.ReadAsync(token, ct) is { } g ? Ok(g) : NotFound();
}
