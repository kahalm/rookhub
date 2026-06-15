using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/challenges")]
[Authorize]
public class ChallengeController : BaseApiController
{
    private readonly ChallengeService _challengeService;

    public ChallengeController(ChallengeService challengeService) => _challengeService = challengeService;

    /// <summary>Schickt ein Puzzle als Challenge an einen oder mehrere Freunde. Ungültige Empfänger
    /// (man selbst / kein Freund / bereits offene gleiche Challenge) werden übersprungen und im Ergebnis
    /// gemeldet; 404 nur, wenn das Puzzle selbst fehlt.</summary>
    [HttpPost]
    public async Task<ActionResult<ChallengeBatchResultDto>> Create([FromBody] CreateChallengeBatchDto dto)
    {
        try
        {
            var result = await _challengeService.CreateBatchAsync(GetUserId(), dto.ToUserIds, dto.PuzzleId, dto.Source);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Offene Challenges an mich (Posteingang).</summary>
    [HttpGet("incoming")]
    public async Task<ActionResult<List<IncomingChallengeDto>>> Incoming()
        => Ok(await _challengeService.GetIncomingAsync(GetUserId()));

    /// <summary>Von mir gesendete Challenges inkl. Ergebnis-Status.</summary>
    [HttpGet("outgoing")]
    public async Task<ActionResult<List<OutgoingChallengeDto>>> Outgoing()
        => Ok(await _challengeService.GetOutgoingAsync(GetUserId()));

    /// <summary>Anzahl offener eingehender Challenges (Navbar-Badge).</summary>
    [HttpGet("incoming/count")]
    public async Task<IActionResult> IncomingCount()
        => Ok(new { count = await _challengeService.GetIncomingCountAsync(GetUserId()) });

    /// <summary>Ergebnis einer Challenge melden (nur der Empfänger).</summary>
    [HttpPost("{id}/resolve")]
    public async Task<IActionResult> Resolve(int id, [FromBody] ResolveChallengeDto dto)
    {
        try
        {
            await _challengeService.ResolveAsync(id, GetUserId(), dto.Solved, dto.TimeSpentSeconds);
            return Ok(new { message = "Challenge resolved." });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}
