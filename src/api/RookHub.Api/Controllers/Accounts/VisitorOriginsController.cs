using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs.Accounts;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Controllers.Accounts;

/// <summary>
/// Besucher-Herkunft: eindeutige Benutzer je Land für die Weltkarte (<c>/visitors</c>). Jedes angemeldete Konto sieht
/// die Länderzahlen über alles Erfasste und alle Oberflächen; Zeitraum und Oberfläche wählt NUR der Admin — die
/// Parameter eines anderen Kontos werden hier verworfen, nicht bloß in der Oberfläche versteckt. Ausgeliefert werden
/// ausschließlich Zähler je Land.
/// </summary>
[ApiController]
[Route("api/visitor-origins")]
[Authorize]
public class VisitorOriginsController : BaseApiController
{
    private readonly VisitorOriginService _service;

    public VisitorOriginsController(VisitorOriginService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<VisitorOriginsDto>> Get([FromQuery] int? days, [FromQuery] string? surface, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!IsAdmin) return Ok(await _service.GetAsync(null, null, withHits: false, today, ct));

        if (days is int d && !VisitorOriginService.AllowedDays.Contains(d))
            return BadRequest(new { message = "Unknown period." });
        surface = string.IsNullOrWhiteSpace(surface) ? null : surface.Trim().ToLowerInvariant();
        if (surface is not null && !VisitorOriginRecorder.Surfaces.Contains(surface))
            return BadRequest(new { message = "Unknown surface." });
        return Ok(await _service.GetAsync(days, surface, withHits: true, today, ct));
    }
}
