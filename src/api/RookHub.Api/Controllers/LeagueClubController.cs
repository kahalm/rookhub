using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.League;

namespace RookHub.Api.Controllers;

/// <summary>
/// LeagueHub — Vereins-Datenbank (Wunsch 2026-09-28): lesen mit <see cref="Permissions.LeagueView"/>, beitragen (PGN
/// hochladen, Partieformular einlesen, eigene Partien löschen) mit <see cref="Permissions.LeagueContribute"/>. Beides
/// bekommt die Rolle der Vereinsmitglieder; löschen darf jeder Verwalter (<see cref="Permissions.LeagueManage"/>) alles,
/// sonst nur eigene NICHT anonymisierte Partien — bei den anonymisierten ist nicht gespeichert, von wem sie stammen.
/// Regeln: <see cref="LeagueClubService"/>.
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

    [HttpGet("games")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<ActionResult<LeagueClubListDto>> List([FromQuery] string? fide, [FromQuery] string? q,
        [FromQuery] int page = 1, CancellationToken ct = default) =>
        Ok(await _club.ListAsync(GetUserId(), CanManage, fide, q, page, ct));

    [HttpGet("games/pgn")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Export([FromQuery] string? fide, [FromQuery] string? q, CancellationToken ct) =>
        File(Encoding.UTF8.GetBytes(await _club.ExportAsync(fide, q, ct)), "application/x-chess-pgn", "vereinspartien.pgn");

    /// <summary>PGN-Massenimport → <see cref="LeagueClubImportResultDto"/>; 400 <c>empty</c>/<c>tooLarge</c>.</summary>
    [HttpPost("games/import")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<LeagueClubImportResultDto>> Import([FromBody] LeagueClubImportRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.Pgn)) return BadRequest(new { reason = "empty", message = "No PGN." });
        if (req.Pgn.Length > LeagueClubService.MaxImportChars) return BadRequest(new { reason = "tooLarge", message = "PGN too large." });
        return Ok(await _club.ImportPgnAsync(GetUserId(), req.Pgn, req.Anonymize, ct));
    }

    /// <summary>Eine Partie (aus einem Partieformular). 400 mit <c>reason</c> wie beim Import, dazu <c>duplicate</c>;
    /// die Einlesung (<c>scanId</c>) wird danach geschlossen — ihr Foto verschwindet.</summary>
    [HttpPost("games")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<IActionResult> Add([FromBody] LeagueClubGameRequest req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { reason = "empty", message = "Body required." });
        var (game, reason, message) = await _club.AddGameAsync(GetUserId(), req, ct);
        if (game == null) return BadRequest(new { reason, message = message ?? "Game not accepted." });
        if (req.ScanId is { } scanId) await _scans.CloseLeagueScanAsync(GetUserId(), scanId);
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

    /// <summary>Stehen diese Namen in einer Meldeliste? (Anzeige in der Korrektur, bevor man übernimmt.)</summary>
    [HttpPost("match")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<LeagueClubMatchDto>> Match([FromBody] LeagueClubMatchRequest req, CancellationToken ct) =>
        Ok(await _club.MatchAsync(req?.White, req?.Black, ct));

    // ── Partieformular (dieselbe Einlesung wie in RookHub, aber ohne „Meine Partien") ─────────────

    [HttpGet("scoresheet/status")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<ScoresheetStatusDto>> ScoresheetStatus() => Ok(await _scans.StatusAsync(GetUserId()));

    [HttpGet("scans")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<List<ScoresheetScanDto>>> Scans() => Ok(await _scans.LeagueScansAsync(GetUserId()));

    /// <summary>Foto hochladen (multipart <c>file</c>, <c>language</c>, <c>side</c>) — Absagen wie
    /// <c>POST /api/scoresheets</c>; Tageszahl und Kostenbremse sind DIESELBEN wie in RookHub.</summary>
    [HttpPost("scans")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    public async Task<ActionResult<ScoresheetScanDto>> Upload(IFormFile? file, [FromForm] string? language, [FromForm] string? side)
    {
        if (file == null || file.Length == 0) return BadRequest(new { reason = "noFile", message = "No file." });
        if (file.Length > ScoresheetScanService.MaxUploadBytes) return BadRequest(new { reason = "tooLarge", message = "File too large." });
        byte[] data;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            data = ms.ToArray();
        }
        var (scan, reason) = await _scans.CreateAsync(GetUserId(), data, file.ContentType, file.FileName, language, side,
            ScoresheetScan.PurposeLeague);
        if (reason == "notConfigured")
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { reason, message = "Reading is not configured." });
        if (reason != null) return BadRequest(new { reason, message = "Photo not accepted." });
        _signal.Wake();
        return Accepted(scan);
    }

    [HttpGet("scans/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<LeagueScanStateDto>> Scan(int id, CancellationToken ct) =>
        await _scans.LeagueScanStateAsync(GetUserId(), id, ct) is { } s ? Ok(s) : NotFound();

    [HttpGet("scans/{id:int}/photo")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<IActionResult> Photo(int id)
    {
        if (await _scans.LeagueScanPhotoAsync(GetUserId(), id) is not { } p) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
        return File(p.Data, p.ContentType);
    }

    [HttpPost("scans/{id:int}/resolve")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<ScoresheetResolveResultDto>> Resolve(int id, [FromBody] ScoresheetResolveRequestDto dto)
    {
        if (dto is null) return BadRequest(new { message = "Body required." });
        try
        {
            var r = await _scans.ResolveLeagueRestAsync(GetUserId(), id, dto.Prefix ?? new(), dto.WrittenFrom);
            return r == null ? NotFound() : Ok(r);
        }
        catch (ArgumentException ex) { return BadRequest(new { reason = "illegalMove", message = ex.Message }); }
    }

    /// <summary>Einlesung verwerfen: Foto und Lesung weg, die Zeile bleibt fürs Tageskontingent.</summary>
    [HttpDelete("scans/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<IActionResult> Discard(int id) => await _scans.CloseLeagueScanAsync(GetUserId(), id) ? NoContent() : NotFound();
}
