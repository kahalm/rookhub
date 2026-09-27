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
/// und die fuer Kinder freigegebenen Kurse. Spielen geht ohne Anmeldung — ein Kind soll die Seite oeffnen
/// und loslegen; der Fortschritt liegt dann auf dem Geraet. Angemeldet gleicht KidHub ihn mit dem Konto
/// ab (<c>/api/kids/progress</c>).
/// </summary>
[ApiController]
[Route("api/kids")]
public class KidsController : BaseApiController
{
    private readonly KidsPuzzleService _service;
    private readonly CourseCommentLocalizer? _localizer;
    private readonly IpCountryService? _ipCountry;
    private readonly KidsProgressService? _progress;
    private readonly KidsEndlessService? _endless;

    public KidsController(KidsPuzzleService service, CourseCommentLocalizer? localizer = null,
        IpCountryService? ipCountry = null, KidsProgressService? progress = null, KidsEndlessService? endless = null)
    {
        _service = service;
        _localizer = localizer;
        _ipCountry = ipCountry;
        _progress = progress;
        _endless = endless;
    }

    /// <summary>Endlos-Modus: je Rating-Fenster ein kindgerechtes Puzzle (die Kurve rechnet KidHub selbst).</summary>
    [HttpPost("endless/batch")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<List<KidsEndlessPuzzleDto>>> GetEndlessBatch([FromBody] KidsEndlessBatchRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _endless!.BatchAsync(request, ct));
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Der Fortschritt im Konto (Stufen, Kurs-Linien).</summary>
    [HttpGet("progress")]
    [Authorize]
    public async Task<ActionResult<KidsProgressDto>> GetProgress(CancellationToken ct) =>
        Ok(await _progress!.GetAsync(GetUserId(), ct));

    /// <summary>Den Stand des Browsers mit dem Konto zusammenfuehren (<see cref="KidsProgressMerge"/>) —
    /// Antwort ist der gemeinsame Stand, den KidHub danach anzeigt.</summary>
    [HttpPut("progress")]
    [Authorize]
    public async Task<ActionResult<KidsProgressDto>> PutProgress([FromBody] KidsProgressDto body, CancellationToken ct)
    {
        try
        {
            return Ok(await _progress!.SyncAsync(GetUserId(), body, ct));
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>
    /// Sprache aus dem Land der Besucher-IP — KidHub fragt das nur, wenn die Browsersprache keine der
    /// Kindersprachen ist, und nimmt ohne Treffer Deutsch. Nachgeschlagen wird lokal
    /// (<see cref="IpCountryService"/>); die IP wird dafür weder weitergegeben noch gespeichert.
    /// </summary>
    [HttpGet("language-hint")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<KidsLanguageHintDto>> GetLanguageHint(CancellationToken ct)
    {
        var country = _ipCountry is null
            ? null
            : await _ipCountry.CountryOfAsync(HttpContext?.Connection.RemoteIpAddress, ct);
        return Ok(new KidsLanguageHintDto { Country = country, Language = KidsLanguageHint.ForCountry(country) });
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

    /// <summary>Die fuer Kinder freigegebenen Kurse, die gerade gezeigt werden (siehe
    /// <see cref="KidsPuzzleService"/>: erst, wenn sie in den geforderten Sprachen vorliegen); Titel je <c>?lang=</c>.</summary>
    [HttpGet("courses")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-puzzle")]
    public async Task<ActionResult<List<KidsCourseDto>>> GetCourses([FromQuery] string? lang, CancellationToken ct) =>
        Ok(await _service.GetCoursesAsync(lang, ct));

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
            var lines = await _service.GetCoursePuzzlesAsync(bookId, lang, ct);
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
