using Microsoft.AspNetCore.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>Admin-Seite der Direktnachrichten: alle Threads übersehen, einen Thread öffnen, einem User
/// schreiben/antworten und User-Antworten als gelesen markieren.</summary>
[ApiController]
[Route("api/admin/messages")]
[HasPermission(Permissions.MessagesAdmin)]
public class AdminMessageController : BaseApiController
{
    private readonly AdminMessageService _messages;

    public AdminMessageController(AdminMessageService messages) => _messages = messages;

    /// <summary>Alle Konversationen (ein Eintrag je User) inkl. letzter Nachricht + ungelesener User-Antworten.</summary>
    [HttpGet("threads")]
    public async Task<IActionResult> GetThreads()
        => Ok(await _messages.GetThreadsAsync());

    /// <summary>Anzahl ungelesener User-Antworten über alle Threads (Admin-Tab-Badge).</summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
        => Ok(new MessageUnreadCountDto(await _messages.CountUnreadForAdminAsync()));

    /// <summary>Vollständiger Thread mit einem User (chronologisch).</summary>
    [HttpGet("threads/{userId:int}")]
    public async Task<IActionResult> GetThread(int userId)
        => Ok(await _messages.GetThreadAsync(userId));

    /// <summary>Admin schickt/antwortet dem User (legt den Thread bei der ersten Nachricht an).</summary>
    [HttpPost("threads/{userId:int}")]
    public async Task<IActionResult> Send(int userId, [FromBody] SendMessageDto dto)
    {
        try { return Ok(await _messages.SendFromAdminAsync(GetUserId(), userId, dto.Body)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>Markiert die User-Antworten eines Threads als vom Admin gelesen.</summary>
    [HttpPost("threads/{userId:int}/seen")]
    public async Task<IActionResult> MarkSeen(int userId)
    {
        await _messages.MarkSeenByAdminAsync(userId);
        return NoContent();
    }

    /// <summary>Übernimmt den Thread (Zuweisung an den aufrufenden Admin).</summary>
    [HttpPost("threads/{userId:int}/claim")]
    public async Task<IActionResult> Claim(int userId)
    {
        await _messages.ClaimThreadAsync(GetUserId(), userId);
        return NoContent();
    }

    /// <summary>Gibt den Thread wieder frei (keine Zuweisung).</summary>
    [HttpPost("threads/{userId:int}/release")]
    public async Task<IActionResult> Release(int userId)
    {
        await _messages.ReleaseThreadAsync(userId);
        return NoContent();
    }
}
