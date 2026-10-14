using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Authorization;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Ebooks;

namespace RookHub.Api.Controllers.Ebooks;

/// <summary>
/// E-Book-Bibliothek (<c>/ebooks</c>): Übersicht, Hochladen, Metadaten + Freigabe je Buch, Lesestand, Datei und
/// Titelbild. Die beiden DATEI-Endpunkte sind <c>[AllowAnonymous]</c>, weil Bild-Tags, Download-Links und der
/// PDF-Leser keinen Bearer schicken können — sie weisen sich mit <c>?t=</c> aus (<see cref="EbookAccessTokens"/>),
/// und jede Anfrage prüft die Sichtbarkeit des Buchs für genau diesen Nutzer. Ohne Ausweis UND ohne Bearer: 401.
///
/// <para><b>Ohne Anmeldung</b> (0.778.0): Übersicht, Buch, Diagramme, Lesestand, Ausweis, Hochladen und das Verwalten
/// der eigenen anonymen Uploads gehen auch ohne Konto — der Aufrufer ist dann die anonyme Kennung aus <c>X-Visitor-Id</c>
/// (<see cref="EbookCaller"/>). Freigaben und Meldungen bleiben angemeldeten Nutzern vorbehalten. Beim Anmelden holt
/// <c>POST claim-session</c> die anonymen Uploads und Lesestände ins Konto.</para>
/// </summary>
[ApiController]
[Route("api/ebooks")]
[Authorize]
public class EbooksController : BaseApiController
{
    private readonly EbookService _service;
    private readonly EbookAccessTokens _tokens;
    private readonly AppDbContext _db;
    private readonly PermissionResolver? _permissions;

    public EbooksController(EbookService service, EbookAccessTokens tokens, AppDbContext db, PermissionResolver? permissions = null)
    {
        _service = service;
        _tokens = tokens;
        _db = db;
        _permissions = permissions;
    }

    /// <summary>Admin oder <c>ebooks.manage</c>: sieht und verwaltet alles (nur angemeldet).</summary>
    private async Task<bool> ManageAllAsync() => IsSignedIn && await HasPermissionAsync(_permissions, Permissions.EbooksManage);

    private bool IsSignedIn => User?.Identity?.IsAuthenticated == true;

    /// <summary>Angemeldet = Konto, sonst die anonyme Kennung aus <c>X-Visitor-Id</c> (ungültige Form = keine).</summary>
    private EbookCaller Caller =>
        IsSignedIn ? GetUserId() : EbookCaller.Anonymous(Request?.Headers["X-Visitor-Id"].FirstOrDefault());

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<EbookListDto>> List([FromQuery] EbookQuery query, CancellationToken ct)
        => Ok(await _service.ListAsync(Caller, await ManageAllAsync(), query, ct));

    /// <summary>Gruppen, die der Aufrufer freigeben kann (Verwalter alle, sonst die eigenen + „Everyone"). Literal vor <c>{id}</c>.</summary>
    [HttpGet("groups")]
    public async Task<ActionResult<List<EbookGroupOptionDto>>> GroupOptions(CancellationToken ct)
        => Ok(await _service.GroupOptionsAsync(GetUserId(), await ManageAllAsync(), ct));

    /// <summary>Kurzlebiger Ausweis für <c>…/file</c> und <c>…/cover</c> (12 h).</summary>
    [HttpGet("access-token")]
    [AllowAnonymous]
    public ActionResult<EbookAccessTokenDto> AccessToken()
    {
        var (token, expires) = _tokens.Issue(Caller);
        return Ok(new EbookAccessTokenDto { Token = token, ExpiresAt = expires });
    }

    /// <summary>Hochladen (Multipart: <c>file</c> + Metadaten). Grenze 1 GiB je Datei — nginx hält dieselbe
    /// (<c>location ^~ /api/ebooks</c>, DeploymentConfigTests).</summary>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-write")]
    [RequestSizeLimit(EbookService.MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = EbookService.MaxUploadRequestBytes)]
    public async Task<ActionResult<EbookDto>> Upload([FromForm] EbookUploadForm form, CancellationToken ct)
    {
        if (form.File == null || form.File.Length == 0)
            return BadRequest(new { message = "A file is required." });
        var meta = new EbookMetaDto
        {
            Title = form.Title,
            Author = form.Author,
            Year = form.Year,
            Tags = EbookFormats.ParseTagCsv(form.Tags),
            Description = form.Description,
            TargetMinElo = form.TargetMinElo,
            DefaultBoardMode = form.DefaultBoardMode,
            GroupIds = ParseIds(form.GroupIds),
        };
        await using var stream = form.File.OpenReadStream();
        var dto = await _service.UploadAsync(Caller, await ManageAllAsync(), stream, form.File.FileName, form.File.Length, meta, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    private static List<int> ParseIds(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var i) ? i : (int?)null)
                .Where(i => i != null).Select(i => i!.Value).Distinct().ToList();

    /// <summary>Freigabe mehrerer Bücher auf einmal → <c>{ updated }</c>. Literal vor <c>{id}</c>.</summary>
    [HttpPut("groups")]
    public async Task<IActionResult> SetGroups([FromBody] SetEbookGroupsDto dto, CancellationToken ct)
        => Ok(new { updated = await _service.SetGroupsAsync(GetUserId(), await ManageAllAsync(), dto.EbookIds, dto.GroupIds, ct) });

    /// <summary>Anonyme Uploads und Lesestände dieser Kennung ins Konto übernehmen (beim Anmelden) → <c>{ claimed }</c>.
    /// Literal vor <c>{id}</c>.</summary>
    [HttpPost("claim-session")]
    public async Task<IActionResult> ClaimSession([FromBody] ClaimEbookSessionDto dto, CancellationToken ct)
        => Ok(new { claimed = await _service.ClaimAnonymousAsync(GetUserId(), dto.SessionId ?? string.Empty, ct) });

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<EbookDto>> Get(int id, CancellationToken ct)
        => Ok(await _service.GetAsync(Caller, await ManageAllAsync(), id, ct));

    [HttpPut("{id:int}")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-write")]
    public async Task<ActionResult<EbookDto>> Update(int id, [FromBody] EbookMetaDto meta, CancellationToken ct)
        => Ok(await _service.UpdateAsync(Caller, await ManageAllAsync(), id, meta, ct));

    [HttpDelete("{id:int}")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-write")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(Caller, await ManageAllAsync(), id, ct);
        return NoContent();
    }

    /// <summary>Erkannte Diagramme (Lage als Seitenanteil, FEN, Nummer, Überschrift) — für die Doppelklick-Zonen des Lesers.</summary>
    [HttpGet("{id:int}/diagrams")]
    [AllowAnonymous]
    public async Task<ActionResult<EbookDiagramListDto>> GetDiagrams(int id, CancellationToken ct)
        => Ok(await _service.GetDiagramsAsync(Caller, await ManageAllAsync(), id, ct));

    /// <summary>„Als Kurs anlegen“ (0.785.0): persönlicher Kurs aus den Diagrammen des Buchs → der neue Kurs.</summary>
    [HttpPost("{id:int}/course")]
    public async Task<ActionResult<RookHub.Api.DTOs.Courses.CourseListItemDto>> CreateCourse(int id, [FromServices] EbookCourseBuilder builder, CancellationToken ct)
        => Ok(await builder.CreateCourseAsync(Caller, await ManageAllAsync(), id, ct));

    /// <summary>„Diagramm falsch" melden → <c>{ reported: true }</c>; optional <c>{ note }</c>.</summary>
    [HttpPost("{id:int}/diagrams/{diagramId:int}/report")]
    public async Task<IActionResult> ReportDiagram(int id, int diagramId, [FromBody] EbookDiagramReportRequest? body, CancellationToken ct)
        => Ok(new { reported = await _service.ReportDiagramAsync(GetUserId(), await ManageAllAsync(), id, diagramId, true, body?.Note, ct) });

    /// <summary>Eigene Meldung zurücknehmen → <c>{ reported: false }</c>.</summary>
    [HttpDelete("{id:int}/diagrams/{diagramId:int}/report")]
    public async Task<IActionResult> WithdrawDiagramReport(int id, int diagramId, CancellationToken ct)
        => Ok(new { reported = await _service.ReportDiagramAsync(GetUserId(), await ManageAllAsync(), id, diagramId, false, null, ct) });

    [HttpGet("{id:int}/progress")]
    [AllowAnonymous]
    public async Task<ActionResult<EbookProgressDto?>> GetProgress(int id, CancellationToken ct)
        => Ok(await _service.GetProgressAsync(Caller, await ManageAllAsync(), id, ct));

    [HttpPut("{id:int}/progress")]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-write")]
    public async Task<ActionResult<EbookProgressDto>> SetProgress(int id, [FromBody] EbookProgressUpdateDto dto, CancellationToken ct)
        => Ok(await _service.SetProgressAsync(Caller, await ManageAllAsync(), id, dto.Page, dto.PageCount, ct, dto.Locator));

    /// <summary>Wer fragt: Bearer (angemeldet) oder Ausweis <c>?t=</c> (Konto oder anonyme Kennung); <c>null</c> = weder noch.</summary>
    private async Task<(EbookCaller Caller, bool ManageAll)?> ResolveCallerAsync(string? token, CancellationToken ct)
    {
        if (IsSignedIn)
            return (GetUserId(), await ManageAllAsync());
        if (!_tokens.TryRead(token, out var tokenCaller)) return null;
        if (tokenCaller.UserId is not int userId) return (tokenCaller, false);
        var user = await _db.AppUsers.Where(u => u.Id == userId && u.DeletedAt == null).Select(u => new { u.IsAdmin }).FirstOrDefaultAsync(ct);
        if (user == null) return null;
        var manageAll = user.IsAdmin || (_permissions != null && (await _permissions.GetAsync(userId)).Has(Permissions.EbooksManage));
        return (userId, manageAll);
    }

    /// <summary>Die Datei (Hauptdatei oder <c>?fileId=</c>), mit Bereichsanfragen für den PDF-Leser; <c>?download=1</c>
    /// als Anhang mit dem ursprünglichen Namen.</summary>
    [HttpGet("{id:int}/file")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPartitions.EbookFilePolicy)]
    public async Task<IActionResult> GetFile(int id, [FromQuery] int? fileId, [FromQuery(Name = "t")] string? token,
        [FromQuery] bool download = false, CancellationToken ct = default)
    {
        var caller = await ResolveCallerAsync(token, ct);
        if (caller == null) return Unauthorized();
        var (ebook, file, fullPath) = await _service.ResolveFileAsync(caller.Value.Caller, caller.Value.ManageAll, id, fileId, ct);
        Response.Headers.CacheControl = "private, max-age=3600";
        var contentType = EbookFormats.ContentType(file.Format);
        if (download)
            return PhysicalFile(fullPath, contentType, DownloadName(ebook, file), enableRangeProcessing: true);
        // Inline: PDF zeigt der Browser/der Leser; der Dateiname steht trotzdem im Header (Speichern-unter).
        Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(DownloadName(ebook, file))}";
        return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
    }

    /// <summary>
    /// Ein einzelner Eintrag aus dem EPUB-ZIP (<see cref="EbookEpubEntries"/>): der EPUB-Leser öffnet das Buch als Ordner
    /// unter <c>…/epub/{token}/</c> und holt nur Container, OPF, Navigation, das gezeigte Kapitel und dessen Bilder. Der
    /// Ausweis steht im PFAD, damit epub.js relative Adressen (Bilder, CSS, Kapitel) ohne Query-Parameter auflösen kann;
    /// Prüfung wie bei <see cref="GetFile"/> (Bearer oder Ausweis, Sichtbarkeit je Anfrage). Fehlender Eintrag → 404.
    /// </summary>
    [HttpGet("{id:int}/epub/{token}/{**path}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPartitions.EbookEntriesPolicy)]
    public async Task<IActionResult> GetEpubEntry(int id, string token, string? path, CancellationToken ct)
    {
        var caller = await ResolveCallerAsync(token, ct);
        if (caller == null) return Unauthorized();
        var file = await _service.ResolveEpubAsync(caller.Value.Caller, caller.Value.ManageAll, id, ct);
        var entry = await EbookEpubEntries.ReadAsync(file, path, ct);
        if (entry == null) return NotFound();
        // Der Inhalt eines Buchs ändert sich nicht; der Ausweis im Pfad wechselt ohnehin alle 12 h.
        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(entry.Bytes, entry.ContentType);
    }

    private const string UnsafeNameChars = "<>:\"/\\|?*";

    /// <summary>Dateiname fürs Speichern: Titel + Format (sicher für Dateisysteme), sonst der Originalname.</summary>
    internal static string DownloadName(Ebook ebook, EbookFile file)
    {
        var baseName = string.IsNullOrWhiteSpace(ebook.Title) ? Path.GetFileNameWithoutExtension(file.FileName) : ebook.Title;
        // Feste Liste statt Path.GetInvalidFileNameChars(): die kennt unter Linux nur „/" und NUL — der Download landet
        // aber meist auf Windows/Android, wo „:" und „?" im Namen scheitern.
        var cleaned = new string(baseName.Select(c => c < ' ' || UnsafeNameChars.Contains(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.Length > 120) cleaned = cleaned[..120].TrimEnd();
        return (cleaned.Length == 0 ? "ebook" : cleaned) + "." + file.Format;
    }

    /// <summary>Titelbild (JPEG/PNG, wie es gerendert wurde) oder 404, solange keines da ist.</summary>
    [HttpGet("{id:int}/cover")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPartitions.EbookFilePolicy)]
    public async Task<IActionResult> GetCover(int id, [FromQuery(Name = "t")] string? token, CancellationToken ct)
    {
        var caller = await ResolveCallerAsync(token, ct);
        if (caller == null) return Unauthorized();
        var path = await _service.ResolveCoverAsync(caller.Value.Caller, caller.Value.ManageAll, id, ct);
        if (path == null) return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(path, SniffImageType(path));
    }

    /// <summary>JPEG/PNG/WebP/GIF an den ersten Bytes erkennen — die Datei heißt immer <c>.cover</c>.</summary>
    internal static string SniffImageType(string path)
    {
        try
        {
            Span<byte> head = stackalloc byte[12];
            using var fs = System.IO.File.OpenRead(path);
            var n = fs.Read(head);
            if (n >= 3 && head[0] == 0xFF && head[1] == 0xD8) return "image/jpeg";
            if (n >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) return "image/png";
            if (n >= 6 && head[0] == (byte)'G' && head[1] == (byte)'I' && head[2] == (byte)'F') return "image/gif";
            if (n >= 12 && head[0] == (byte)'R' && head[1] == (byte)'I' && head[2] == (byte)'F' && head[3] == (byte)'F'
                && head[8] == (byte)'W' && head[9] == (byte)'E' && head[10] == (byte)'B' && head[11] == (byte)'P') return "image/webp";
        }
        catch (IOException) { /* unten */ }
        return "application/octet-stream";
    }
}
