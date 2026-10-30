using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Authorization;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;

namespace RookHub.Api.Controllers.Ebooks;

/// <summary>Verwaltung der E-Book-Bibliothek (<c>ebooks.manage</c>): die Bibliothek auf dem Server einlesen.</summary>
[ApiController]
[Route("api/admin/ebooks")]
[HasPermission(Permissions.EbooksManage)]
public class EbooksAdminController : BaseApiController
{
    private readonly EbookLibraryImporter _importer;
    private readonly EbookStorage _storage;
    private readonly AppDbContext _db;
    private readonly EbookService _service;
    private readonly EbookReflowService _reflow;

    public EbooksAdminController(EbookLibraryImporter importer, EbookStorage storage, AppDbContext db, EbookService service, EbookReflowService reflow)
    {
        _importer = importer;
        _storage = storage;
        _db = db;
        _service = service;
        _reflow = reflow;
    }

    /// <summary>Partiensammlungen (0.831.0): Partien eines Buchs setzen und Diagramme darauf zeigen lassen
    /// (<see cref="EbookGamesImportDto"/>) → Zähler. Unbekanntes Buch → 404.</summary>
    [HttpPut("{id:int}/games")]
    public async Task<ActionResult<EbookGamesImportResultDto>> ImportGames(int id, [FromBody] EbookGamesImportDto dto, [FromServices] EbookGameService games, CancellationToken ct)
        => Ok(await games.ImportAsync(id, dto, ct));

    /// <summary>Eröffnungsbaum (0.881.0) setzen bzw. ersetzen: <c>{ nodes: [[parentIndex, san], …] }</c> → <c>{ ebookId, nodes }</c>.
    /// Ungültig → 400 <c>ebook_tree_invalid</c>, unbekanntes Buch → 404.</summary>
    [HttpPut("{id:int}/tree")]
    public async Task<IActionResult> SetTree(int id, [FromBody] EbookTreeDto dto, [FromServices] EbookTreeService trees, CancellationToken ct)
        => Ok(new { ebookId = id, nodes = await trees.SetAsync(id, dto, ct) });

    /// <summary>Eröffnungsbaum entfernen → 204 (auch ohne Baum); unbekanntes Buch → 404.</summary>
    [HttpDelete("{id:int}/tree")]
    public async Task<IActionResult> DeleteTree(int id, [FromServices] EbookTreeService trees, CancellationToken ct)
    {
        await trees.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Gemeldete falsche Diagramme (neueste zuerst, höchstens 500) — Grundlage für Font-Korrekturen.</summary>
    [HttpGet("diagram-reports")]
    public async Task<ActionResult<List<EbookDiagramReportDto>>> DiagramReports(CancellationToken ct)
        => Ok(await _service.ListDiagramReportsAsync(ct));

    /// <summary>Diagramm-Erkennung aller PDF-Bücher neu anstoßen (nach neuen Font-Belegungen) → <c>{ queued }</c>.</summary>
    [HttpPost("diagrams/rescan")]
    public async Task<IActionResult> RescanDiagrams(CancellationToken ct)
        => Ok(new { queued = await _service.RescanDiagramsAsync(ct) });

    /// <summary>
    /// Diagramme EINES Buchs ergänzen statt ersetzen (0.811.0): die Erkennung läuft erneut (auch bei kuratierten Büchern),
    /// vorhandene Diagramme bleiben unverändert, neue kommen nur an freien Stellen dazu; unplausible Lesungen werden als
    /// <c>FontFamily = "unread"</c> aufgehoben (für Nutzer unsichtbar). 202 <c>{ ebookId, queued }</c>, unbekannt → 404.
    /// </summary>
    [HttpPost("{id:int}/diagrams/supplement")]
    public async Task<IActionResult> SupplementDiagrams(int id, CancellationToken ct)
    {
        await _service.QueueDiagramSupplementAsync(id, ct);
        return Accepted(new { ebookId = id, queued = true });
    }

    /// <summary>Zugrecht aus „White/Black to move“ unter den Brettern nachtragen (0.823.0) — nur Diagramme ohne Zugrecht,
    /// das Buch gilt danach NICHT als kuratiert → <c>{ ebookId, checked, set, changed }</c>; unbekannt → 404.</summary>
    [HttpPost("{id:int}/diagrams/side-from-captions")]
    public async Task<ActionResult<EbookSideFromCaptions.Outcome>> SideFromCaptions(int id, CancellationToken ct)
        => Ok(await EbookSideFromCaptions.ApplyAsync(_db, _storage, id, ct));

    /// <summary>
    /// Fließtext-EPUB aus dem Text-PDF des Buchs bauen (0.802.0, Prototyp; synchron, ~10–20 s für 400 Seiten): abgeleitete
    /// Datei <c>converted/{id}.epub</c>, Diagramme als Brettbilder, ein zweiter Aufruf ersetzt sie. 400
    /// <c>ebook_reflow_unsupported</c> ohne Original-PDF, mit echtem EPUB, vor der Diagrammerkennung oder bei einem Scan.
    /// </summary>
    [HttpPost("{id:int}/reflow")]
    public async Task<ActionResult<EbookReflowResultDto>> Reflow(int id, CancellationToken ct)
    {
        var r = await _reflow.ConvertAsync(id, ct);
        return Ok(new EbookReflowResultDto
        {
            EbookId = r.EbookId, FileId = r.FileId, SizeBytes = r.SizeBytes, Pages = r.Pages, Chapters = r.Chapters,
            Paragraphs = r.Paragraphs, Diagrams = r.Diagrams, DiagramsLinked = r.DiagramsLinked,
        });
    }

    /// <summary>Buch-Übersetzung (0.876.0): Übersicht — Sprachen der Bücher, Stand je Zielsprache, laufendes Buch mit
    /// Restdauer, die nächsten in der Reihe, zuletzt gescheiterte. Literal vor <c>{id}</c>.</summary>
    [HttpGet("translations")]
    public async Task<ActionResult<EbookTranslationStatusDto>> TranslationStatus([FromServices] EbookTranslationService translations, CancellationToken ct)
        => Ok(await translations.StatusAsync(ct));

    /// <summary>Gescheiterte und unvollständige Übersetzungen wieder einreihen → <c>{ queued }</c>.</summary>
    [HttpPost("translations/retry-failed")]
    public async Task<IActionResult> RetryFailedTranslations([FromServices] EbookTranslationService translations, CancellationToken ct)
        => Ok(new { queued = await translations.RetryFailedAsync(ct) });

    /// <summary>Ein Buch vorziehen bzw. neu übersetzen lassen (nächster Lauf außerhalb der Sperrzeit) → die Zeile.
    /// Buch schon in der Sprache → 400, unbekannt → 404.</summary>
    [HttpPost("{id:int}/translations/{lang}")]
    public async Task<ActionResult<EbookTranslationItemDto>> EnqueueTranslation(int id, string lang, [FromServices] EbookTranslationService translations, CancellationToken ct)
        => Ok(await translations.EnqueueAsync(id, lang, ct));

    /// <summary>Ist die Bibliothek konfiguriert, wie viel ist eingelesen, wie viele Titelbilder stehen noch aus.</summary>
    [HttpGet("library")]
    public async Task<ActionResult<EbookLibraryStatusDto>> LibraryStatus(CancellationToken ct)
        => Ok(new EbookLibraryStatusDto
        {
            Configured = _storage.LibraryEnabled,
            UploadsEnabled = _storage.UploadsEnabled,
            Imported = await _db.Ebooks.CountAsync(b => b.Source == EbookSource.Library, ct),
            CoversPending = await _db.Ebooks.CountAsync(b => b.CoverState == EbookCoverState.Pending, ct),
            DiagramsPending = await _db.Ebooks.CountAsync(b => b.DiagramState == EbookDiagramState.Pending, ct),
        });

    /// <summary>Bibliothek einlesen (idempotent; Dateien bleiben an Ort und Stelle). Titelbilder folgen im Hintergrund.</summary>
    [HttpPost("library/import")]
    public async Task<ActionResult<EbookLibraryImportResultDto>> ImportLibrary(CancellationToken ct)
        => Ok(await _importer.ImportAsync(GetUserId(), ct));
}
