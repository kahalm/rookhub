using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs.EngineBroker;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Controllers.EngineBroker;

/// <summary>
/// Admin-Seite „Engines" (0.946.0, Admin → Reiter „Engines"): alle registrierten Engines aller Konten, Hintergrund-Zuordnung,
/// Warteschlange, laufende Aufträge je Engine und Durchsatz — NUR lesend (der Pause-Schalter bleibt im Reiter
/// „Hintergrundjobs"). Nur Admins (live aus dem Admin-Flag): die Seite zeigt Engines und Aufträge fremder Konten.
/// </summary>
[ApiController]
[Route("api/admin/engines")]
[HasPermission(Permissions.AdminOnly)]
public class AdminEnginesController(EngineOverviewService overview) : BaseApiController
{
    /// <summary>Übersicht (bis zu <see cref="EngineOverviewService.CacheFor"/> zwischengespeichert).</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<EngineOverviewDto>> Overview(CancellationToken ct)
        => Ok(await overview.GetAsync(ct));
}
