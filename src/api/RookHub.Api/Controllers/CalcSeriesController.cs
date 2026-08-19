using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Kalkulations-Serien (eigener Bereich, Phase 1): terminierte Ausgaben eines Kalkulationsbuchs mit Video.
/// Verwaltung (Anlegen/Ändern/Löschen) nur durch Buch-Besitzer oder Admin; die Betrachter-Liste
/// (<c>GET {bookId}</c>) liefert nur bereits freigegebene Ausgaben. Das Sichtbarkeits-Gating der
/// Stellungen selbst passiert in den Kalkulations-Endpoints (<see cref="CalculationController"/>).
/// </summary>
[ApiController]
[Route("api/calc-editions")]
[Authorize]
public class CalcSeriesController : BaseApiController
{
    private readonly CalcEditionService _service;
    public CalcSeriesController(CalcEditionService service) => _service = service;

    /// <summary>Betrachter: freigegebene Ausgaben eines Buchs inkl. Video (keine Entwürfe).</summary>
    [HttpGet("{bookId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<CalcEditionDto>>> ListVisible(int bookId, CancellationToken ct)
        => Ok(await _service.ListVisibleAsync(bookId, ct));

    /// <summary>Verwaltung: ALLE Ausgaben (inkl. Entwürfe). Nur Besitzer/Admin.</summary>
    [HttpGet("{bookId:int}/manage")]
    public async Task<ActionResult<List<CalcEditionDto>>> ListManage(int bookId, CancellationToken ct)
    {
        if (!await _service.CanManageAsync(GetUserId(), bookId, IsAdmin, ct)) return Forbid();
        return Ok(await _service.ListAsync(bookId, ct));
    }

    /// <summary>Ausgabe anlegen/ändern (Upsert je Kapitel). Nur Besitzer/Admin.</summary>
    [HttpPut("{bookId:int}")]
    public async Task<ActionResult<CalcEditionDto>> Upsert(int bookId, [FromBody] CalcEditionInputDto dto, CancellationToken ct)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Chapter)) return BadRequest(new { message = "Chapter required." });
        if (!await _service.CanManageAsync(GetUserId(), bookId, IsAdmin, ct)) return Forbid();
        return Ok(await _service.UpsertAsync(bookId, dto, ct));
    }

    /// <summary>Ausgabe löschen. Nur Besitzer/Admin.</summary>
    [HttpDelete("{bookId:int}/{editionId:int}")]
    public async Task<IActionResult> Delete(int bookId, int editionId, CancellationToken ct)
    {
        if (!await _service.CanManageAsync(GetUserId(), bookId, IsAdmin, ct)) return Forbid();
        return await _service.DeleteAsync(bookId, editionId, ct) ? NoContent() : NotFound();
    }
}
