using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/endless")]
public class EndlessController : BaseApiController
{
    private static readonly Regex SessionIdPattern = new(@"^[a-fA-F0-9\-]{1,36}$", RegexOptions.Compiled);
    private readonly EndlessProgressService _service;

    public EndlessController(EndlessProgressService service) => _service = service;

    // --- Authenticated ---

    [HttpGet("progress")]
    [Authorize]
    public async Task<ActionResult<EndlessSyncResponseDto>> GetProgress()
    {
        var data = await _service.GetSyncDataAsync(GetUserId());
        return Ok(data);
    }

    [HttpPut("progress")]
    [Authorize]
    public async Task<ActionResult<EndlessProgressDto>> SaveProgress([FromBody] SaveEndlessProgressDto dto)
    {
        var result = await _service.SaveProgressAsync(GetUserId(), dto);
        return Ok(result);
    }

    [HttpGet("history")]
    [Authorize]
    public async Task<ActionResult<EndlessHistoryResponseDto>> GetHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] bool? archived = null)
    {
        var result = await _service.GetSessionHistoryAsync(GetUserId(), page, pageSize, archived);
        return Ok(result);
    }

    [HttpPost("archive")]
    [Authorize]
    public async Task<ActionResult<object>> ArchiveSessions([FromBody] ArchiveSessionsDto dto)
    {
        if (dto.SessionIds.Count == 0)
            return BadRequest(new { message = "No session IDs provided." });
        if (dto.SessionIds.Count > 100)
            return BadRequest(new { message = "Maximum 100 sessions per request." });

        var count = await _service.ArchiveSessionsAsync(GetUserId(), dto.SessionIds, dto.Archive);
        return Ok(new { updated = count });
    }

    [HttpPost("sessions")]
    [Authorize]
    public async Task<ActionResult<EndlessSessionDto>> RecordSession([FromBody] RecordEndlessSessionDto dto)
    {
        var result = await _service.RecordSessionAsync(GetUserId(), dto);
        return Ok(result);
    }

    [HttpPost("sessions/bulk")]
    [Authorize]
    public async Task<ActionResult<object>> BulkImportSessions([FromBody] BulkImportSessionDto dto)
    {
        if (dto.Sessions.Count > 50)
            return BadRequest(new { message = "Maximum 50 sessions per import." });

        var count = await _service.BulkImportSessionsAsync(GetUserId(), dto.Sessions);
        return Ok(new { imported = count });
    }

    [HttpPost("claim-session")]
    [Authorize]
    public async Task<ActionResult<object>> ClaimSession([FromBody] ClaimEndlessSessionDto dto)
    {
        var transferred = await _service.ClaimSessionAsync(GetUserId(), dto.AnonymousSessionId);
        return Ok(new { transferred });
    }

    // --- Anonymous ---

    [HttpGet("progress/anonymous")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<EndlessSyncResponseDto>> GetAnonymousProgress([FromQuery] string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || !SessionIdPattern.IsMatch(sessionId))
            return BadRequest(new { message = "Invalid session ID." });

        var data = await _service.GetAnonymousSyncDataAsync(sessionId);
        return Ok(data);
    }

    [HttpPut("progress/anonymous")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<EndlessProgressDto>> SaveAnonymousProgress([FromBody] SaveAnonymousProgressDto dto)
    {
        var result = await _service.SaveAnonymousProgressAsync(dto.SessionId, dto);
        return Ok(result);
    }

    [HttpPost("sessions/anonymous")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<EndlessSessionDto>> RecordAnonymousSession([FromBody] RecordAnonymousSessionDto dto)
    {
        var result = await _service.RecordAnonymousSessionAsync(dto.SessionId, dto);
        return Ok(result);
    }

    [HttpPost("sessions/bulk/anonymous")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<object>> BulkImportAnonymousSessions([FromBody] BulkImportAnonymousSessionDto dto)
    {
        if (dto.Sessions.Count > 50)
            return BadRequest(new { message = "Maximum 50 sessions per import." });

        var count = await _service.BulkImportAnonymousSessionsAsync(dto.SessionId, dto.Sessions);
        return Ok(new { imported = count });
    }
}
