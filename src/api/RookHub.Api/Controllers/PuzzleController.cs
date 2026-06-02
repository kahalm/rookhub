using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    public async Task<ActionResult<PuzzleStatsDto>> GetStats([FromQuery] int? vizLevel)
    {
        return Ok(await _puzzleService.GetStatsAsync(GetUserId(), vizLevel ?? 0));
    }

    [HttpGet("history")]
    public async Task<ActionResult<List<PuzzleAttemptDto>>> GetHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        return Ok(await _puzzleService.GetHistoryAsync(GetUserId(), page, pageSize));
    }

    [HttpGet("elo-history")]
    public async Task<ActionResult<List<EloHistoryPointDto>>> GetEloHistory([FromQuery] int limit = 500)
    {
        return Ok(await _puzzleService.GetEloHistoryAsync(GetUserId(), limit));
    }

    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    [HttpPost("{id}/attempt/anonymous")]
    public async Task<IActionResult> RecordAnonymousAttempt(int id, [FromBody] AnonymousAttemptDto dto)
    {
        if (!IsValidSessionId(dto.SessionId))
            return BadRequest(new { message = "Invalid session ID." });

        try
        {
            var result = await _puzzleService.RecordAnonymousAttemptAsync(dto.SessionId, id,
                new RecordPuzzleAttemptDto { Solved = dto.Solved, TimeSpentSeconds = dto.TimeSpentSeconds, MoveLog = dto.MoveLog, ScreenWidth = dto.ScreenWidth, ScreenHeight = dto.ScreenHeight, VisualizationLevel = dto.VisualizationLevel });
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    [HttpGet("stats/anonymous")]
    public async Task<IActionResult> GetAnonymousStats([FromQuery] string sessionId)
    {
        if (!IsValidSessionId(sessionId))
            return BadRequest(new { message = "Invalid session ID." });

        return Ok(await _puzzleService.GetAnonymousStatsAsync(sessionId));
    }

    [HttpPost("claim-session")]
    public async Task<IActionResult> ClaimSession([FromBody] ClaimSessionDto dto)
    {
        if (!IsValidSessionId(dto.SessionId))
            return BadRequest(new { message = "Invalid session ID." });

        var claimed = await _puzzleService.ClaimSessionAsync(GetUserId(), dto.SessionId);
        return Ok(new { claimed });
    }

    private static readonly Regex SessionIdPattern = new(@"^[a-fA-F0-9\-]{1,36}$", RegexOptions.Compiled);

    private static bool IsValidSessionId(string? sessionId)
        => !string.IsNullOrEmpty(sessionId) && sessionId.Length <= 36 && SessionIdPattern.IsMatch(sessionId);
}
