using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Data;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Kalkulations-Serien (eigener Bereich): terminierte Ausgaben eines Kalkulationsbuchs mit Video (Phase 1)
/// und der private Verteiler mit Tester-Häkchen (Phase 2).
/// Verwaltung (Anlegen/Ändern/Löschen) nur durch Buch-Besitzer oder Admin (ein Mitglied darf sich
/// selbst aus dem Verteiler austragen); die Betrachter-Liste
/// (<c>GET {bookId}</c>) liefert nur bereits freigegebene Ausgaben. Das Sichtbarkeits-Gating der
/// Stellungen selbst (Woche mit Ausgabe versteckt bis <c>PublishAt</c>, für Tester ab
/// <c>TesterPreviewAt</c>; Besitzer/Admin sehen alles) liegt zentral in
/// <see cref="CalcVisibility.HiddenChaptersAsync"/> und greift in den Kalkulations-Endpoints
/// (<see cref="CalculationController"/>) UND auf der Kurs-Detailseite (<see cref="CourseController"/>:
/// <c>GET api/courses/{bookId}</c> ohne gesperrte Wochen in Kapitelliste und Zählern,
/// <c>GET api/courses/{bookId}/lines?chapter=</c> liefert für eine gesperrte Woche eine leere Liste wie
/// für ein unbekanntes Kapitel).
/// Fehlerfälle werfen die Dienste als Domänen-Ausnahme; Verwalten prüft die eine Besitzer-oder-Admin-Regel
/// (<see cref="CourseAccess.LoadManageableAsync"/>: 404 nicht lesbar, 403 nicht berechtigt). Jede
/// Fehlerantwort trägt <c>{ message }</c> (Codereview A7-011).
/// </summary>
[ApiController]
[Route("api/calc-editions")]
[Authorize]
public class CalcSeriesController : BaseApiController
{
    private readonly CalcEditionService _service;
    private readonly AppDbContext _db;
    public CalcSeriesController(CalcEditionService service, AppDbContext db)
    {
        _service = service;
        _db = db;
    }

    /// <summary>Betrachter: freigegebene Ausgaben eines Buchs inkl. Video (keine Entwürfe).
    ///
    /// GEGATET wie der Kurs selbst: anonym nur bei einem öffentlichen Buch, eingeloggt über
    /// <see cref="CourseAccess.CanAccessAsync"/> (das kennt Besitzer, Freigaben, Gruppen UND den
    /// privaten Verteiler der Serie). Ohne diese Prüfung war der eigentliche Inhalt einer PRIVATEN
    /// Serie — die Video-URLs der freigegebenen Wochen — per Buch-Id-Iteration ohne Login lesbar,
    /// und das „privat schalten" über <c>IsPublic=false</c> griff hier nicht.</summary>
    [HttpGet("{bookId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<CalcEditionDto>>> ListVisible(int bookId, CancellationToken ct)
    {
        var userId = GetUserIdOrNull();
        var allowed = userId is null
            ? await _db.Books.AnyAsync(b => b.Id == bookId && b.IsPublic, ct)
            : await CourseAccess.CanAccessAsync(_db, userId.Value, bookId, IsAdmin, ct);
        if (!allowed) return NotFound(new { message = "Book not found." });
        return Ok(await _service.ListVisibleAsync(bookId, ct));
    }

    /// <summary>Verwaltung: ALLE Ausgaben (inkl. Entwürfe). Nur Besitzer/Admin.</summary>
    [HttpGet("{bookId:int}/manage")]
    public async Task<ActionResult<List<CalcEditionDto>>> ListManage(int bookId, CancellationToken ct)
    {
        await _service.EnsureCanManageAsync(GetUserId(), bookId, IsAdmin, ct);
        return Ok(await _service.ListAsync(bookId, ct));
    }

    /// <summary>Ausgabe anlegen/ändern (Upsert je Kapitel). Nur Besitzer/Admin.
    /// 400, wenn eine neue Ausgabe den Deckel je Buch überschreiten würde.</summary>
    [HttpPut("{bookId:int}")]
    public async Task<ActionResult<CalcEditionDto>> Upsert(int bookId, [FromBody] CalcEditionInputDto dto, CancellationToken ct)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Chapter)) return BadRequest(new { message = "Chapter required." });
        await _service.EnsureCanManageAsync(GetUserId(), bookId, IsAdmin, ct);
        return Ok(await _service.UpsertAsync(bookId, dto, ct));
    }

    /// <summary>Ausgabe löschen. Nur Besitzer/Admin.</summary>
    [HttpDelete("{bookId:int}/{editionId:int}")]
    public async Task<IActionResult> Delete(int bookId, int editionId, CancellationToken ct)
    {
        await _service.EnsureCanManageAsync(GetUserId(), bookId, IsAdmin, ct);
        return await _service.DeleteAsync(bookId, editionId, ct) ? NoContent() : NotFound(new { message = "Edition not found." });
    }

    // ===== Privater Verteiler (Phase 2) — Besitzer/Admin; Austragen auch selbst =====

    /// <summary>Mitglieder des Verteilers (inkl. Tester-Häkchen). Nur Besitzer/Admin.</summary>
    [HttpGet("{bookId:int}/members")]
    public async Task<ActionResult<List<CalcSeriesMemberDto>>> ListMembers(int bookId, CancellationToken ct)
    {
        await _service.EnsureCanManageAsync(GetUserId(), bookId, IsAdmin, ct);
        return Ok(await _service.ListMembersAsync(bookId, ct));
    }

    /// <summary>Mitglied hinzufügen/ändern (per Benutzername). Nur Besitzer/Admin; neu eintragen lassen sich
    /// nur Freunde des Besitzers (Admins: jeder), siehe <see cref="CalcEditionService.UpsertMemberAsync"/>.
    /// 404 — gleiche Antwort für „gibt es nicht" und „kein Freund" (kein Benutzernamen-Orakel);
    /// 400 bei einem Nicht-Kalkulationsbuch oder vollem Verteiler.</summary>
    [HttpPut("{bookId:int}/members")]
    public async Task<ActionResult<CalcSeriesMemberDto>> UpsertMember(int bookId, [FromBody] CalcSeriesMemberInputDto dto, CancellationToken ct)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Username)) return BadRequest(new { message = "Username required." });
        await _service.EnsureCanManageAsync(GetUserId(), bookId, IsAdmin, ct);
        var member = await _service.UpsertMemberAsync(bookId, dto.Username, dto.IsTester, IsAdmin, ct);
        return member is null ? NotFound(new { message = "User not found or not a friend." }) : Ok(member);
    }

    /// <summary>Mitglied entfernen. Besitzer/Admin — oder das Mitglied sich selbst (Austragen: wer in einen
    /// Verteiler eingetragen wurde, muss wieder herauskommen, ohne den Besitzer zu fragen).</summary>
    [HttpDelete("{bookId:int}/members/{userId:int}")]
    public async Task<IActionResult> RemoveMember(int bookId, int userId, CancellationToken ct)
    {
        if (userId != GetUserId()) await _service.EnsureCanManageAsync(GetUserId(), bookId, IsAdmin, ct);
        return await _service.RemoveMemberAsync(bookId, userId, ct) ? NoContent() : NotFound(new { message = "Member not found." });
    }

    /// <summary>„Gesehen"-Übersicht (Phase 3): welches Mitglied welche Ausgabe wann geöffnet hat.
    /// Nur Besitzer/Admin.</summary>
    [HttpGet("{bookId:int}/views")]
    public async Task<ActionResult<List<CalcEditionViewDto>>> ListViews(int bookId, CancellationToken ct)
    {
        await _service.EnsureCanManageAsync(GetUserId(), bookId, IsAdmin, ct);
        return Ok(await _service.ListViewsAsync(bookId, ct));
    }
}
