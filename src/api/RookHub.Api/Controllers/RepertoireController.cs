using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/repertoires")]
[Authorize]
public class RepertoireController : BaseApiController
{
    private readonly RepertoireService _repertoireService;
    private readonly ImportReprocessService _reprocess;
    private readonly IReprocessLauncher _reprocessLauncher;
    private readonly RepertoireTrainingService _training;
    private readonly CourseService _courseService;
    private readonly SharedLineService _sharedLines;
    private readonly RepertoirePositionLookupService _positionLookup;

    public RepertoireController(RepertoireService repertoireService, ImportReprocessService reprocess, IReprocessLauncher reprocessLauncher, RepertoireTrainingService training, CourseService courseService, SharedLineService sharedLines, RepertoirePositionLookupService positionLookup)
    {
        _repertoireService = repertoireService;
        _reprocess = reprocess;
        _reprocessLauncher = reprocessLauncher;
        _training = training;
        _courseService = courseService;
        _sharedLines = sharedLines;
        _positionLookup = positionLookup;
    }

    // ===== Stellungs-Rückwärtssuche: „In welchen Repertoire-Linien kommt diese Stellung vor?" =====

    /// <summary>Findet alle eigenen Repertoire-Linien (Repertoire → Kapitel → Linie), in denen die
    /// gegebene Stellung vorkommt (Zugumstellungen inklusive). Literale Route MUSS vor `{id}` stehen.</summary>
    [HttpPost("position-lookup")]
    public async Task<ActionResult<PositionLookupResultDto>> PositionLookup([FromBody] PositionLookupRequestDto dto, CancellationToken ct)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Fen)) return BadRequest();
        return Ok(await _positionLookup.LookupAsync(GetUserId(), dto.Fen, ct));
    }

    // ===== Einzelne Linie öffentlich teilen (Nur-Ansehen-Link /l/{token}) =====

    /// <summary>Öffentliche Sicht einer geteilten Linie über das Token — kein Login nötig.
    /// Literale Route MUSS vor den `{id}`-Routen stehen.</summary>
    [HttpGet("shared-line/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<SharedLineDto>> GetSharedLine(string token, CancellationToken ct)
    {
        var dto = await _sharedLines.GetByTokenAsync(token, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>Erzeugt einen öffentlichen Nur-Ansehen-Link für eine Linie des Repertoires.
    /// Besitzer oder Freigabe-Empfänger; liefert bei erneutem Teilen derselben Linie denselben Link.</summary>
    [HttpPost("{id:int}/share-line")]
    public async Task<ActionResult<ShareLineResultDto>> ShareLine(int id, [FromBody] ShareLineInputDto dto, CancellationToken ct)
    {
        var res = await _sharedLines.CreateAsync(GetUserId(), id, dto, ct);
        return res == null ? NotFound() : Ok(res);
    }

    // ===== Repertoire-Trainer (Spaced Repetition, 9-Stufen-Leiter) =====
    // Brett-/Baumlogik + Linien-Schlüssel liegen im Frontend; hier nur der Linien-SR-Zustand
    // (Stufe/Fälligkeit) + die Intervall-Konfiguration (global + pro-Repertoire-Override).

    /// <summary>Globale (per-User) Standard-Intervalle der 9 Stufen — die literale Route MUSS vor
    /// den `{id}`-Routen stehen.</summary>
    [HttpGet("training/sr-config")]
    public async Task<ActionResult<List<SrLevelDto>>> GetUserSrConfig(CancellationToken ct)
        => Ok(await _training.GetUserConfigAsync(GetUserId(), ct));

    /// <summary>Setzt die globalen Nutzer-Intervalle (`levels`=null → auf Defaults zurücksetzen).</summary>
    [HttpPut("training/sr-config")]
    public async Task<IActionResult> SetUserSrConfig([FromBody] SetSrConfigRequest req, CancellationToken ct)
        => await _training.SetUserConfigAsync(GetUserId(), req.Levels, ct) ? NoContent() : BadRequest();

    /// <summary>Alle Linien-SR-Zustände (Stufe/Fälligkeit) des eigenen Repertoires — das Frontend
    /// ermittelt daraus die fälligen Linien. 404 wenn nicht vorhanden/nicht eigenes Repertoire.</summary>
    [HttpGet("{id:int}/training/lines")]
    public async Task<ActionResult<List<LineStateDto>>> TrainingLines(int id, CancellationToken ct)
    {
        var lines = await _training.GetLineStatesAsync(GetUserId(), id, ct);
        return lines is null ? NotFound() : Ok(lines);
    }

    /// <summary>Bewertet eine geübte Linie (richtig → +1 Stufe, falsch → Stufe 1) und plant sie neu.</summary>
    [HttpPost("{id:int}/training/line-review")]
    public async Task<ActionResult<LineStateDto>> TrainingLineReview(int id, [FromBody] LineReviewRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(req.LineKey)) return BadRequest();
        var dto = await _training.ReviewLineAsync(GetUserId(), id, req, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>Effektive SR-Konfiguration dieses Repertoires (Override &gt; global &gt; Default) +
    /// beide Ebenen für die Bearbeitung. 404 wenn nicht eigenes Repertoire.</summary>
    [HttpGet("{id:int}/training/config")]
    public async Task<ActionResult<SrConfigDto>> TrainingConfig(int id, CancellationToken ct)
    {
        var cfg = await _training.GetConfigAsync(GetUserId(), id, ct);
        return cfg is null ? NotFound() : Ok(cfg);
    }

    /// <summary>Setzt den pro-Repertoire-Intervall-Override (`levels`=null → Override löschen =
    /// wieder globale Defaults). 404 nicht eigenes Repertoire, 400 ungültige Stufen.</summary>
    [HttpPut("{id:int}/training/config")]
    public async Task<IActionResult> SetTrainingConfig(int id, [FromBody] SetSrConfigRequest req, CancellationToken ct)
    {
        var res = await _training.SetRepertoireConfigAsync(GetUserId(), id, req.Levels, ct);
        return res is null ? NotFound() : res.Value ? NoContent() : BadRequest();
    }

    /// <summary>Pausiert/aktiviert Linien (Kapitel = alle seine Linien-Schlüssel) — pausierte Linien
    /// fallen NICHT in den Übungspool. 404 nicht eigenes Repertoire.</summary>
    [HttpPost("{id:int}/training/pause")]
    public async Task<IActionResult> TrainingPause(int id, [FromBody] SetPausedRequest req, CancellationToken ct)
    {
        var n = await _training.SetPausedAsync(GetUserId(), id, req.LineKeys ?? new(), req.Paused, ct);
        return n is null ? NotFound() : Ok(new { affected = n });
    }

    /// <summary>Nimmt Linien in den Übungspool auf (Learn/manuell; Kapitel/Kurs = deren Linien-
    /// Schlüssel) — sofort fällig. 404 nicht eigenes Repertoire.</summary>
    [HttpPost("{id:int}/training/promote")]
    public async Task<IActionResult> TrainingPromote(int id, [FromBody] PromoteLinesRequest req, CancellationToken ct)
    {
        var n = await _training.PromoteAsync(GetUserId(), id, req.LineKeys ?? new(), ct);
        return n is null ? NotFound() : Ok(new { affected = n });
    }

    /// <summary>Macht Pool-Linien sofort fällig + hebt Pause auf (leere Liste = ganzer Kurs). 404 nicht
    /// eigenes Repertoire.</summary>
    [HttpPost("{id:int}/training/make-due")]
    public async Task<IActionResult> TrainingMakeDue(int id, [FromBody] MakeDueRequest req, CancellationToken ct)
    {
        var n = await _training.MakeDueAsync(GetUserId(), id, req.LineKeys ?? new(), ct);
        return n is null ? NotFound() : Ok(new { affected = n });
    }

    /// <summary>Löscht ALLE Linien-SR-Zustände des eigenen Users für dieses Repertoire — der Trainer
    /// startet danach mit frischem Fortschritt (alle Linien wieder fällig). 404 wenn Repertoire nicht
    /// existiert / nicht dem User gehört.</summary>
    [HttpDelete("{id:int}/training/reset")]
    public async Task<ActionResult<int>> TrainingReset(int id, CancellationToken ct)
    {
        var deleted = await _training.ResetAsync(GetUserId(), id, ct);
        return deleted is null ? NotFound() : Ok(new { deleted });
    }

    [HttpGet]
    public async Task<ActionResult<List<RepertoireDto>>> GetAll()
    {
        return Ok(await _repertoireService.GetAllAsync(GetUserId()));
    }

    /// <summary>Status der Aufbereitungs-Versionierung der eigenen Repertoires (Basis für den
    /// „Repertoires aktualisieren (N)"-Knopf). Heute meist 0, da Repertoires live ausgewertet werden.</summary>
    [HttpGet("reprocess/status")]
    public async Task<ActionResult<ReprocessStatusDto>> ReprocessStatus(CancellationToken ct)
        => Ok(await _reprocess.GetRepertoireStatusAsync(GetUserId(), IsAdmin, ct));

    /// <summary>Bereitet veraltete eigene Repertoires auf. <paramref name="localOnly"/>=true („Aus Cache")
    /// nur lokal aufbereitbare (Nicht-Chessable, Versions-Mark); false („Alle") holt zusätzlich
    /// Chessable-Repertoires frisch. Läuft im HINTERGRUND → antwortet sofort 202 (kein Request-Timeout
    /// bei vielen Chessable-Re-Fetches); Fortschritt über das Status-Banner / die Import-Anzeige.</summary>
    [HttpPost("reprocess")]
    public IActionResult Reprocess([FromQuery] bool localOnly)
    {
        _reprocessLauncher.LaunchRepertoires(GetUserId(), IsAdmin, localOnly);
        return Accepted(new { started = true });
    }

    [HttpPost]
    public async Task<ActionResult<RepertoireDto>> Create([FromBody] CreateRepertoireDto dto)
    {
        var result = await _repertoireService.CreateAsync(GetUserId(), dto);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RepertoireDetailDto>> GetById(int id)
    {
        try
        {
            return Ok(await _repertoireService.GetByIdAsync(id, GetUserId()));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<RepertoireDto>> Update(int id, [FromBody] UpdateRepertoireDto dto)
    {
        try
        {
            return Ok(await _repertoireService.UpdateAsync(id, GetUserId(), dto));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _repertoireService.DeleteAsync(id, GetUserId());
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("{id}/files")]
    [RequestSizeLimit(11 * 1024 * 1024)]  // 10-MB-PGN-Limit + etwas Multipart-Overhead; lehnt zu grosse Bodies ab, bevor sie gepuffert werden
    public async Task<ActionResult<RepertoireFileDto>> UploadFile(int id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });

        if (!Path.GetExtension(file.FileName).Equals(".pgn", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only .pgn files are allowed." });

        if (file.Length > RepertoireService.MaxFileSize)
            return BadRequest(new { message = $"File size exceeds maximum of {RepertoireService.MaxFileSize / 1024 / 1024} MB." });

        try
        {
            using var stream = file.OpenReadStream();
            var result = await _repertoireService.UploadFileAsync(id, GetUserId(), file.FileName, stream);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("{id}/files/{fileId}")]
    public async Task<IActionResult> DownloadFile(int id, int fileId)
    {
        try
        {
            var (fileName, content) = await _repertoireService.DownloadFileAsync(id, fileId, GetUserId());
            return File(System.Text.Encoding.UTF8.GetBytes(content), "application/x-chess-pgn", fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}/files/{fileId}")]
    public async Task<IActionResult> DeleteFile(int id, int fileId)
    {
        try
        {
            await _repertoireService.DeleteFileAsync(id, fileId, GetUserId());
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("{id}/pgn")]
    public async Task<IActionResult> GetCombinedPgn(int id)
    {
        try
        {
            var pgn = await _repertoireService.GetCombinedPgnAsync(id, GetUserId());
            return Content(pgn, "text/plain");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>„Repertoire → Kurs umwandeln" (Verschieben): legt aus dem Repertoire-PGN einen
    /// persönlichen Kurs an und ENTFERNT anschließend das Original-Repertoire. Funktioniert nur mit
    /// Puzzle-PGN im Chessable-Stil (FEN + Round + Trainingsmarker je Zug); ein reines Eröffnungs-
    /// Repertoire ohne Puzzle-Marker liefert 400 (kein quiz-barer Inhalt) — dann bleibt das Repertoire
    /// erhalten (Löschung passiert erst NACH erfolgreicher Kurs-Erstellung).</summary>
    [HttpPost("{id}/convert-to-course")]
    public async Task<IActionResult> ConvertToCourse(int id)
    {
        try
        {
            var userId = GetUserId();
            // Umwandeln VERSCHIEBT (löscht das Original) → nur der Besitzer, nicht ein Freigabe-Empfänger.
            if (!await _repertoireService.IsOwnerAsync(id, userId))
                return NotFound(new { message = "Repertoire not found." });
            var detail = await _repertoireService.GetByIdAsync(id, userId);
            var pgn = await _repertoireService.GetCombinedPgnAsync(id, userId);
            var course = await _courseService.UploadPersonalCourseAsync(userId, detail.Name + ".pgn", pgn, detail.Name);
            // Verschieben statt Kopieren: das Original-Repertoire nach erfolgreicher Umwandlung entfernen.
            await _repertoireService.DeleteAsync(id, userId);
            return Ok(course);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Teilt ein eigenes Repertoire mit ausgewählten (befreundeten) Nutzern (Batch).
    /// Antwortet <c>{ shared, skipped[] }</c> (übersprungene Empfänger mit Grund).</summary>
    [HttpPost("{id}/share")]
    public async Task<ActionResult<RepertoireShareResultDto>> Share(int id, [FromBody] ShareRepertoireInputDto dto)
    {
        try { return Ok(await _repertoireService.ShareAsync(GetUserId(), id, dto.RecipientUserIds ?? new List<int>(), IsAdmin)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
    }

    /// <summary>Mit welchen Nutzern ist dieses eigene Repertoire aktuell geteilt? (Für den Teilen-Dialog.)</summary>
    [HttpGet("{id}/shares")]
    public async Task<ActionResult<List<RepertoireShareRecipientDto>>> Shares(int id)
    {
        try { return Ok(await _repertoireService.GetShareRecipientsAsync(GetUserId(), id)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
    }

    /// <summary>Nimmt die Freigabe des eigenen Repertoires für einen Empfänger zurück (idempotent).</summary>
    [HttpDelete("{id}/share/{recipientId}")]
    public async Task<IActionResult> Unshare(int id, int recipientId)
    {
        try { await _repertoireService.UnshareAsync(GetUserId(), id, recipientId); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
    }
}
