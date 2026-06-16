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

    /// <summary>Alle drei Kategorien für die gewählte Periode (Default „alltime"); `top` 1–500.</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string period = "alltime", [FromQuery] int top = 100)
    {
        if (!LeaderboardService.Periods.Contains(period))
            return BadRequest(new { message = "period must be one of: daily, weekly, monthly, alltime." });
        return Ok(await _service.GetAsync(period, Math.Clamp(top, 1, 500)));
    }
}
