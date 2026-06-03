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

    /// <summary>
    /// Lädt für jedes Rating-Fenster ein eindeutiges Zufalls-Puzzle — für das Offline-
    /// Vorab-Laden eines ganzen Endless-Runs (ein Request statt vieler).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("random-batch")]
    public async Task<ActionResult<List<PuzzleDto>>> GetRandomBatch([FromBody] RandomBatchRequestDto dto)
    {
        if (dto?.Windows == null || dto.Windows.Count == 0)
            return Ok(new List<PuzzleDto>());
        // Schutz gegen überzogene Anfragen.
        var windows = dto.Windows.Take(100).Select(w => (w.MinRating, w.MaxRating));
        int? userId = int.TryParse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), out var id) ? id : null;
        var puzzles = await _puzzleService.GetRandomBatchAsync(userId, windows, dto.Themes, dto.ExcludeSolved);
        return Ok(puzzles);
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

    [HttpGet("stats/breakdown")]
    public async Task<ActionResult<PuzzleBreakdownDto>> GetBreakdown()
    {
        return Ok(await _puzzleService.GetBreakdownAsync(GetUserId()));
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

    private static readonly Regex SessionIdPattern = new(ValidationConstants.SessionIdPattern, RegexOptions.Compiled);

    private static bool IsValidSessionId(string? sessionId)
        => !string.IsNullOrEmpty(sessionId) && sessionId.Length <= 36 && SessionIdPattern.IsMatch(sessionId);
}
