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

    public EbooksAdminController(EbookLibraryImporter importer, EbookStorage storage, AppDbContext db, EbookService service)
    {
        _importer = importer;
        _storage = storage;
        _db = db;
        _service = service;
    }

    /// <summary>Gemeldete falsche Diagramme (neueste zuerst, höchstens 500) — Grundlage für Font-Korrekturen.</summary>
    [HttpGet("diagram-reports")]
    public async Task<ActionResult<List<EbookDiagramReportDto>>> DiagramReports(CancellationToken ct)
        => Ok(await _service.ListDiagramReportsAsync(ct));

    /// <summary>Diagramm-Erkennung aller PDF-Bücher neu anstoßen (nach neuen Font-Belegungen) → <c>{ queued }</c>.</summary>
    [HttpPost("diagrams/rescan")]
    public async Task<IActionResult> RescanDiagrams(CancellationToken ct)
        => Ok(new { queued = await _service.RescanDiagramsAsync(ct) });

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
