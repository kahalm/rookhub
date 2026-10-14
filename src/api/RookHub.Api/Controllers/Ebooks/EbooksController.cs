using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Authorization;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Ebooks;

namespace RookHub.Api.Controllers.Ebooks;

/// <summary>
/// E-Book-Bibliothek (<c>/ebooks</c>): Übersicht, Hochladen, Metadaten + Freigabe je Buch, Lesestand, Datei und
/// Titelbild. Die beiden DATEI-Endpunkte sind <c>[AllowAnonymous]</c>, weil Bild-Tags, Download-Links und der
/// PDF-Leser keinen Bearer schicken können — sie weisen sich mit <c>?t=</c> aus (<see cref="EbookAccessTokens"/>),
/// und jede Anfrage prüft die Sichtbarkeit des Buchs für genau diesen Nutzer. Ohne Ausweis UND ohne Bearer: 401.
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

    /// <summary>Admin oder <c>ebooks.manage</c>: sieht und verwaltet alles.</summary>
    private Task<bool> ManageAllAsync() => HasPermissionAsync(_permissions, Permissions.EbooksManage);

    [HttpGet]
    public async Task<ActionResult<EbookListDto>> List([FromQuery] EbookQuery query, CancellationToken ct)
        => Ok(await _service.ListAsync(GetUserId(), await ManageAllAsync(), query, ct));

    /// <summary>Gruppen, die der Aufrufer freigeben kann (Verwalter alle, sonst die eigenen + „Everyone"). Literal vor <c>{id}</c>.</summary>
    [HttpGet("groups")]
    public async Task<ActionResult<List<EbookGroupOptionDto>>> GroupOptions(CancellationToken ct)
        => Ok(await _service.GroupOptionsAsync(GetUserId(), await ManageAllAsync(), ct));

    /// <summary>Kurzlebiger Ausweis für <c>…/file</c> und <c>…/cover</c> (12 h).</summary>
    [HttpGet("access-token")]
    public ActionResult<EbookAccessTokenDto> AccessToken()
    {
        var (token, expires) = _tokens.Issue(GetUserId());
        return Ok(new EbookAccessTokenDto { Token = token, ExpiresAt = expires });
    }

    /// <summary>Hochladen (Multipart: <c>file</c> + Metadaten). Grenze 1 GiB je Datei — nginx hält dieselbe
    /// (<c>location ^~ /api/ebooks</c>, DeploymentConfigTests).</summary>
    [HttpPost]
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
            GroupIds = ParseIds(form.GroupIds),
        };
        await using var stream = form.File.OpenReadStream();
        var dto = await _service.UploadAsync(GetUserId(), await ManageAllAsync(), stream, form.File.FileName, form.File.Length, meta, ct);
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

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EbookDto>> Get(int id, CancellationToken ct)
        => Ok(await _service.GetAsync(GetUserId(), await ManageAllAsync(), id, ct));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<EbookDto>> Update(int id, [FromBody] EbookMetaDto meta, CancellationToken ct)
        => Ok(await _service.UpdateAsync(GetUserId(), await ManageAllAsync(), id, meta, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(GetUserId(), await ManageAllAsync(), id, ct);
        return NoContent();
    }

    /// <summary>Erkannte Diagramme (Lage als Seitenanteil, FEN, Nummer, Überschrift) — für die Doppelklick-Zonen des Lesers.</summary>
    [HttpGet("{id:int}/diagrams")]
    public async Task<ActionResult<EbookDiagramListDto>> GetDiagrams(int id, CancellationToken ct)
        => Ok(await _service.GetDiagramsAsync(GetUserId(), await ManageAllAsync(), id, ct));

    [HttpGet("{id:int}/progress")]
    public async Task<ActionResult<EbookProgressDto?>> GetProgress(int id, CancellationToken ct)
        => Ok(await _service.GetProgressAsync(GetUserId(), await ManageAllAsync(), id, ct));

    [HttpPut("{id:int}/progress")]
    public async Task<ActionResult<EbookProgressDto>> SetProgress(int id, [FromBody] EbookProgressUpdateDto dto, CancellationToken ct)
        => Ok(await _service.SetProgressAsync(GetUserId(), await ManageAllAsync(), id, dto.Page, dto.PageCount, ct));

    /// <summary>Wer fragt: Bearer (angemeldet) oder Ausweis <c>?t=</c>; <c>null</c> = weder noch.</summary>
    private async Task<(int UserId, bool ManageAll)?> ResolveCallerAsync(string? token, CancellationToken ct)
    {
        if (User?.Identity?.IsAuthenticated == true)
            return (GetUserId(), await ManageAllAsync());
        if (!_tokens.TryRead(token, out var userId)) return null;
        var user = await _db.AppUsers.Where(u => u.Id == userId && u.DeletedAt == null).Select(u => new { u.IsAdmin }).FirstOrDefaultAsync(ct);
        if (user == null) return null;
        var manageAll = user.IsAdmin || (_permissions != null && (await _permissions.GetAsync(userId)).Has(Permissions.EbooksManage));
        return (userId, manageAll);
    }

    /// <summary>Die Datei (Hauptdatei oder <c>?fileId=</c>), mit Bereichsanfragen für den PDF-Leser; <c>?download=1</c>
    /// als Anhang mit dem ursprünglichen Namen.</summary>
    [HttpGet("{id:int}/file")]
    [AllowAnonymous]
    public async Task<IActionResult> GetFile(int id, [FromQuery] int? fileId, [FromQuery(Name = "t")] string? token,
        [FromQuery] bool download = false, CancellationToken ct = default)
    {
        var caller = await ResolveCallerAsync(token, ct);
        if (caller == null) return Unauthorized();
        var (ebook, file, fullPath) = await _service.ResolveFileAsync(caller.Value.UserId, caller.Value.ManageAll, id, fileId, ct);
        Response.Headers.CacheControl = "private, max-age=3600";
        var contentType = EbookFormats.ContentType(file.Format);
        if (download)
            return PhysicalFile(fullPath, contentType, DownloadName(ebook, file), enableRangeProcessing: true);
        // Inline: PDF zeigt der Browser/der Leser; der Dateiname steht trotzdem im Header (Speichern-unter).
        Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(DownloadName(ebook, file))}";
        return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
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
    public async Task<IActionResult> GetCover(int id, [FromQuery(Name = "t")] string? token, CancellationToken ct)
    {
        var caller = await ResolveCallerAsync(token, ct);
        if (caller == null) return Unauthorized();
        var path = await _service.ResolveCoverAsync(caller.Value.UserId, caller.Value.ManageAll, id, ct);
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
