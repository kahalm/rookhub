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
/// Fehlerfälle werfen die Dienste als Domänen-Ausnahme (404/403/400), der globale DomainExceptionFilter
/// macht daraus <c>{ message }</c> — hier wird nichts gefangen (Codereview A7-011).
/// </summary>
[ApiController]
[Route("api/courses")]
[Authorize]
public class CourseController : BaseApiController
{
    private readonly CourseService _service;
    private readonly CourseStatsService _stats;
    private readonly ImportReprocessService _reprocess;
    private readonly IReprocessLauncher _reprocessLauncher;
    private readonly CourseAuthoringService _authoring;
    private readonly FlashcardMarkService _flashcards;
    /// <summary>Kurs ⇄ Repertoire — beide Richtungen, siehe <see cref="CourseRepertoireConversionService"/>.</summary>
    private readonly CourseRepertoireConversionService _conversion;
    /// <summary>PGN-Export (Buch/Kapitel/Linie), siehe <see cref="CoursePgnExportService"/>.</summary>
    private readonly CoursePgnExportService _pgnExport;
    /// <summary>Kurs-Übersetzung ausliefern (<c>?lang=</c>). Optional, damit bestehende Test-Konstruktionen
    /// unverändert kompilieren — ohne ihn bleibt <c>lang</c> wirkungslos (Original).</summary>
    private readonly CourseCommentLocalizer? _localizer;

    public CourseController(CourseService service, CourseStatsService stats, ImportReprocessService reprocess,
        IReprocessLauncher reprocessLauncher, CourseAuthoringService authoring, FlashcardMarkService flashcards,
        CourseRepertoireConversionService conversion, CoursePgnExportService pgnExport,
        CourseCommentLocalizer? localizer = null)
    {
        _conversion = conversion;
        _pgnExport = pgnExport;
        _localizer = localizer;
        _service = service;
        _stats = stats;
        _reprocess = reprocess;
        _reprocessLauncher = reprocessLauncher;
        _authoring = authoring;
        _flashcards = flashcards;
    }

    /// <summary>Status der Aufbereitungs-Versionierung: wie viele (verwaltbare) Kurse sind veraltet
    /// und wie aufbereitbar — Basis für den „Kurse aktualisieren (N)"-Knopf.</summary>
    [HttpGet("reprocess/status")]
    public async Task<ActionResult<ReprocessStatusDto>> ReprocessStatus(CancellationToken ct)
        => Ok(await _reprocess.GetCourseStatusAsync(GetUserId(), IsAdmin, ct));

    /// <summary>Bereitet veraltete, verwaltbare Kurse neu auf. <paramref name="localOnly"/>=true
    /// („Aus Cache") ohne Chessable-Abruf: aus gespeichertem PGN, Chessable-Kurse mit oids mit frischen
    /// Zugtexten aus dem Linien-Cache; false („Alle") reiht zusätzlich Chessable-Altbestand
    /// ohne Quelle als Re-Fetch-Hintergrund-Job ein. Läuft im HINTERGRUND (kann bei vielen Kursen
    /// über das Request-Timeout hinaus dauern) → antwortet sofort 202; der Fortschritt erscheint über
    /// das Reprocess-Status-Banner bzw. die Chessable-Import-Anzeige.</summary>
    [HttpPost("reprocess")]
    public IActionResult Reprocess([FromQuery] bool localOnly)
    {
        _reprocessLauncher.LaunchCourses(GetUserId(), IsAdmin, localOnly);
        return Accepted(new { started = true });
    }

    /// <summary>Alle Puzzles eines (zugänglichen) Buchs am Stück — für das Offline-Speichern.
    /// <c>?lang=</c> liefert die Kommentare übersetzt, wo es aktuelle Übersetzungen gibt (sonst Original).</summary>
    [HttpGet("{bookId}/puzzles")]
    public async Task<ActionResult<List<BookPuzzleDto>>> GetAllPuzzles(int bookId, [FromQuery] string? lang = null,
        CancellationToken ct = default)
    {
        var lines = await _service.GetAllPuzzlesAsync(GetUserId(), bookId, IsAdmin);
        if (_localizer is not null) await _localizer.ApplyAsync(lines, lang, ct);
        return Ok(lines);
    }

    /// <summary>Puzzles eines ÖFFENTLICHEN Kurses — OHNE Login. Ermöglicht das registrierungsfreie
    /// Durchspielen eines als „public" markierten Kurses über den Direkt-Link (anonymer Fortschritt
    /// bleibt lokal). Optional <c>?skip=&amp;take=</c> für seitenweises Laden (großer Kurs → erste
    /// Seite sofort, Rest im Hintergrund); ohne Parameter das ganze Buch. 404, wenn nicht öffentlich.</summary>
    [AllowAnonymous]
    [HttpGet("{bookId}/public")]
    public async Task<ActionResult<List<BookPuzzleDto>>> GetPublicCourse(int bookId, [FromQuery] int? skip, [FromQuery] int? take,
        [FromQuery] string? lang = null, CancellationToken ct = default)
    {
        int? clampedTake = take is int t ? Math.Clamp(t, 1, 1000) : null;
        int? clampedSkip = skip is int s && s > 0 ? s : null;
        var lines = await _service.GetPublicCoursePuzzlesAsync(bookId, clampedSkip, clampedTake);
        if (_localizer is not null) await _localizer.ApplyAsync(lines, lang, ct);
        return Ok(lines);
    }

    /// <summary>Löst einen öffentlichen Kurz-Alias (z. B. <c>mate1</c>) auf sein Ziel auf — OHNE Login:
    /// <c>{ bookId, isCalculation }</c>. Basis für die Kurz-URL <c>/{slug}</c>. <c>isCalculation</c>
    /// entscheidet, ob der Link in den Kalkulations-Modus oder in den Solver springt (ein
    /// Kalkulationsbuch hat nur Info-Linien — der Solver wäre sofort „abgeschlossen").
    /// 404 bei unbekanntem Alias.</summary>
    [AllowAnonymous]
    [HttpGet("by-slug/{slug}")]
    public async Task<ActionResult<PublicSlugTargetDto>> ResolvePublicSlug(string slug)
    {
        var target = await _service.ResolvePublicSlugAsync(slug);
        return target is not null ? Ok(target) : NotFound(new { message = "Unknown alias." });
    }

    /// <summary>Löst <c>/{slug}/{kapitel}</c> auf — OHNE Login: <c>{ bookId, isCalculation, chapter,
    /// chapterIndex }</c>. Der Kapitel-Teil der URL IST der Kapitelname (getrimmt, ohne
    /// Groß-/Kleinschreibungs-Unterschied verglichen); <c>chapterIndex</c> ist der SOLVER-Index für
    /// <c>courses/:bookId/chapter/:index/:mode</c> und <c>null</c> bei Kalkulationsbüchern bzw. reinen
    /// Info-Kapiteln (dort filtert der Kalkulations-Modus über den Namen).
    /// 404 bei unbekanntem Alias ODER unbekanntem Kapitel.</summary>
    [AllowAnonymous]
    [HttpGet("by-slug/{slug}/{chapter}")]
    public async Task<ActionResult<PublicSlugChapterDto>> ResolvePublicSlugChapter(string slug, string chapter)
    {
        var target = await _service.ResolvePublicSlugChapterAsync(slug, chapter);
        return target is not null ? Ok(target) : NotFound(new { message = "Unknown alias or chapter." });
    }

    /// <summary>Pro-Linien-Status eines (zugänglichen) Buchs für die „Linien durchsehen"-Ansicht:
    /// gelöste (✓) und versucht-aber-nicht-gelöste (✗) Linien des Users.</summary>
    [HttpGet("{bookId}/line-status")]
    public async Task<ActionResult<CourseLineStatusDto>> GetLineStatus(int bookId)
    {
        return Ok(await _service.GetLineStatusAsync(GetUserId(), bookId, IsAdmin));
    }

    /// <summary>Lädt das Buch als PGN herunter (ein Spiel je Linie).</summary>
    [HttpGet("{bookId}/pgn")]
    public async Task<IActionResult> DownloadPgn(int bookId)
    {
        var (pgn, fileName) = await _pgnExport.GetBookPgnAsync(GetUserId(), bookId, IsAdmin);
        return PgnDownload(pgn, fileName);
    }

    /// <summary>Lädt EIN Kapitel als PGN herunter (<c>chapter</c> leer = „ohne Kapitel").</summary>
    [HttpGet("{bookId:int}/chapter-pgn")]
    public async Task<IActionResult> DownloadChapterPgn(int bookId, [FromQuery] string? chapter)
    {
        var (pgn, fileName) = await _pgnExport.GetChapterPgnAsync(GetUserId(), bookId, chapter, IsAdmin);
        return PgnDownload(pgn, fileName);
    }

    /// <summary>Lädt EINE Linie als PGN herunter.</summary>
    [HttpGet("{bookId:int}/lines/{lineId:int}/pgn")]
    public async Task<IActionResult> DownloadLinePgn(int bookId, int lineId)
    {
        var (pgn, fileName) = await _pgnExport.GetLinePgnAsync(GetUserId(), bookId, lineId, IsAdmin);
        return PgnDownload(pgn, fileName);
    }

    /// <summary>PGN als Datei ausliefern — ohne die internen Marker <c>[%alt]</c>/<c>[%info]</c>, Info-Linien
    /// mit „Info | " im White-Header (siehe <see cref="PgnParser.ForDownload"/>).</summary>
    private FileContentResult PgnDownload(string pgn, string fileName)
        => File(System.Text.Encoding.UTF8.GetBytes(PgnParser.ForDownload(pgn)), "application/x-chess-pgn", fileName);

    /// <summary>„Kurs → Repertoire umwandeln": legt aus dem Kurs-PGN ein neues Repertoire des Users an
    /// (Original-Kurs bleibt). Antwort = das neue Repertoire.</summary>
    [HttpPost("{bookId}/convert-to-repertoire")]
    public async Task<IActionResult> ConvertToRepertoire(int bookId)
    {
        return Ok(await _conversion.ConvertCourseToRepertoireAsync(GetUserId(), bookId, IsAdmin));
    }

    /// <summary>Sichtbare Bücher als Kurse inkl. Fortschritt des aktuellen Users (Admin: alle).</summary>
    [HttpGet]
    public async Task<IActionResult> GetCourses()
        => Ok(await _service.GetCoursesAsync(GetUserId(), IsAdmin));

    /// <summary>Hat der User Zugriff auf mindestens einen Kurs? (Basis für die Menü-Sichtbarkeit.)</summary>
    [HttpGet("access")]
    public async Task<IActionResult> HasAnyAccess()
        => Ok(new { hasAccess = await _service.HasAnyAccessAsync(GetUserId(), IsAdmin) });

    /// <summary>Legt einen persönlichen Kurs des Users an (eigenes Buch, nur für ihn sichtbar) — mit PGN
    /// als Inhalt ODER leer, wenn keine Datei mitkommt. Der leere Kurs wird danach auf der Detailseite
    /// Kapitel für Kapitel gefüllt; ohne Datei ist der Name deshalb Pflicht (er kann nicht aus einem
    /// Dateinamen abgeleitet werden).</summary>
    [HttpPost]
    [RequestSizeLimit(11 * 1024 * 1024)]  // 10-MB-PGN-Limit + Multipart-Overhead
    public async Task<ActionResult<CourseListItemDto>> Create(IFormFile? file, [FromForm] string? name)
    {
        if (file == null || file.Length == 0)
        {
            return Ok(await _service.CreatePersonalCourseAsync(GetUserId(), name ?? string.Empty));
        }
        if (!Path.GetExtension(file.FileName).Equals(".pgn", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only .pgn files are allowed." });
        if (file.Length > RepertoireService.MaxFileSize)
            return BadRequest(new { message = $"File size exceeds maximum of {RepertoireService.MaxFileSize / 1024 / 1024} MB." });

        using var reader = new StreamReader(file.OpenReadStream());
        var pgn = await reader.ReadToEndAsync();
        var course = await _service.UploadPersonalCourseAsync(GetUserId(), file.FileName, pgn, name);
        return Ok(course);
    }

    /// <summary>Löscht einen eigenen Kurs des Users (nur der Besitzer; sonst 404).</summary>
    [HttpDelete("{bookId}")]
    public async Task<IActionResult> Delete(int bookId)
    {
        await _service.DeletePersonalCourseAsync(GetUserId(), bookId);
        return NoContent();
    }

    /// <summary>Teilt einen eigenen Kurs mit ausgewählten (befreundeten) Nutzern (Batch).
    /// Antwortet <c>{ shared, skipped[] }</c> (übersprungene Empfänger mit Grund).</summary>
    [HttpPost("{bookId}/share")]
    public async Task<ActionResult<CourseShareResultDto>> Share(int bookId, [FromBody] ShareCourseInputDto dto)
    {
        return Ok(await _service.ShareCourseAsync(GetUserId(), bookId, dto.RecipientUserIds ?? new List<int>(), IsAdmin));
    }

    /// <summary>Mit welchen Nutzern ist dieser eigene Kurs aktuell geteilt? (Für den Teilen-Dialog.)</summary>
    [HttpGet("{bookId}/shares")]
    public async Task<ActionResult<List<CourseShareRecipientDto>>> Shares(int bookId)
    {
        return Ok(await _service.GetShareRecipientsAsync(GetUserId(), bookId));
    }

    /// <summary>Nimmt die Freigabe des eigenen Kurses für einen Empfänger zurück (idempotent).</summary>
    [HttpDelete("{bookId}/share/{recipientId}")]
    public async Task<IActionResult> Unshare(int bookId, int recipientId)
    {
        await _service.UnshareCourseAsync(GetUserId(), bookId, recipientId);
        return NoContent();
    }

    /// <summary>Verknüpft diesen Kurs mit einem anderen (Buch↔Workbook) für den Schnellwechsel.</summary>
    [HttpPost("{bookId}/link")]
    public async Task<IActionResult> Link(int bookId, [FromBody] LinkCourseInputDto dto)
    {
        await _service.LinkCoursesAsync(GetUserId(), bookId, dto.LinkedBookId, IsAdmin);
        return NoContent();
    }

    /// <summary>Der aktuell verknüpfte Partner-Kurs (oder leere Felder). Literale Route.</summary>
    [HttpGet("{bookId}/link")]
    public async Task<ActionResult<CourseLinkDto>> GetLink(int bookId)
    {
        return Ok(await _service.GetLinkAsync(GetUserId(), bookId, IsAdmin));
    }

    /// <summary>Hebt die Verknüpfung dieses Kurses wieder auf (idempotent).</summary>
    [HttpDelete("{bookId}/link")]
    public async Task<IActionResult> Unlink(int bookId)
    {
        await _service.UnlinkCourseAsync(GetUserId(), bookId);
        return NoContent();
    }

    // --- Kurs-Statistik (für die /stats-Seite, Umschalter „Kurse"). Literale Routen MÜSSEN vor
    //     den `{bookId}`-Routen stehen, sonst matcht der Router „stats"/„history" als bookId. ---

    /// <summary>Aggregierte Kurs-Puzzle-Statistik des Users (ohne Elo).</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<CourseStatsDto>> GetStats()
        => Ok(await _stats.GetStatsAsync(GetUserId()));

    /// <summary>Paginierte Kurs-Versuchs-History des Users (neueste zuerst).</summary>
    [HttpGet("history")]
    public async Task<ActionResult<List<CourseAttemptDto>>> GetHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _stats.GetHistoryAsync(GetUserId(), page, pageSize));

    /// <summary>Aufschlüsselung der Kurs-Versuche nach Thema/Rating-Band/Aktivität.</summary>
    [HttpGet("stats/breakdown")]
    public async Task<ActionResult<PuzzleBreakdownDto>> GetBreakdown()
        => Ok(await _stats.GetBreakdownAsync(GetUserId()));

    /// <summary>Kapitel eines (zugänglichen) Buchs in Lesereihenfolge inkl. Fortschritt — Basis der Kapitelübersicht.</summary>
    [HttpGet("{bookId}/chapters")]
    public async Task<ActionResult<List<CourseChapterDto>>> GetChapters(int bookId, [FromQuery] string? lang = null,
        CancellationToken ct = default)
    {
        var chapters = await _service.GetChaptersAsync(GetUserId(), bookId, IsAdmin);
        if (_localizer is not null) await _localizer.ApplyAsync(bookId, chapters, lang, ct);
        return Ok(chapters);
    }

    /// <summary>Nächstes ungelöstes Puzzle des Kurses (sequential/random); aktualisiert den letzten Modus.
    /// Mit <paramref name="chapterIndex"/> wird der Pool + Fortschritt auf das Kapitel beschränkt.</summary>
    [HttpGet("{bookId}/next")]
    public async Task<IActionResult> GetNext(
        int bookId,
        [FromQuery] string mode = "sequential",
        [FromQuery] int? after = null,
        [FromQuery] int? exclude = null,
        [FromQuery] int? chapterIndex = null,
        [FromQuery] string? lang = null,
        CancellationToken ct = default)
    {
        var next = await _service.GetNextAsync(GetUserId(), bookId, mode, after, exclude, IsAdmin, chapterIndex);
        if (_localizer is not null) await _localizer.ApplyAsync(next.Puzzle, lang, ct);
        return Ok(next);
    }

    /// <summary>Zeichnet einen Lösungsversuch auf. Bei Solved wird das Puzzle (idempotent) als gelöst markiert.</summary>
    [HttpPost("{bookId}/results")]
    public async Task<IActionResult> RecordResult(int bookId, [FromBody] RecordCourseResultDto dto)
    {
        return Ok(await _service.RecordResultAsync(GetUserId(), bookId, dto, IsAdmin));
    }

    /// <summary>Merkt eine sequenziell durchgeklickte Info-/Erklärlinie — beim nächsten Wiedereinstieg
    /// startet der Kurs dahinter statt sie erneut zu zeigen. 404 wenn die Linie nicht zum (zugänglichen)
    /// Buch gehört oder keine Info-Linie ist.</summary>
    [HttpPost("{bookId}/info-seen")]
    public async Task<IActionResult> MarkInfoSeen(int bookId, [FromBody] MarkInfoSeenDto dto)
    {
        await _service.MarkInfoSeenAsync(GetUserId(), bookId, dto.BookPuzzleId, IsAdmin);
        return NoContent();
    }

    /// <summary>Setzt den Fortschritt eines Kurses zurück (löscht alle gelösten Markierungen).</summary>
    [HttpPost("{bookId}/reset")]
    public async Task<IActionResult> Reset(int bookId)
    {
        return Ok(await _service.ResetAsync(GetUserId(), bookId, IsAdmin));
    }

    /// <summary>Pinnt den Kurs fürs Dashboard an (persönlich, idempotent). 404 wenn nicht zugänglich.</summary>
    [HttpPost("{bookId}/pin")]
    public async Task<IActionResult> Pin(int bookId)
    {
        await _service.PinCourseAsync(GetUserId(), bookId, IsAdmin);
        return NoContent();
    }

    /// <summary>Löst den Kurs wieder vom Dashboard (idempotent).</summary>
    [HttpDelete("{bookId}/pin")]
    public async Task<IActionResult> Unpin(int bookId)
    {
        await _service.UnpinCourseAsync(GetUserId(), bookId);
        return NoContent();
    }

    /// <summary>Setzt die Themen-Tags des Kurs-Buchs (Admin/Besitzer). 404 unzugänglich, 403 nicht
    /// berechtigt, 400 ungültiger Theme-Key. Antwortet mit den effektiven Keys (Default „tactics").</summary>
    [HttpPut("{bookId}/themes")]
    public async Task<IActionResult> SetThemes(int bookId, [FromBody] SetCourseThemesInputDto dto)
    {
        return Ok(new { themes = await _service.SetBookThemesAsync(GetUserId(), bookId, dto.Themes ?? new List<string>(), IsAdmin) });
    }

    // ===== Detailseite + Inhaltspflege (CourseAuthoringService) ==============
    // Die {bookId:int}-Zwänge halten diese Routen von den literalen (access/stats/history) fern.

    /// <summary>Vollbild der Kurs-Detailseite: Metadaten, eigener Fortschritt, Kapitel-Verwaltungssicht
    /// (inkl. reiner Stellungs-Kapitel). 404 wenn nicht zugänglich. Terminierte, für den Betrachter noch
    /// gesperrte Wochen einer Kalkulations-Serie fehlen in Kapitelliste UND Zählern
    /// (<see cref="CalcVisibility.HiddenChaptersAsync"/>; Besitzer/Admin sehen alles).</summary>
    [HttpGet("{bookId:int}")]
    public async Task<ActionResult<CourseDetailDto>> GetDetail(int bookId, CancellationToken ct,
        [FromQuery] string? lang = null)
    {
        var detail = await _authoring.GetDetailAsync(GetUserId(), bookId, IsAdmin, ct);
        if (_localizer is not null) await _localizer.ApplyAsync(detail, lang, ct);
        return Ok(detail);
    }

    /// <summary>Schaltet den Kalkulations-Modus des Kurses ein/aus (Besitzer/Admin, 403 sonst; 404
    /// unzugänglich). Antwortet mit dem effektiven Zustand.</summary>
    [HttpPut("{bookId:int}/calculation")]
    public async Task<IActionResult> SetCalculation(int bookId, [FromBody] SetCourseCalculationDto dto,
        CancellationToken ct)
    {
        var value = await _authoring.SetCalculationAsync(GetUserId(), bookId, dto.IsCalculation, IsAdmin, ct);
        return Ok(new { isCalculation = value });
    }

    /// <summary>Als Flashcard markierte Linien-Ids des Users in diesem Kurs. 404 unzugänglich.</summary>
    [HttpGet("{bookId:int}/flashcards")]
    public async Task<IActionResult> GetFlashcardMarks(int bookId, CancellationToken ct)
    {
        var ids = await _flashcards.GetCourseMarksAsync(GetUserId(), bookId, IsAdmin, ct);
        return ids is null ? NotFound(new { message = "Book not found." }) : Ok(new { lineIds = ids });
    }

    /// <summary>Markiert eine Kurs-Linie als Flashcard (idempotent). 404 unzugänglich/Linie fremd.</summary>
    [HttpPost("{bookId:int}/flashcards/{lineId:int}")]
    public async Task<IActionResult> MarkFlashcard(int bookId, int lineId, CancellationToken ct)
    {
        var res = await _flashcards.SetCourseMarkAsync(GetUserId(), bookId, lineId, marked: true, IsAdmin, ct);
        return res is null ? NotFound(new { message = "Line not found." }) : Ok(new { marked = true });
    }

    /// <summary>Entfernt die Flashcard-Markierung einer Kurs-Linie (idempotent).</summary>
    [HttpDelete("{bookId:int}/flashcards/{lineId:int}")]
    public async Task<IActionResult> UnmarkFlashcard(int bookId, int lineId, CancellationToken ct)
    {
        var res = await _flashcards.SetCourseMarkAsync(GetUserId(), bookId, lineId, marked: false, IsAdmin, ct);
        return res is null ? NotFound(new { message = "Line not found." }) : Ok(new { marked = false });
    }

    /// <summary>Linien EINES Kapitels (`chapter` leer = „ohne Kapitel") — ohne Lösungszüge. Eine für den
    /// Betrachter noch gesperrte Woche einer Kalkulations-Serie liefert eine leere Liste wie ein
    /// unbekanntes Kapitel (<see cref="CalcVisibility.HiddenChaptersAsync"/>, kein Orakel).</summary>
    [HttpGet("{bookId:int}/lines")]
    public async Task<ActionResult<List<CourseLineDto>>> GetChapterLines(int bookId, [FromQuery] string? chapter,
        CancellationToken ct)
    {
        return Ok(await _authoring.GetChapterLinesAsync(GetUserId(), bookId, chapter, IsAdmin, ct));
    }

    /// <summary>Fügt Stellungen als neue Linien ein (Memo-Text, eine Stellung je Zeile, optional
    /// nummeriert + Kommentar); ein noch nicht vorhandenes Kapitel entsteht dadurch. Nur Besitzer/Admin
    /// (403). Antwortet mit der Anzahl übernommener Zeilen + je verworfener Zeile einem Grund.</summary>
    [HttpPost("{bookId:int}/lines")]
    public async Task<ActionResult<AddCourseLinesResultDto>> AddLines(int bookId,
        [FromBody] AddCourseLinesDto dto, CancellationToken ct)
    {
        return Ok(await _authoring.AddLinesAsync(GetUserId(), bookId, dto, IsAdmin, ct));
    }

    /// <summary>Löscht eine einzelne Linie des Buchs (samt abhängiger Nutzerdaten). Nur Besitzer/Admin.</summary>
    [HttpDelete("{bookId:int}/lines/{lineId:int}")]
    public async Task<IActionResult> DeleteLine(int bookId, int lineId, CancellationToken ct)
    {
        await _authoring.DeleteLineAsync(GetUserId(), bookId, lineId, IsAdmin, ct);
        return NoContent();
    }

    /// <summary>Benennt ein Kapitel um (leerer neuer Name = „ohne Kapitel"). Nur Besitzer/Admin;
    /// 400 wenn der Zielname schon existiert.</summary>
    [HttpPut("{bookId:int}/chapters/rename")]
    public async Task<IActionResult> RenameChapter(int bookId, [FromBody] RenameCourseChapterDto dto,
        CancellationToken ct)
    {
        return Ok(new { updated = await _authoring.RenameChapterAsync(GetUserId(), bookId, dto, IsAdmin, ct) });
    }

    /// <summary>Löscht ein ganzes Kapitel = alle seine Linien. Nur Besitzer/Admin.</summary>
    [HttpPost("{bookId:int}/chapters/delete")]
    public async Task<IActionResult> DeleteChapter(int bookId, [FromBody] CourseChapterRefDto dto,
        CancellationToken ct)
    {
        return Ok(new { deleted = await _authoring.DeleteChapterAsync(GetUserId(), bookId, dto.Chapter, IsAdmin, ct) });
    }

    /// <summary>Setzt den EIGENEN Fortschritt eines Kapitels zurück (gelöste Linien, Zeit-/Versuchs-Log,
    /// gesehene Info-Linien). Braucht nur Lese-Zugriff auf den Kurs; das buchweite `ResetAt` und die
    /// eigenen Analysebäume bleiben unberührt.</summary>
    [HttpPost("{bookId:int}/chapters/reset")]
    public async Task<IActionResult> ResetChapter(int bookId, [FromBody] CourseChapterRefDto dto,
        CancellationToken ct)
    {
        return Ok(new { cleared = await _authoring.ResetChapterProgressAsync(GetUserId(), bookId, dto.Chapter, IsAdmin, ct) });
    }
}
