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
}
