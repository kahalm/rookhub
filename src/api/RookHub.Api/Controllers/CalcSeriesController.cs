using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Kalkulations-Serien (eigener Bereich): terminierte Ausgaben eines Kalkulationsbuchs mit Video (Phase 1)
/// und der private Verteiler mit Tester-Häkchen (Phase 2).
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

    // ===== Privater Verteiler (Phase 2) — nur Besitzer/Admin =================

    /// <summary>Mitglieder des Verteilers (inkl. Tester-Häkchen). Nur Besitzer/Admin.</summary>
    [HttpGet("{bookId:int}/members")]
    public async Task<ActionResult<List<CalcSeriesMemberDto>>> ListMembers(int bookId, CancellationToken ct)
    {
        if (!await _service.CanManageAsync(GetUserId(), bookId, IsAdmin, ct)) return Forbid();
        return Ok(await _service.ListMembersAsync(bookId, ct));
    }

    /// <summary>Mitglied hinzufügen/ändern (per Benutzername). Nur Besitzer/Admin.
    /// 404, wenn es keinen Nutzer mit diesem Namen gibt.</summary>
    [HttpPut("{bookId:int}/members")]
    public async Task<ActionResult<CalcSeriesMemberDto>> UpsertMember(int bookId, [FromBody] CalcSeriesMemberInputDto dto, CancellationToken ct)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Username)) return BadRequest(new { message = "Username required." });
        if (!await _service.CanManageAsync(GetUserId(), bookId, IsAdmin, ct)) return Forbid();
        var member = await _service.UpsertMemberAsync(bookId, dto.Username, dto.IsTester, ct);
        return member is null ? NotFound(new { message = "User not found." }) : Ok(member);
    }

    /// <summary>Mitglied entfernen. Nur Besitzer/Admin.</summary>
    [HttpDelete("{bookId:int}/members/{userId:int}")]
    public async Task<IActionResult> RemoveMember(int bookId, int userId, CancellationToken ct)
    {
        if (!await _service.CanManageAsync(GetUserId(), bookId, IsAdmin, ct)) return Forbid();
        return await _service.RemoveMemberAsync(bookId, userId, ct) ? NoContent() : NotFound();
    }

    /// <summary>„Gesehen"-Übersicht (Phase 3): welches Mitglied welche Ausgabe wann geöffnet hat.
    /// Nur Besitzer/Admin.</summary>
    [HttpGet("{bookId:int}/views")]
    public async Task<ActionResult<List<CalcEditionViewDto>>> ListViews(int bookId, CancellationToken ct)
    {
        if (!await _service.CanManageAsync(GetUserId(), bookId, IsAdmin, ct)) return Forbid();
        return Ok(await _service.ListViewsAsync(bookId, ct));
    }
}
