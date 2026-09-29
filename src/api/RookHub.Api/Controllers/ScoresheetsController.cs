using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// „Partieformular einlesen" (0.529.0): Foto hochladen, Stand abfragen. Das Ergebnis ist eine normale
/// gespeicherte Partie (Quelle <c>scoresheet</c>); Foto, Korrektur und Neuaufbereitung hängen an ihr
/// (<see cref="GameCorrectionController"/>).
/// </summary>
[ApiController]
[Route("api/scoresheets")]
[Authorize]
public class ScoresheetsController : BaseApiController
{
    private readonly ScoresheetScanService _service;
    private readonly ScoresheetScanSignal _signal;

    public ScoresheetsController(ScoresheetScanService service, ScoresheetScanSignal signal)
    {
        _service = service;
        _signal = signal;
    }

    /// <summary>Kann man gerade einlesen (API-Key da?), wie viel ist vom Tageskontingent übrig, welche Sprachen.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<ScoresheetStatusDto>> Status()
        => Ok(await _service.StatusAsync(GetUserId()));

    /// <summary>Die letzten Einlesungen (neueste zuerst, ohne Foto).</summary>
    [HttpGet]
    public async Task<ActionResult<List<ScoresheetScanDto>>> List([FromQuery] int take = 20)
        => Ok(await _service.ListAsync(GetUserId(), take));

    /// <summary>
    /// Foto hochladen (multipart: <c>file</c>, <c>language</c> = Code oder <c>auto</c>). Antwortet 202 mit der
    /// Einlesung; gelesen wird im Hintergrund. Absage 400 mit <c>reason</c> (<c>unsupportedImage</c>,
    /// <c>tooLarge</c>, <c>dailyLimit</c>, <c>tooManyOpen</c>, <c>invalidLanguage</c>) bzw. 503
    /// <c>notConfigured</c>, wenn kein API-Key hinterlegt ist.
    /// </summary>
    /// <summary>Foto hochladen — bei einem Formular über mehrere Blätter mehrere Teile <c>file</c> in Seitenreihenfolge
    /// (höchstens <see cref="ScoresheetScanService.MaxPages"/>, 0.600.0).</summary>
    [HttpPost]
    [RequestSizeLimit(ScoresheetScanService.MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ScoresheetScanService.MaxUploadRequestBytes)]
    public async Task<ActionResult<ScoresheetScanDto>> Upload([FromForm] List<IFormFile>? file, [FromForm] string? language,
        [FromForm] string? side)
    {
        var files = (file ?? new()).Where(f => f.Length > 0).ToList();
        if (files.Count == 0) return BadRequest(new { reason = "noFile", message = "No file." });
        if (files.Count > ScoresheetScanService.MaxPages)
            return BadRequest(new { reason = "tooManyPages", message = $"At most {ScoresheetScanService.MaxPages} photos." });
        if (files.Any(f => f.Length > ScoresheetScanService.MaxUploadBytes))
            return BadRequest(new { reason = "tooLarge", message = "File too large." });

        var pages = new List<ScoresheetUpload>();
        foreach (var f in files)
        {
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms);
            pages.Add(new ScoresheetUpload(ms.ToArray(), f.ContentType, f.FileName));
        }
        var (scan, reason) = await _service.CreateAsync(GetUserId(), pages, language, side);
        if (reason == "notConfigured")
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { reason, message = "Reading is not configured." });
        if (reason != null) return BadRequest(new { reason, message = "Photo not accepted." });
        _signal.Wake();
        return Accepted(scan);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ScoresheetScanDto>> Get(int id)
    {
        var scan = await _service.GetAsync(GetUserId(), id);
        return scan == null ? NotFound() : Ok(scan);
    }
}

/// <summary>
/// Korrigieren einer gespeicherten Partie und alles, was an ihrem Formular-Foto hängt. Eigene Klasse unter
/// derselben Route wie <see cref="GamesController"/>, damit dessen Abhängigkeiten nicht wachsen.
/// </summary>
[ApiController]
[Route("api/games")]
[Authorize]
public class GameCorrectionController : BaseApiController
{
    private readonly SavedGameService _games;
    private readonly ScoresheetScanService _scans;

    public GameCorrectionController(SavedGameService games, ScoresheetScanService scans)
    {
        _games = games;
        _scans = scans;
    }

    /// <summary>Partie korrigieren (Züge, Kommentare, Kopfdaten). 400 bei einem illegalen Zug; ändern sich die
    /// Züge, fällt die verknüpfte Analyse weg.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<SavedGameDetailDto>> Update(int id, [FromBody] GameUpdateDto dto)
    {
        if (dto is null) return BadRequest(new { message = "Body required." });
        try
        {
            var game = await _games.UpdateAsync(GetUserId(), id, dto);
            if (game == null) return NotFound();
            if (dto.ScoresheetPlies != null) await _scans.SaveEditStateAsync(GetUserId(), id, dto.ScoresheetPlies);
            return Ok(game);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { reason = "illegalMove", message = ex.Message });
        }
    }

    /// <summary>Das Formular-Foto der Partie (<c>?download=true</c> als Anhang). 404 ohne Foto.</summary>
    /// <summary>Das Formular-Foto; <c>page</c> = Seite eines mehrseitigen Formulars (ab 1).</summary>
    [HttpGet("{id:int}/photo")]
    public async Task<IActionResult> Photo(int id, [FromQuery] bool download = false, [FromQuery] int page = 1)
    {
        var photo = await _scans.PhotoForGameAsync(GetUserId(), id, page);
        if (photo is not { } p) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
        // Wie viele Seiten es gibt — der Foto-Dialog blättert damit, ohne die Einlesung abzufragen.
        Response.Headers["X-Page-Count"] = p.PageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return download ? File(p.Data, p.ContentType, p.FileName) : File(p.Data, p.ContentType);
    }

    /// <summary>Formular-Einträge + Stand je Halbzug (Korrekturseite). 404 ohne Einlesung.</summary>
    [HttpGet("{id:int}/scoresheet")]
    public async Task<ActionResult<ScoresheetEditStateDto>> Scoresheet(int id)
    {
        var state = await _scans.EditStateAsync(GetUserId(), id);
        return state == null ? NotFound() : Ok(state);
    }

    /// <summary>Den Rest ab einer festgelegten Stelle neu aufbereiten (ohne Modell-Aufruf).</summary>
    [HttpPost("{id:int}/scoresheet/resolve")]
    public async Task<ActionResult<ScoresheetResolveResultDto>> Resolve(int id, [FromBody] ScoresheetResolveRequestDto dto)
    {
        if (dto is null) return BadRequest(new { message = "Body required." });
        try
        {
            var result = await _scans.ResolveRestAsync(GetUserId(), id, dto.Prefix ?? new(), dto.WrittenFrom);
            return result == null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { reason = "illegalMove", message = ex.Message });
        }
    }
}
