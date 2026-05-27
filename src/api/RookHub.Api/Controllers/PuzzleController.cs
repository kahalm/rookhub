using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/puzzles")]
[Authorize]
public class PuzzleController : BaseApiController
{
    private readonly PuzzleService _puzzleService;

    public PuzzleController(PuzzleService puzzleService) => _puzzleService = puzzleService;

    [AllowAnonymous]
    [HttpGet("rating-range")]
    public async Task<IActionResult> GetRatingRange()
    {
        var range = await _puzzleService.GetRatingRangeAsync();
        if (range == null)
            return NotFound(new { message = "No puzzles in database." });
        return Ok(new { min = range.Value.Min, max = range.Value.Max });
    }

    [AllowAnonymous]
    [HttpGet("random")]
    public async Task<IActionResult> GetRandom(
        [FromQuery] int? minRating,
        [FromQuery] int? maxRating,
        [FromQuery] string? themes,
        [FromQuery] bool excludeSolved = false)
    {
        int? userId = int.TryParse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), out var id) ? id : null;
        var puzzle = await _puzzleService.GetRandomAsync(userId, minRating, maxRating, themes, excludeSolved);
        if (puzzle == null)
            return NotFound(new { message = "No puzzles found matching criteria." });
        return Ok(puzzle);
    }

    [AllowAnonymous]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var puzzle = await _puzzleService.GetByIdAsync(id);
        if (puzzle == null)
            return NotFound(new { message = "Puzzle not found." });
        return Ok(puzzle);
    }

    [HttpPost("{id}/attempt")]
    public async Task<IActionResult> RecordAttempt(int id, [FromBody] RecordPuzzleAttemptDto dto)
    {
        try
        {
            var result = await _puzzleService.RecordAttemptAsync(GetUserId(), id, dto);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("stats")]
    public async Task<ActionResult<PuzzleStatsDto>> GetStats()
    {
        return Ok(await _puzzleService.GetStatsAsync(GetUserId()));
    }

    [HttpGet("history")]
    public async Task<ActionResult<List<PuzzleAttemptDto>>> GetHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        return Ok(await _puzzleService.GetHistoryAsync(GetUserId(), page, pageSize));
    }
}
