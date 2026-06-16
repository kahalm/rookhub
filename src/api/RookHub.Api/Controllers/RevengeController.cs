using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/revenge")]
[Authorize]
public class RevengeController : BaseApiController
{
    private readonly RevengeNotificationService _service;

    public RevengeController(RevengeNotificationService service) => _service = service;

    /// <summary>Ergebnis einer Revanche melden (vom Puzzle-Solver, fire-and-forget). Benachrichtigt den Ziel-User.</summary>
    [HttpPost("result")]
    public async Task<IActionResult> Result([FromBody] RevengeResultDto dto)
    {
        // dto.Solved wird bewusst ignoriert — das Ergebnis leitet der Service serverseitig aus den
        // echten Puzzle-Versuchen des Avengers her (Schutz vor fabrizierten/Spam-Benachrichtigungen).
        var created = await _service.RecordAsync(GetUserId(), dto.TargetUserId, dto.PuzzleId);
        return Ok(new { created });
    }

    /// <summary>Revanche-Benachrichtigungen an mich (jemand hat eines meiner gescheiterten Puzzles angegangen).</summary>
    [HttpGet("notifications")]
    public async Task<ActionResult<List<RevengeNotificationDto>>> Notifications()
        => Ok(await _service.GetForUserAsync(GetUserId()));

    /// <summary>Anzahl ungelesener Revanche-Benachrichtigungen (Navbar-Badge).</summary>
    [HttpGet("notifications/count")]
    public async Task<IActionResult> UnseenCount()
        => Ok(new { count = await _service.GetUnseenCountAsync(GetUserId()) });

    /// <summary>Alle eigenen Revanche-Benachrichtigungen als gelesen markieren.</summary>
    [HttpPost("notifications/seen")]
    public async Task<IActionResult> MarkSeen()
    {
        await _service.MarkAllSeenAsync(GetUserId());
        return Ok(new { message = "Marked as seen." });
    }
}
