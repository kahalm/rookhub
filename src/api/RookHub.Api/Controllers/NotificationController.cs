using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>In-App-Benachrichtigungen des eingeloggten Users (Navbar-Glocke).</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationController : BaseApiController
{
    private readonly NotificationService _notifications;

    public NotificationController(NotificationService notifications) => _notifications = notifications;

    /// <summary>Letzte Benachrichtigungen (neueste zuerst).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int take = 20)
        => Ok(await _notifications.GetForUserAsync(GetUserId(), take));

    /// <summary>Vollständige History (paginiert, neueste zuerst) + Gesamtzahl.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 30)
        => Ok(await _notifications.GetHistoryAsync(GetUserId(), page, pageSize));

    /// <summary>Anzahl ungelesener Benachrichtigungen (Badge/„!").</summary>
    [HttpGet("count")]
    public async Task<IActionResult> GetCount()
        => Ok(new NotificationCountDto(await _notifications.CountUnseenAsync(GetUserId())));

    /// <summary>Markiert alle als gesehen (beim Öffnen der Glocke).</summary>
    [HttpPost("seen")]
    public async Task<IActionResult> MarkSeen()
    {
        await _notifications.MarkAllSeenAsync(GetUserId());
        return NoContent();
    }

    /// <summary>Markiert EINE Benachrichtigung als gesehen (Klick darauf).</summary>
    [HttpPost("{id:int}/seen")]
    public async Task<IActionResult> MarkOneSeen(int id)
    {
        await _notifications.MarkSeenAsync(GetUserId(), id);
        return NoContent();
    }
}
