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

    public FriendController(FriendService friendService) => _friendService = friendService;

    [HttpGet]
    public async Task<ActionResult<List<FriendDto>>> GetFriends()
    {
        return Ok(await _friendService.GetFriendsAsync(GetUserId()));
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
