using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.League;
using Actor = RookHub.Api.Services.ScoresheetScanService.ScanActor;

namespace RookHub.Api.Controllers;

/// <summary>
/// LeagueHub — Vereins-Datenbank (Wunsch 2026-09-28): lesen mit <see cref="Permissions.LeagueView"/>, beitragen (PGN
/// hochladen, Partieformular einlesen, eigene Partien löschen) mit <see cref="Permissions.LeagueContribute"/>. Beides
/// bekommt die Rolle der Vereinsmitglieder; löschen darf jeder Verwalter (<see cref="Permissions.LeagueManage"/>) alles,
/// sonst nur eigene Partien ohne „Schwaz" — bei den anderen ist nicht gespeichert, von wem sie stammen. Derselbe Weg
/// OHNE Anmeldung über einen Teilen-Link: <see cref="LeagueShareClubController"/>. Regeln: <see cref="LeagueClubService"/>.
/// </summary>
[ApiController]
[Route("api/league/club")]
[Authorize]
public class LeagueClubController : BaseApiController
{
    private readonly LeagueClubService _club;
    private readonly ScoresheetScanService _scans;
    private readonly ScoresheetScanSignal _signal;

    public LeagueClubController(LeagueClubService club, ScoresheetScanService scans, ScoresheetScanSignal signal)
    {
        _club = club; _scans = scans; _signal = signal;
    }

    private bool CanManage => User.IsInRole("Admin")
        || User.HasClaim(PermissionAuthorizationHandler.PermissionClaimType, Permissions.LeagueManage);

    private Actor Me => Actor.User(GetUserId());

    [HttpGet("games")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<ActionResult<LeagueClubListDto>> List([FromQuery] string? fide, [FromQuery] string? q,
        [FromQuery] int page = 1, CancellationToken ct = default) =>
        Ok(await _club.ListAsync(GetUserId(), CanManage, fide, q, page, ct));

    [HttpGet("games/pgn")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Export([FromQuery] string? fide, [FromQuery] string? q, CancellationToken ct) =>
        File(Encoding.UTF8.GetBytes(await _club.ExportAsync(fide, q, ct)), "application/x-chess-pgn", "vereinspartien.pgn");

    /// <summary>PGN lesen und zuordnen, NICHTS speichern → Übersicht; 400 <c>empty</c>/<c>tooLarge</c>.</summary>
    [HttpPost("games/preview")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public async Task<ActionResult<LeagueClubPreviewDto>> Preview([FromBody] LeagueClubPreviewRequest req, CancellationToken ct) =>
        ClubUpload.CheckPgn(req?.Pgn) is { } bad ? BadRequest(bad) : Ok(await _club.PreviewAsync(GetUserId(), req!.Pgn, ct));

    /// <summary>PGN übernehmen, je Partie mit den Entscheidungen aus der Übersicht → <see cref="LeagueClubImportResultDto"/>.</summary>
    [HttpPost("games/import")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public async Task<ActionResult<LeagueClubImportResultDto>> Import([FromBody] LeagueClubImportRequest req, CancellationToken ct) =>
        ClubUpload.CheckPgn(req?.Pgn) is { } bad ? BadRequest(bad) : Ok(await _club.ImportPgnAsync(GetUserId(), req!.Pgn, req.Games, ct));

    /// <summary>Eine Partie (aus einem Partieformular). 400 mit <c>reason</c> wie beim Import, dazu <c>duplicate</c>;
    /// die Einlesung (<c>scanId</c>) wird danach geschlossen — ihr Foto verschwindet.</summary>
    [HttpPost("games")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<IActionResult> Add([FromBody] LeagueClubGameRequest req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { reason = "empty", message = "Body required." });
        var (game, reason, message) = await _club.AddGameAsync(GetUserId(), req, ct);
        if (game == null) return BadRequest(new { reason, message = message ?? "Game not accepted." });
        if (req.ScanId is { } scanId) await _scans.CloseLeagueScanAsync(Me, scanId);
        return Ok(new { id = game.Id, anonymized = game.Anonymized });
    }

    [HttpDelete("games/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await _club.DeleteAsync(GetUserId(), CanManage, id, ct) switch
        {
            LeagueClubService.DeleteResult.Deleted => NoContent(),
            LeagueClubService.DeleteResult.Forbidden => Forbid(),
            _ => NotFound(),
        };

    /// <summary>Ligaspieler zum Eintippen der Namen (ab zwei Buchstaben).</summary>
    [HttpGet("players")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<List<LeagueRosterPersonDto>>> Players([FromQuery] string? q, CancellationToken ct) =>
        Ok(string.IsNullOrWhiteSpace(q) ? new List<LeagueRosterPersonDto>() : await _club.SuggestAsync(q, ct));

    /// <summary>Stehen diese Namen in einer Meldeliste? Spieler von Schwaz?</summary>
    [HttpPost("match")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<LeagueClubMatchDto>> Match([FromBody] LeagueClubMatchRequest req, CancellationToken ct) =>
        Ok(await _club.MatchAsync(req?.White, req?.Black, ct));

    // ── Partieformular (dieselbe Einlesung wie in RookHub, aber ohne „Meine Partien") ─────────────

    /// <summary>Tageszahl dieses Wegs: <see cref="ScoresheetScanService.DefaultLeagueDailyLimit"/> je Nutzer.</summary>
    [HttpGet("scoresheet/status")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<ScoresheetStatusDto>> ScoresheetStatus() =>
        Ok(await _scans.StatusAsync(GetUserId(), ScoresheetScan.PurposeLeague));

    [HttpGet("scans")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<List<ScoresheetScanDto>>> Scans() => Ok(await _scans.LeagueScansAsync(GetUserId()));

    /// <summary>Foto hochladen (multipart <c>file</c>, <c>language</c>, <c>side</c>) — Absagen wie <c>POST /api/scoresheets</c>.</summary>
    [HttpPost("scans")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, [FromForm] string? language, [FromForm] string? side)
    {
        if (await ClubUpload.ReadAsync(file) is not { } data) return BadRequest(ClubUpload.FileError(file));
        var (scan, reason) = await _scans.CreateAsync(GetUserId(), data, file!.ContentType, file.FileName, language, side,
            ScoresheetScan.PurposeLeague);
        if (ClubUpload.Refusal(reason) is { } refused) return refused;
        _signal.Wake();
        return Accepted(scan);
    }

    [HttpGet("scans/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<LeagueScanStateDto>> Scan(int id, CancellationToken ct) =>
        await _scans.LeagueScanStateAsync(Me, id, ct) is { } s ? Ok(s) : NotFound();

    [HttpGet("scans/{id:int}/photo")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<IActionResult> Photo(int id) => ClubUpload.PhotoResult(this, await _scans.LeagueScanPhotoAsync(Me, id));

    [HttpPost("scans/{id:int}/resolve")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<ScoresheetResolveResultDto>> Resolve(int id, [FromBody] ScoresheetResolveRequestDto dto) =>
        await ClubUpload.ResolveAsync(this, () => _scans.ResolveLeagueRestAsync(Me, id, dto?.Prefix ?? new(), dto?.WrittenFrom ?? 0));

    /// <summary>Einlesung verwerfen: Foto und Lesung weg, die Zeile bleibt fürs Tageskontingent.</summary>
    [HttpDelete("scans/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<IActionResult> Discard(int id) => await _scans.CloseLeagueScanAsync(Me, id) ? NoContent() : NotFound();
}

/// <summary>
/// Dieselben Upload-Wege OHNE Anmeldung, über einen gültigen Teilen-Link (Wunsch 2026-09-28: „auf dem Link für die
/// nächste Aufstellung soll es auch die Option geben, Spiele hochzuladen — ohne Anmeldung"). Der Link ist der Nachweis;
/// Partieformulare zusätzlich höchstens <see cref="ScoresheetScanService.AnonPerIpDailyLimit"/> je IP und
/// <see cref="ScoresheetScanService.AnonDailyLimit"/> je Tag für alle zusammen. Eine Einlesung gehört dem Browser, der den
/// beim Hochladen ausgegebenen Schlüssel hat. Lesen der Vereinspartien gibt es hier NICHT.
/// </summary>
[ApiController]
[Route("api/league/s/{token}/club")]
[AllowAnonymous]
[EnableRateLimiting("anonymous-tournament")]
public class LeagueShareClubController : ControllerBase
{
    private readonly LeagueService _league;
    private readonly LeagueClubService _club;
    private readonly ScoresheetScanService _scans;
    private readonly ScoresheetScanSignal _signal;

    public LeagueShareClubController(LeagueService league, LeagueClubService club, ScoresheetScanService scans, ScoresheetScanSignal signal)
    {
        _league = league; _club = club; _scans = scans; _signal = signal;
    }

    private Task<bool> ValidAsync(string token, CancellationToken ct) => _league.ShareValidAsync(token, ct);
    private string IpHash => _scans.AnonIpHash(HttpContext.Connection.RemoteIpAddress);

    [HttpPost("games/preview")]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public async Task<ActionResult<LeagueClubPreviewDto>> Preview(string token, [FromBody] LeagueClubPreviewRequest req, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return ClubUpload.CheckPgn(req?.Pgn) is { } bad ? BadRequest(bad) : Ok(await _club.PreviewAsync(null, req!.Pgn, ct));
    }

    [HttpPost("games/import")]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public async Task<ActionResult<LeagueClubImportResultDto>> Import(string token, [FromBody] LeagueClubImportRequest req, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return ClubUpload.CheckPgn(req?.Pgn) is { } bad ? BadRequest(bad) : Ok(await _club.ImportPgnAsync(null, req!.Pgn, req.Games, ct));
    }

    /// <summary>Eine Partie aus einem Partieformular; <c>scanKey</c> = der Schlüssel der Einlesung (wird geschlossen).</summary>
    [HttpPost("games")]
    public async Task<IActionResult> Add(string token, [FromBody] LeagueClubGameRequest req, [FromQuery] string? scanKey, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        if (req is null) return BadRequest(new { reason = "empty", message = "Body required." });
        req.ScanId = null;
        var (game, reason, message) = await _club.AddGameAsync(null, req, ct);
        if (game == null) return BadRequest(new { reason, message = message ?? "Game not accepted." });
        if (!string.IsNullOrWhiteSpace(scanKey)) await _scans.CloseLeagueScanAsync(Actor.Anonymous(scanKey), null);
        return Ok(new { id = game.Id, anonymized = game.Anonymized });
    }

    [HttpGet("players")]
    public async Task<ActionResult<List<LeagueRosterPersonDto>>> Players(string token, [FromQuery] string? q, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return Ok(string.IsNullOrWhiteSpace(q) ? new List<LeagueRosterPersonDto>() : await _club.SuggestAsync(q, ct));
    }

    [HttpPost("match")]
    public async Task<ActionResult<LeagueClubMatchDto>> Match(string token, [FromBody] LeagueClubMatchRequest req, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return Ok(await _club.MatchAsync(req?.White, req?.Black, ct));
    }

    [HttpGet("scoresheet/status")]
    public async Task<ActionResult<ScoresheetStatusDto>> ScoresheetStatus(string token, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return Ok(await _scans.AnonStatusAsync(IpHash));
    }

    public sealed record KeysRequest(List<string>? Keys);

    /// <summary>Die offenen Einlesungen zu den Schlüsseln, die der Browser sich gemerkt hat.</summary>
    [HttpPost("scans/lookup")]
    public async Task<IActionResult> Lookup(string token, [FromBody] KeysRequest req, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        var found = await _scans.LeagueScansByKeysAsync(req?.Keys ?? new());
        return Ok(found.Select(f => new { key = f.Key, scan = f.Scan }));
    }

    /// <summary>Foto OHNE Konto hochladen → <c>{ key, scan }</c>. Absagen wie angemeldet, dazu <c>anonDailyLimit</c>.</summary>
    [HttpPost("scans")]
    [RequestSizeLimit(ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload(string token, IFormFile? file, [FromForm] string? language, [FromForm] string? side,
        CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        if (await ClubUpload.ReadAsync(file) is not { } data) return BadRequest(ClubUpload.FileError(file));
        var (scan, key, reason) = await _scans.CreateAnonymousAsync(data, file!.ContentType, file.FileName, language, side, IpHash);
        if (ClubUpload.Refusal(reason) is { } refused) return refused;
        _signal.Wake();
        return Accepted(new { key, scan });
    }

    [HttpGet("scans/{key}")]
    public async Task<ActionResult<LeagueScanStateDto>> Scan(string token, string key, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return await _scans.LeagueScanStateAsync(Actor.Anonymous(key), null, ct) is { } s ? Ok(s) : NotFound();
    }

    [HttpGet("scans/{key}/photo")]
    public async Task<IActionResult> Photo(string token, string key, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return ClubUpload.PhotoResult(this, await _scans.LeagueScanPhotoAsync(Actor.Anonymous(key), null));
    }

    [HttpPost("scans/{key}/resolve")]
    public async Task<ActionResult<ScoresheetResolveResultDto>> Resolve(string token, string key, [FromBody] ScoresheetResolveRequestDto dto,
        CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return await ClubUpload.ResolveAsync(this,
            () => _scans.ResolveLeagueRestAsync(Actor.Anonymous(key), null, dto?.Prefix ?? new(), dto?.WrittenFrom ?? 0));
    }

    [HttpDelete("scans/{key}")]
    public async Task<IActionResult> Discard(string token, string key, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return await _scans.CloseLeagueScanAsync(Actor.Anonymous(key), null) ? NoContent() : NotFound();
    }
}

/// <summary>Was beide Upload-Controller gleich machen.</summary>
internal static class ClubUpload
{
    public const int MaxBodyBytes = 12 * 1024 * 1024;

    public static object? CheckPgn(string? pgn) =>
        string.IsNullOrWhiteSpace(pgn) ? new { reason = "empty", message = "No PGN." }
        : pgn.Length > LeagueClubService.MaxImportChars ? new { reason = "tooLarge", message = "PGN too large." } : null;

    public static async Task<byte[]?> ReadAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0 || file.Length > ScoresheetScanService.MaxUploadBytes) return null;
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return ms.ToArray();
    }

    public static object FileError(IFormFile? file) => file == null || file.Length == 0
        ? new { reason = "noFile", message = "No file." } : new { reason = "tooLarge", message = "File too large." };

    public static IActionResult? Refusal(string? reason) => reason switch
    {
        null => null,
        "notConfigured" => new ObjectResult(new { reason, message = "Reading is not configured." }) { StatusCode = StatusCodes.Status503ServiceUnavailable },
        _ => new BadRequestObjectResult(new { reason, message = "Photo not accepted." }),
    };

    public static IActionResult PhotoResult(ControllerBase c, (byte[] Data, string ContentType)? p)
    {
        if (p is not { } photo) return c.NotFound();
        c.Response.Headers.CacheControl = "private, max-age=3600";
        return c.File(photo.Data, photo.ContentType);
    }

    public static async Task<ActionResult<ScoresheetResolveResultDto>> ResolveAsync(ControllerBase c,
        Func<Task<ScoresheetResolveResultDto?>> run)
    {
        try
        {
            var r = await run();
            return r == null ? c.NotFound() : c.Ok(r);
        }
        catch (ArgumentException ex) { return c.BadRequest(new { reason = "illegalMove", message = ex.Message }); }
    }
}
