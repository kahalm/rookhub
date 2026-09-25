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
    [HttpPost]
    [RequestSizeLimit(ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    public async Task<ActionResult<ScoresheetScanDto>> Upload(IFormFile? file, [FromForm] string? language)
    {
        if (file == null || file.Length == 0) return BadRequest(new { reason = "noFile", message = "No file." });
        if (file.Length > ScoresheetScanService.MaxUploadBytes)
            return BadRequest(new { reason = "tooLarge", message = "File too large." });

        byte[] data;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            data = ms.ToArray();
        }
        var (scan, reason) = await _service.CreateAsync(GetUserId(), data, file.ContentType, file.FileName, language);
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
    [HttpGet("{id:int}/photo")]
    public async Task<IActionResult> Photo(int id, [FromQuery] bool download = false)
    {
        var photo = await _scans.PhotoForGameAsync(GetUserId(), id);
        if (photo is not { } p) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
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
