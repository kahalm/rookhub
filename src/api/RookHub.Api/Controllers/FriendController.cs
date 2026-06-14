using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/friends")]
[Authorize]
public class FriendController : BaseApiController
{
    private readonly FriendService _friendService;
    private readonly PuzzleService _puzzleService;

    public FriendController(FriendService friendService, PuzzleService puzzleService)
    {
        _friendService = friendService;
        _puzzleService = puzzleService;
    }

    [HttpGet]
    public async Task<ActionResult<List<FriendDto>>> GetFriends()
    {
        return Ok(await _friendService.GetFriendsAsync(GetUserId()));
    }

    /// <summary>Puzzle-Statistik eines Freundes für den Vergleich „Du vs. Freund". Nur zwischen Freunden sichtbar.</summary>
    [HttpGet("{userId}/stats")]
    public async Task<ActionResult<FriendStatsDto>> GetFriendStats(int userId)
    {
        if (!await _friendService.AreFriendsAsync(GetUserId(), userId))
            return StatusCode(403, new { message = "Not friends with this user." });

        var basic = await _friendService.GetUserBasicAsync(userId);
        if (basic == null)
            return NotFound(new { message = "User not found." });

        var stats = await _puzzleService.GetStatsAsync(userId);
        var breakdown = await _puzzleService.GetBreakdownAsync(userId);

        return Ok(new FriendStatsDto
        {
            UserId = userId,
            Username = basic.Username,
            DisplayName = basic.DisplayName,
            Stats = stats,
            Themes = breakdown.Themes
        });
    }

    /// <summary>„Revenge a Friend": Puzzles, an denen der Freund gescheitert ist und die er nie gelöst hat —
    /// du kannst sie nun selbst lösen. Nur zwischen akzeptierten Freunden.</summary>
    [HttpGet("{userId}/revenge")]
    public async Task<ActionResult<RevengeListDto>> GetRevenge(int userId)
    {
        if (!await _friendService.AreFriendsAsync(GetUserId(), userId))
            return StatusCode(403, new { message = "Not friends with this user." });

        var basic = await _friendService.GetUserBasicAsync(userId);
        if (basic == null)
            return NotFound(new { message = "User not found." });

        var puzzles = await _puzzleService.GetUnsolvedFailuresAsync(userId, GetUserId());

        return Ok(new RevengeListDto
        {
            UserId = userId,
            Username = basic.Username,
            DisplayName = basic.DisplayName,
            Puzzles = puzzles
        });
    }

    [HttpGet("requests")]
    public async Task<ActionResult<List<FriendRequestDto>>> GetRequests()
    {
        return Ok(await _friendService.GetPendingRequestsAsync(GetUserId()));
    }

    [HttpPost("request/{userId}")]
    public async Task<IActionResult> SendRequest(int userId)
    {
        try
        {
            await _friendService.SendRequestAsync(GetUserId(), userId);
            return Ok(new { message = "Friend request sent." });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("accept/{friendshipId}")]
    public async Task<IActionResult> Accept(int friendshipId)
    {
        try
        {
            await _friendService.AcceptRequestAsync(friendshipId, GetUserId());
            return Ok(new { message = "Friend request accepted." });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPost("decline/{friendshipId}")]
    public async Task<IActionResult> Decline(int friendshipId)
    {
        try
        {
            await _friendService.DeclineRequestAsync(friendshipId, GetUserId());
            return Ok(new { message = "Friend request declined." });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpDelete("{friendshipId}")]
    public async Task<IActionResult> Remove(int friendshipId)
    {
        try
        {
            await _friendService.RemoveFriendAsync(friendshipId, GetUserId());
            return Ok(new { message = "Friend removed." });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
    }

    [HttpGet("search")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<List<UserSearchResultDto>>> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return BadRequest(new { message = "Query must be at least 2 characters." });

        if (q.Length > 50) q = q[..50];

        return Ok(await _friendService.SearchUsersAsync(q, GetUserId()));
    }
}
