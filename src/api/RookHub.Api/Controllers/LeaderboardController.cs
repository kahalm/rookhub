using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Bestenlisten (nur eingeloggte Nutzer): einzigartige Standard-Puzzles, Endlos-Läufe und
/// gelöste Kurs-Linien — je Periode daily/weekly/monthly/alltime.
/// </summary>
[ApiController]
[Route("api/leaderboards")]
[Authorize]
public class LeaderboardController : BaseApiController
{
    private readonly LeaderboardService _service;
    public LeaderboardController(LeaderboardService service) => _service = service;

    /// <summary>Obergrenze für <c>top</c>. Die Oberfläche fragt 5 an; mehr als eine Bildschirmseite braucht niemand,
    /// und eine 500er-Liste je Kategorie war ein Nutzerverzeichnis in einem Aufruf.</summary>
    public const int MaxTop = 50;

    /// <summary>Alle Kategorien für die gewählte Periode (Default „alltime"). Je Kategorie nur die
    /// besten `top` (1–<see cref="MaxTop"/>, Default 5) PLUS das Fenster ±`around` (0–25, Default 2) um den eigenen
    /// Platz; jeder Eintrag trägt seinen echten Rang + ein `isMe`-Flag.</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string period = "alltime", [FromQuery] int top = 5, [FromQuery] int around = 2)
    {
        if (!LeaderboardService.Periods.Contains(period))
            return BadRequest(new { message = "period must be one of: daily, weekly, monthly, alltime." });
        return Ok(await _service.GetAsync(period, GetUserId(), Math.Clamp(top, 1, MaxTop), Math.Clamp(around, 0, 25)));
    }
}
