using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Die Kinderseite KidHub (kidhub.oberschmid.homes): Stufen-Leiter aus besonders einfachen Lichess-Puzzles
/// und die fuer Kinder freigegebenen Kurse. Alles ohne Anmeldung — ein Kind soll die Seite oeffnen und
/// loslegen; der Fortschritt bleibt auf dem Geraet.
/// </summary>
[ApiController]
[Route("api/kids")]
public class KidsController : BaseApiController
{
    private readonly KidsPuzzleService _service;
    private readonly CourseCommentLocalizer? _localizer;

    public KidsController(KidsPuzzleService service, CourseCommentLocalizer? localizer = null)
    {
        _service = service;
        _localizer = localizer;
    }

    /// <summary>Alle Stufen in Reihenfolge (leer, solange die Leiter noch nicht aufgebaut ist).</summary>
    [HttpGet("levels")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<List<KidsLevelDto>>> GetLevels(CancellationToken ct) =>
        Ok(await _service.GetLevelsAsync(ct));

    /// <summary>Eine Stufe mit allen Aufgaben.</summary>
    [HttpGet("levels/{level:int}")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<KidsLevelDetailDto>> GetLevel(int level, CancellationToken ct)
    {
        var detail = await _service.GetLevelAsync(level, ct);
        return detail is null ? NotFound(new { message = "Level not found." }) : Ok(detail);
    }

    /// <summary>Die fuer Kinder freigegebenen Kurse.</summary>
    [HttpGet("courses")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<List<KidsCourseDto>>> GetCourses(CancellationToken ct) =>
        Ok(await _service.GetCoursesAsync(ct));

    /// <summary>Die Aufgaben eines Kinderkurses am Stueck; <c>?lang=</c> liefert die Kommentare
    /// uebersetzt, wo es aktuelle Uebersetzungen gibt.</summary>
    [HttpGet("courses/{bookId:int}/puzzles")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<List<BookPuzzleDto>>> GetCoursePuzzles(int bookId, [FromQuery] string? lang,
        CancellationToken ct)
    {
        try
        {
            var lines = await _service.GetCoursePuzzlesAsync(bookId, ct);
            if (_localizer is not null) await _localizer.ApplyAsync(lines, lang, ct);
            return Ok(lines);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Leiter sofort neu aufbauen — nach einem Neuimport der Standard-Puzzles (der die Leiter
    /// ueber den Fremdschluessel mit leert) oder um eine geaenderte Auswahl zu sehen.</summary>
    [HttpPost("/api/admin/kids/rebuild")]
    [HasPermission(Permissions.PuzzlesManage)]
    public async Task<ActionResult<KidsRebuildResultDto>> Rebuild(CancellationToken ct) =>
        Ok(await _service.RebuildAsync(ct));
}
