using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>In-App-Benachrichtigungen des eingeloggten Users (Navbar-Glocke).</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationController : BaseApiController
{
    private readonly NotificationService _notifications;
    private readonly PushNotificationService _push;
    private readonly PermissionResolver? _permissions;

    public NotificationController(NotificationService notifications, PushNotificationService push,
        PermissionResolver? permissions = null)
    {
        _notifications = notifications;
        _push = push;
        _permissions = permissions;
    }

    /// <summary>Letzte Benachrichtigungen (neueste zuerst). <paramref name="unseenOnly"/>=true liefert
    /// nur ungelesene — die Glocke zeigt nur diese, gelesene bleiben über „Alle anzeigen" (History) sichtbar.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int take = 20, [FromQuery] bool unseenOnly = false)
        => Ok(await _notifications.GetForUserAsync(GetUserId(), take, unseenOnly));

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

    // ----- Web-Push -------------------------------------------------------

    /// <summary>Push-Status des Users: VAPID-Public-Key (null = serverseitig nicht konfiguriert) +
    /// die aktuell aktivierten Bereiche. Standardmäßig ist Push aus (leere Liste).</summary>
    [HttpGet("push/config")]
    public async Task<IActionResult> PushConfig()
        => Ok(new PushConfigDto(_push.PublicKey, await _push.GetEnabledCategoriesAsync(GetUserId())));

    /// <summary>Registriert/aktualisiert eine Browser-Push-Subscription dieses Users. Nicht während einer Impersonation:
    /// der Server hängt eine bekannte Subscription auf den neuen Nutzer um — der Admin-Browser bekäme danach die Pushes
    /// des Zielkontos statt der eigenen.</summary>
    [HttpPost("push/subscribe")]
    [DenyWhileImpersonating]
    public async Task<IActionResult> PushSubscribe([FromBody] PushSubscribeInputDto dto)
    {
        try { await _push.SubscribeAsync(GetUserId(), dto.Endpoint ?? "", dto.P256dh ?? "", dto.Auth ?? ""); return NoContent(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Meldet eine Browser-Push-Subscription wieder ab (idempotent).</summary>
    [HttpPost("push/unsubscribe")]
    public async Task<IActionResult> PushUnsubscribe([FromBody] PushUnsubscribeInputDto dto)
    {
        await _push.UnsubscribeAsync(GetUserId(), dto.Endpoint ?? "");
        return NoContent();
    }

    /// <summary>Setzt die aktivierten Push-Bereiche (leer = Push aus). „admin" nur für die Nutzerverwaltung
    /// (<c>users.manage</c>, Admins immer) — der Bereich trägt nur „neue Registrierung", und diese Glocke geht seit F5-005
    /// an alle mit dem Recht. Antwortet mit den effektiv gespeicherten Keys. 400 bei ungültigem Bereich.</summary>
    [HttpPut("push/preferences")]
    public async Task<IActionResult> PushPreferences([FromBody] PushPreferencesInputDto dto)
    {
        var adminArea = await HasPermissionAsync(_permissions, Permissions.UsersManage);
        try { return Ok(new { categories = await _push.SetEnabledCategoriesAsync(GetUserId(), dto.Categories ?? new List<string>(), adminArea) }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
