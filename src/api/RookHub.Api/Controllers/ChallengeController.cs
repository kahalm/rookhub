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

    /// <summary>Schickt ein Puzzle als Challenge an einen Freund.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateChallengeDto dto)
    {
        try
        {
            var challenge = await _challengeService.CreateAsync(GetUserId(), dto.ToUserId, dto.PuzzleId);
            return Ok(new { id = challenge.Id, message = "Challenge sent." });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
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
