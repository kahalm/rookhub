using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>Direktnachrichten-Thread des eingeloggten Users mit dem Admin-Team. Der Admin startet die
/// Konversation; der User kann hier antworten (sobald ein Thread existiert) und sie als gelesen markieren.</summary>
[ApiController]
[Route("api/messages")]
[Authorize]
public class MessageController : BaseApiController
{
    private readonly AdminMessageService _messages;

    public MessageController(AdminMessageService messages) => _messages = messages;

    /// <summary>Eigener Thread (chronologisch, älteste zuerst). Leer, wenn der Admin nie geschrieben hat.</summary>
    [HttpGet]
    public async Task<IActionResult> GetThread()
        => Ok(await _messages.GetUserThreadAsync(GetUserId()));

    /// <summary>Anzahl ungelesener Admin-Nachrichten (Navbar-Badge).</summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
        => Ok(new MessageUnreadCountDto(await _messages.CountUnreadForUserAsync(GetUserId())));

    /// <summary>Antwort des Users im eigenen Thread (400, wenn noch keine Konversation existiert).</summary>
    [HttpPost("reply")]
    public async Task<IActionResult> Reply([FromBody] SendMessageDto dto)
    {
        try { return Ok(await _messages.ReplyFromUserAsync(GetUserId(), dto.Body)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Markiert die Admin-Nachrichten im eigenen Thread als gelesen.</summary>
    [HttpPost("seen")]
    public async Task<IActionResult> MarkSeen()
    {
        await _messages.MarkSeenByUserAsync(GetUserId());
        return NoContent();
    }
}
