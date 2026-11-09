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

/// <summary>Öffentliche Ansicht EINER geteilten Vereinspartie (<see cref="ClubGameLinks"/>): ohne Anmeldung, nur lesend, je 404 =
/// ungültig oder widerrufen.</summary>
[ApiController]
[Route("api/league/s-club-game/{token}")]
[AllowAnonymous]
[EnableRateLimiting("anonymous-tournament")]
public class ClubGameLinkController(ClubGameLinks links) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string token, CancellationToken ct) => await links.ReadAsync(token, ct) is { } g ? Ok(g) : NotFound();

    [HttpGet("evals")]
    public async Task<IActionResult> Evals(string token, CancellationToken ct) => await links.EvalsAsync(token, ct) is { } e ? Ok(e) : NotFound();

    [HttpGet("comparisons")]
    public async Task<IActionResult> Comparisons(string token, CancellationToken ct) => await links.ComparisonsAsync(token, ct) is { } c ? Ok(c) : NotFound();

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(string token, CancellationToken ct) => await links.SummaryAsync(token, ct) is { } s ? Ok(s) : NotFound();
}
