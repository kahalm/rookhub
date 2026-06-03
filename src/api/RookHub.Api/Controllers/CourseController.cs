using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// „Kurse" = importierte Bücher, die ein User puzzleweise durcharbeitet. Pro Buch gibt es einen
/// (geteilten) Fortschritt = gelöste Puzzles / Gesamtzahl; der Modus (sequential/random) bestimmt
/// nur die Reihenfolge. Fortschritt ist user-bezogen und liegt komplett in der DB.
/// Sichtbarkeit: Admins sehen alle Bücher; Nicht-Admins nur Bücher, die einer ihrer Gruppen
/// per <see cref="Models.BookGroupAccess"/> freigegeben wurden. Die Logik liegt im
/// <see cref="CourseService"/>; kein Zugriff → 404.
/// </summary>
[ApiController]
[Route("api/courses")]
[Authorize]
public class CourseController : BaseApiController
{
    private readonly CourseService _service;

    public CourseController(CourseService service) => _service = service;

    private bool IsAdmin => User.IsInRole("Admin");

    /// <summary>Alle Puzzles eines (zugänglichen) Buchs am Stück — für das Offline-Speichern.</summary>
    [HttpGet("{bookId}/puzzles")]
    public async Task<ActionResult<List<BookPuzzleDto>>> GetAllPuzzles(int bookId)
    {
        try { return Ok(await _service.GetAllPuzzlesAsync(GetUserId(), bookId, IsAdmin)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Sichtbare Bücher als Kurse inkl. Fortschritt des aktuellen Users (Admin: alle).</summary>
    [HttpGet]
    public async Task<IActionResult> GetCourses()
        => Ok(await _service.GetCoursesAsync(GetUserId(), IsAdmin));

    /// <summary>Hat der User Zugriff auf mindestens einen Kurs? (Basis für die Menü-Sichtbarkeit.)</summary>
    [HttpGet("access")]
    public async Task<IActionResult> HasAnyAccess()
        => Ok(new { hasAccess = await _service.HasAnyAccessAsync(GetUserId(), IsAdmin) });

    /// <summary>Nächstes ungelöstes Puzzle des Kurses (sequential/random); aktualisiert den letzten Modus.</summary>
    [HttpGet("{bookId}/next")]
    public async Task<IActionResult> GetNext(
        int bookId,
        [FromQuery] string mode = "sequential",
        [FromQuery] int? after = null,
        [FromQuery] int? exclude = null)
    {
        try { return Ok(await _service.GetNextAsync(GetUserId(), bookId, mode, after, exclude, IsAdmin)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Zeichnet einen Lösungsversuch auf. Bei Solved wird das Puzzle (idempotent) als gelöst markiert.</summary>
    [HttpPost("{bookId}/results")]
    public async Task<IActionResult> RecordResult(int bookId, [FromBody] RecordCourseResultDto dto)
    {
        try { return Ok(await _service.RecordResultAsync(GetUserId(), bookId, dto, IsAdmin)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Setzt den Fortschritt eines Kurses zurück (löscht alle gelösten Markierungen).</summary>
    [HttpPost("{bookId}/reset")]
    public async Task<IActionResult> Reset(int bookId)
    {
        try { return Ok(await _service.ResetAsync(GetUserId(), bookId, IsAdmin)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}
