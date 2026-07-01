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

    public RepertoireController(RepertoireService repertoireService, ImportReprocessService reprocess, IReprocessLauncher reprocessLauncher, RepertoireTrainingService training)
    {
        _repertoireService = repertoireService;
        _reprocess = reprocess;
        _reprocessLauncher = reprocessLauncher;
        _training = training;
    }

    // ===== Repertoire-Trainer (Spaced Repetition) =====
    // Brett-/Baumlogik liegt im Frontend; hier nur der SM-2-Kartenzustand je Stellung.

    /// <summary>SM-2-Zustände aller Trainingskarten des eigenen Repertoires (Frontend ermittelt
    /// daraus fällige/neue Karten). 404 wenn das Repertoire nicht existiert/nicht dem User gehört.</summary>
    [HttpGet("{id}/training/cards")]
    public async Task<ActionResult<List<RepertoireCardStateDto>>> TrainingCards(int id, CancellationToken ct)
    {
        var cards = await _training.GetCardsAsync(GetUserId(), id, ct);
        return cards is null ? NotFound() : Ok(cards);
    }

    /// <summary>Bewertet eine Karte nach einem Versuch (legt sie bei Bedarf an) und plant sie neu.</summary>
    [HttpPost("{id}/training/review")]
    public async Task<ActionResult<RepertoireCardStateDto>> TrainingReview(int id, [FromBody] ReviewCardRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(req.CardKey)) return BadRequest();
        var dto = await _training.ReviewAsync(GetUserId(), id, req, ct);
        return dto is null ? NotFound() : Ok(dto);
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
}
