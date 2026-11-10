using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Authorization;
using RookHub.Api.Data;
using RookHub.Api.DTOs.EngineBroker;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Controllers.EngineBroker;

/// <summary>
/// Admin-Seite „Engines" (0.946.0, Admin → Reiter „Engines"): alle registrierten Engines aller Konten, Hintergrund-Zuordnung,
/// Warteschlange, laufende Aufträge je Engine und Durchsatz. Seit 0.951.0 pflegt der Admin hier auch die Hintergrund-Zuordnung
/// (je Engine und Konto an/aus, <see cref="SetBackground"/>); der Pause-Schalter bleibt im Reiter „Hintergrundjobs".
/// Nur Admins (live aus dem Admin-Flag): die Seite zeigt Engines und Aufträge fremder Konten.
/// </summary>
[ApiController]
[Route("api/admin/engines")]
[HasPermission(Permissions.AdminOnly)]
public class AdminEnginesController(
    EngineOverviewService overview,
    AppDbContext db,
    EncryptionService encryption,
    EngineRegistry registry,
    ILogger<AdminEnginesController> logger) : BaseApiController
{
    /// <summary>Übersicht (bis zu <see cref="EngineOverviewService.CacheFor"/> zwischengespeichert).</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<EngineOverviewDto>> Overview(CancellationToken ct)
        => Ok(await overview.GetAsync(ct));

    /// <summary>
    /// Die Engine in die Hintergrund-Liste des Kontos <c>userId</c> aufnehmen bzw. daraus entfernen — die Liste dieses
    /// Kontos (in der Regel des Besitzers), nicht die des Admins. Regeln wie im Profil
    /// (<see cref="EngineSettingsService.SetBackgroundMembershipAsync"/>): idempotent (200, <c>changed: false</c>), Deckel
    /// → 400 <c>background_list_full</c>, Engine gehört dem Konto nicht → 404 <c>engine_not_found</c>, Konto unbekannt →
    /// 404 <c>user_not_found</c>, Lichess nicht erreichbar → 502. Danach ist der Übersichts-Cache verworfen.
    /// </summary>
    [HttpPut("{engineId}/background")]
    public async Task<IActionResult> SetBackground(string engineId, [FromBody] SetEngineBackgroundRequest request,
        CancellationToken ct)
    {
        if (!await db.AppUsers.AsNoTracking().AnyAsync(u => u.Id == request.UserId, ct))
            return NotFound(ApiErrorResponses.Body("User not found", ApiErrorCodes.UserNotFound));

        var settings = new EngineSettingsService(db, encryption, registry);
        BackgroundMembershipResult? result;
        EngineSettingsRejection? rejection;
        try
        {
            (result, rejection) = await settings.SetBackgroundMembershipAsync(request.UserId, engineId, request.Enabled, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "AdminEngines: Lichess nicht erreichbar beim Pruefen von {EngineId} fuer user={UserId}",
                engineId, request.UserId);
            return StatusCode(502, new { message = "Lichess unreachable" });
        }
        if (rejection is not null)
        {
            var body = ApiErrorResponses.Body(rejection.Message, rejection.Code);
            return rejection.NotFound ? NotFound(body) : BadRequest(body);
        }

        var id = engineId.Trim();
        if (result!.Changed)
        {
            overview.Invalidate();
            logger.LogInformation(
                "AdminEngines: Hintergrund {Action} engine={EngineId} konto={UserId} von admin={AdminId} ({AdminName})",
                request.Enabled ? "an" : "aus", id, request.UserId, GetUserId(), User.FindFirstValue(ClaimTypes.Name));
        }
        return Ok(new SetEngineBackgroundResponse(id, request.UserId, request.Enabled, result.Changed, result.Ids));
    }
}
