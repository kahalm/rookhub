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
    private readonly ImportReprocessService _reprocess;

    public CourseController(CourseService service, ImportReprocessService reprocess)
    {
        _service = service;
        _reprocess = reprocess;
    }

    /// <summary>Status der Aufbereitungs-Versionierung: wie viele (verwaltbare) Kurse sind veraltet
    /// und wie aufbereitbar — Basis für den „Kurse aktualisieren (N)"-Knopf.</summary>
    [HttpGet("reprocess/status")]
    public async Task<ActionResult<ReprocessStatusDto>> ReprocessStatus(CancellationToken ct)
        => Ok(await _reprocess.GetCourseStatusAsync(GetUserId(), IsAdmin, ct));

    /// <summary>Bereitet veraltete, verwaltbare Kurse neu auf. <paramref name="localOnly"/>=true
    /// („Aus Cache") nur lokal aus gespeichertem PGN; false („Alle") reiht zusätzlich Chessable-Altbestand
    /// ohne Quelle als Re-Fetch-Hintergrund-Job ein.</summary>
    [HttpPost("reprocess")]
    public async Task<ActionResult<ReprocessResultDto>> Reprocess([FromQuery] bool localOnly, CancellationToken ct)
        => Ok(await _reprocess.ReprocessCoursesAsync(GetUserId(), IsAdmin, localOnly, ct));

    /// <summary>Alle Puzzles eines (zugänglichen) Buchs am Stück — für das Offline-Speichern.</summary>
    [HttpGet("{bookId}/puzzles")]
    public async Task<ActionResult<List<BookPuzzleDto>>> GetAllPuzzles(int bookId)
    {
        try { return Ok(await _service.GetAllPuzzlesAsync(GetUserId(), bookId, IsAdmin)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Lädt das Buch als PGN herunter (ein Spiel je Linie).</summary>
    [HttpGet("{bookId}/pgn")]
    public async Task<IActionResult> DownloadPgn(int bookId)
    {
        try
        {
            var (pgn, fileName) = await _service.GetBookPgnAsync(GetUserId(), bookId, IsAdmin);
            return File(System.Text.Encoding.UTF8.GetBytes(pgn), "application/x-chess-pgn", fileName);
        }
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

    /// <summary>Lädt ein PGN als persönlichen Kurs des Users hoch (eigenes Buch, nur für ihn sichtbar).</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(11 * 1024 * 1024)]  // 10-MB-PGN-Limit + Multipart-Overhead
    public async Task<ActionResult<CourseListItemDto>> Upload(IFormFile file, [FromForm] string? name)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });
        if (!Path.GetExtension(file.FileName).Equals(".pgn", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only .pgn files are allowed." });
        if (file.Length > RepertoireService.MaxFileSize)
            return BadRequest(new { message = $"File size exceeds maximum of {RepertoireService.MaxFileSize / 1024 / 1024} MB." });

        try
        {
            using var reader = new StreamReader(file.OpenReadStream());
            var pgn = await reader.ReadToEndAsync();
            var course = await _service.UploadPersonalCourseAsync(GetUserId(), file.FileName, pgn, name);
            return Ok(course);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Löscht einen eigenen Kurs des Users (nur der Besitzer; sonst 404).</summary>
    [HttpDelete("{bookId}")]
    public async Task<IActionResult> Delete(int bookId)
    {
        try { await _service.DeletePersonalCourseAsync(GetUserId(), bookId); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    // --- Kurs-Statistik (für die /stats-Seite, Umschalter „Kurse"). Literale Routen MÜSSEN vor
    //     den `{bookId}`-Routen stehen, sonst matcht der Router „stats"/„history" als bookId. ---

    /// <summary>Aggregierte Kurs-Puzzle-Statistik des Users (ohne Elo).</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<CourseStatsDto>> GetStats()
        => Ok(await _service.GetStatsAsync(GetUserId()));

    /// <summary>Paginierte Kurs-Versuchs-History des Users (neueste zuerst).</summary>
    [HttpGet("history")]
    public async Task<ActionResult<List<CourseAttemptDto>>> GetHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _service.GetHistoryAsync(GetUserId(), page, pageSize));

    /// <summary>Aufschlüsselung der Kurs-Versuche nach Thema/Rating-Band/Aktivität.</summary>
    [HttpGet("stats/breakdown")]
    public async Task<ActionResult<PuzzleBreakdownDto>> GetBreakdown()
        => Ok(await _service.GetBreakdownAsync(GetUserId()));

    /// <summary>Kapitel eines (zugänglichen) Buchs in Lesereihenfolge inkl. Fortschritt — Basis der Kapitelübersicht.</summary>
    [HttpGet("{bookId}/chapters")]
    public async Task<ActionResult<List<CourseChapterDto>>> GetChapters(int bookId)
    {
        try { return Ok(await _service.GetChaptersAsync(GetUserId(), bookId, IsAdmin)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Nächstes ungelöstes Puzzle des Kurses (sequential/random); aktualisiert den letzten Modus.
    /// Mit <paramref name="chapterIndex"/> wird der Pool + Fortschritt auf das Kapitel beschränkt.</summary>
    [HttpGet("{bookId}/next")]
    public async Task<IActionResult> GetNext(
        int bookId,
        [FromQuery] string mode = "sequential",
        [FromQuery] int? after = null,
        [FromQuery] int? exclude = null,
        [FromQuery] int? chapterIndex = null)
    {
        try { return Ok(await _service.GetNextAsync(GetUserId(), bookId, mode, after, exclude, IsAdmin, chapterIndex)); }
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
