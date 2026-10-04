using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.ChessBase;
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
    private readonly PermissionResolver? _permissions;

    public LeagueClubController(LeagueClubService club, ScoresheetScanService scans, ScoresheetScanSignal signal,
        PermissionResolver? permissions = null)
    {
        _club = club; _scans = scans; _signal = signal; _permissions = permissions;
    }

    /// <summary>Verwalter (<c>league.manage</c>) — LIVE, wie <c>[HasPermission]</c> (0.589.0): eine eben vergebene oder
    /// entzogene Rolle gilt sofort, nicht erst nach dem nächsten Anmelden.</summary>
    private async Task<bool> CanManageAsync() =>
        User.IsInRole("Admin") || (_permissions != null
            ? (await _permissions.GetAsync(GetUserId())).Has(Permissions.LeagueManage)
            : User.HasClaim(PermissionAuthorizationHandler.PermissionClaimType, Permissions.LeagueManage));

    /// <summary>Verwalter dürfen JEDE Liga-Einlesung öffnen, übernehmen und verwerfen — sonst nur die eigenen.</summary>
    private async Task<Actor> MeAsync() => await CanManageAsync() ? Actor.ManagerOf(GetUserId()) : Actor.User(GetUserId());

    [HttpGet("games")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<ActionResult<LeagueClubListDto>> List([FromQuery] string? fide, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] bool mine = false, CancellationToken ct = default) =>
        Ok(await _club.ListAsync(GetUserId(), await CanManageAsync(), fide, q, page, ct, mine));

    /// <summary>Eine Vereinspartie zum Nachspielen (PGN + Stand der Analyse); 404 unbekannt.</summary>
    [HttpGet("games/{id:int}")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<ActionResult<LeagueClubGameDto>> Get(int id, CancellationToken ct) =>
        await _club.GetAsync(GetUserId(), await CanManageAsync(), id, ct) is { } g ? Ok(g) : NotFound();

    /// <summary>Bewertungen aus der Hintergrund-Analyse (0.593.0) — für jeden, der die Vereinspartien sieht; 404 unbekannt.</summary>
    [HttpGet("games/{id:int}/evals")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<ActionResult<GameEvalsDto>> Evals(int id, CancellationToken ct) =>
        await _club.EvalsAsync(id, ct) is { } e ? Ok(e) : NotFound();

    [HttpGet("games/pgn")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Export([FromQuery] string? fide, [FromQuery] string? q, CancellationToken ct) =>
        File(Encoding.UTF8.GetBytes(await _club.ExportAsync(fide, q, ct)), "application/x-chess-pgn", "vereinspartien.pgn");

    /// <summary>PGN lesen und zuordnen, NICHTS speichern → Übersicht; 400 <c>empty</c>/<c>tooLarge</c>.</summary>
    [HttpPost("games/preview")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public async Task<ActionResult<LeagueClubPreviewDto>> Preview([FromBody] LeagueClubPreviewRequest req,
        [FromServices] LeagueClubDraftService drafts, CancellationToken ct) =>
        ClubUpload.CheckPgn(req?.Pgn) is { } bad ? BadRequest(bad)
            : Ok(await _club.PreviewAsync(await ActingUserAsync(req!.DraftId, drafts, ct), req.Pgn, ct));

    /// <summary>Für wen gerechnet wird: ohne Entwurf der Aufrufer; mit Entwurf (auf den er Zugriff hat) der Einreicher — ein
    /// Verwalter, der fertigstellt, handelt für ihn (ohne Konto eingereicht: für niemanden).</summary>
    private async Task<int?> ActingUserAsync(int? draftId, LeagueClubDraftService drafts, CancellationToken ct)
    {
        if (draftId is not int id) return GetUserId();
        var (found, owner) = await drafts.ActingUserAsync(DraftActor.User(GetUserId(), await CanManageAsync()), id, ct);
        return found ? owner : GetUserId();
    }

    // ── Entwürfe (0.595.0): jede eingereichte Partieliste liegt sofort online ────────────────────

    [HttpPost("drafts")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public async Task<IActionResult> CreateDraft([FromBody] LeagueClubDraftCreateRequest req, [FromServices] LeagueClubDraftService drafts,
        CancellationToken ct)
    {
        if (ClubUpload.CheckPgn(req?.Pgn) is { } bad) return BadRequest(bad);
        var (draft, reason) = await drafts.CreateAsync(GetUserId(), null, req!.Pgn, req.Source, req.Label, ct);
        return draft == null ? BadRequest(new { reason, message = "Too many open lists." }) : Ok(draft);
    }

    [HttpGet("drafts")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<List<LeagueClubDraftDto>>> Drafts([FromServices] LeagueClubDraftService drafts, CancellationToken ct) =>
        Ok(await drafts.ListAsync(DraftActor.User(GetUserId(), false), null, ct));

    [HttpGet("drafts/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<LeagueClubDraftDetailDto>> Draft(int id, [FromServices] LeagueClubDraftService drafts, CancellationToken ct) =>
        await drafts.GetAsync(DraftActor.User(GetUserId(), await CanManageAsync()), id, ct) is { } d ? Ok(d) : NotFound();

    [HttpPut("drafts/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public async Task<IActionResult> SaveDraft(int id, [FromBody] LeagueClubDraftSaveRequest req, [FromServices] LeagueClubDraftService drafts,
        CancellationToken ct)
    {
        var (found, reason) = await drafts.SaveAsync(DraftActor.User(GetUserId(), await CanManageAsync()), id, req ?? new(), ct);
        return !found ? NotFound() : reason != null ? BadRequest(new { reason }) : NoContent();
    }

    [HttpDelete("drafts/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<IActionResult> DeleteDraft(int id, [FromServices] LeagueClubDraftService drafts, CancellationToken ct) =>
        await drafts.DeleteAsync(DraftActor.User(GetUserId(), await CanManageAsync()), id, ct) ? NoContent() : NotFound();

    /// <summary>Alle offenen Entwürfe (Verwalter) — auch über Teilen-Links, damit nichts liegen bleibt.</summary>
    [HttpGet("admin/drafts")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<ActionResult<List<LeagueClubDraftDto>>> AllDrafts([FromServices] LeagueClubDraftService drafts, CancellationToken ct) =>
        Ok(await drafts.ListAllAsync(GetUserId(), ct));

    /// <summary>PGN übernehmen, je Partie mit den Entscheidungen aus der Übersicht → <see cref="LeagueClubImportResultDto"/>.</summary>
    [HttpPost("games/import")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public async Task<ActionResult<LeagueClubImportResultDto>> Import([FromBody] LeagueClubImportRequest req,
        [FromServices] LeagueClubDraftService drafts, CancellationToken ct) =>
        ClubUpload.CheckPgn(req?.Pgn) is { } bad ? BadRequest(bad)
            : Ok(await _club.ImportPgnAsync(await ActingUserAsync(req!.DraftId, drafts, ct), req.Pgn, req.Games, ct));

    /// <summary>Eine Partie (aus einem Partieformular). 400 mit <c>reason</c> wie beim Import, dazu <c>duplicate</c>;
    /// die Einlesung (<c>scanId</c>) wird danach geschlossen — ihr Foto verschwindet.</summary>
    [HttpPost("games")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<IActionResult> Add([FromBody] LeagueClubGameRequest req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { reason = "empty", message = "Body required." });
        var (game, reason, message) = await _club.AddGameAsync(GetUserId(), req, ct);
        if (game == null) return BadRequest(new { reason, message = message ?? "Game not accepted." });
        if (req.ScanId is { } scanId) await _scans.CloseLeagueScanAsync(await MeAsync(), scanId);
        return Ok(new { id = game.Id, anonymized = game.Anonymized });
    }

    [HttpDelete("games/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await _club.DeleteAsync(GetUserId(), await CanManageAsync(), id, ct) switch
        {
            LeagueClubService.DeleteResult.Deleted => NoContent(),
            LeagueClubService.DeleteResult.Forbidden => Forbid(),
            _ => NotFound(),
        };

    /// <summary>„Alle Partien dieses Links entfernen" (Verwalter, Codereview 2026-09-29): alles, was über den Teilen-Link
    /// hochgeladen wurde — auch nach seinem Ablauf, in jeder Schreibweise, solange die Link-Zeile noch steht (sonst zählt
    /// der Wert, wie er kommt). <c>dryRun=true</c> zählt nur → <c>{ count, dryRun }</c>.</summary>
    [HttpDelete("admin/shares/{token}/games")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> DeleteShareGames(string token, [FromQuery] bool dryRun = false, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(token) || token.Length > 64 ? BadRequest(new { reason = "invalidToken", message = "Invalid link." })
            : Ok(new { count = await _club.DeleteByShareAsync(token, dryRun, ct), dryRun });

    /// <summary>Ligaspieler zum Eintippen der Namen (ab zwei Buchstaben).</summary>
    [HttpGet("players")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<List<LeagueRosterPersonDto>>> Players([FromQuery] string? q, [FromQuery] bool all = false,
        CancellationToken ct = default) =>
        Ok(string.IsNullOrWhiteSpace(q) ? new List<LeagueRosterPersonDto>() : await _club.SuggestAsync(q, all, ct));

    /// <summary>PGN einer öffentlichen Lichess-Studie holen → <c>{ pgn }</c> (danach wie ein Upload: Übersicht, Import).
    /// 400 <c>invalidUrl</c>/<c>lichessNotFound</c>/<c>lichessFailed</c>/<c>tooLarge</c>.</summary>
    [HttpPost("games/lichess")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<IActionResult> Lichess([FromBody] LeagueClubLichessRequest req, [FromServices] LichessStudySource lichess,
        CancellationToken ct)
    {
        var (pgn, reason) = await lichess.FetchAsync(req?.Url, ct);
        return pgn == null ? BadRequest(new { reason, message = "Study not loaded." }) : Ok(new { pgn });
    }

    /// <summary>Eine ChessBase-Datenbank (multipart <c>files</c>: die Dateien, einzeln gepackt als <c>.gz</c>, oder ein ZIP)
    /// → PGN der Hauptvarianten samt Kopfdaten (0.598.0); danach wie ein Upload. 400 mit Grund-Code (siehe
    /// <see cref="ChessBaseImportService.ConvertAsync"/>), 429 <c>busy</c>.</summary>
    [HttpPost("games/chessbase")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ChessBaseImportService.MaxBodyBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ChessBaseImportService.MaxBodyBytes + 1024 * 1024)]
    public async Task<IActionResult> ChessBaseUpload([FromServices] ChessBaseImportService chessBase, CancellationToken ct) =>
        ClubUpload.ChessBaseResult(this, await chessBase.ConvertAsync(await ClubUpload.FormFilesAsync(Request, ct), ct));

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

    /// <summary>Namen und Ergebnis korrigieren → die Partie; 400 <c>reason</c> (<c>anonymous</c>, <c>noLeaguePlayer</c>,
    /// <c>onlyOwnClub</c>, <c>invalidResult</c>), 403 fremde, 404 unbekannt.</summary>
    [HttpPut("games/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<LeagueClubGameDto>> Update(int id, [FromBody] LeagueClubGameUpdateRequest req, CancellationToken ct)
    {
        var manage = await CanManageAsync();
        var (game, reason) = await _club.UpdateAsync(GetUserId(), manage, id, req ?? new(), ct);
        return reason switch
        {
            null => Ok(LeagueClubService.ToDto(game!, GetUserId(), manage)),
            "notFound" => NotFound(),
            "forbidden" => Forbid(),
            _ => BadRequest(new { reason }),
        };
    }

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

    // ── Stapel-Upload (0.651.0): Bilder nur ablegen, nicht einlesen — die Admins bekommen eine Nachricht ──

    public sealed record BatchStartRequest(string? Comment);

    [HttpPost("batches")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<IActionResult> BatchStart([FromBody] BatchStartRequest? req, [FromServices] LeagueBatchUploadService batches,
        CancellationToken ct) => Ok(await batches.StartAsync(LeagueBatchUploadService.Uploader.User(GetUserId()), req?.Comment, ct));

    [HttpPost("batches/{key}/files")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    public async Task<IActionResult> BatchFile(string key, IFormFile? file, [FromServices] LeagueBatchUploadService batches, CancellationToken ct)
    {
        if (await ClubUpload.ReadAsync(file) is not { } data) return BadRequest(ClubUpload.FileError(file));
        return ClubUpload.BatchResult(this, await batches.AddFileAsync(LeagueBatchUploadService.Uploader.User(GetUserId()), key, data,
            file!.ContentType, file.FileName, ct));
    }

    [HttpPost("batches/{key}/finish")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<IActionResult> BatchFinish(string key, [FromServices] LeagueBatchUploadService batches, CancellationToken ct) =>
        ClubUpload.BatchResult(this, await batches.FinishAsync(LeagueBatchUploadService.Uploader.User(GetUserId()), key, ct));

    /// <summary>Alle offenen Liga-Einlesungen, auch fremde und über Teilen-Links (Verwalter).</summary>
    [HttpGet("admin/scans")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<ActionResult<List<LeagueOpenScanDto>>> OpenScans(CancellationToken ct) =>
        Ok(await _scans.LeagueOpenScansAsync(GetUserId(), ct));

    [HttpGet("scans/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<LeagueScanStateDto>> Scan(int id, CancellationToken ct) =>
        await _scans.LeagueScanStateAsync(await MeAsync(), id, ct) is { } s ? Ok(s) : NotFound();

    [HttpGet("scans/{id:int}/photo")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<IActionResult> Photo(int id) => ClubUpload.PhotoResult(this, await _scans.LeagueScanPhotoAsync(await MeAsync(), id));

    [HttpPost("scans/{id:int}/resolve")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<ScoresheetResolveResultDto>> Resolve(int id, [FromBody] ScoresheetResolveRequestDto dto)
    {
        var me = await MeAsync();
        return await ClubUpload.ResolveAsync(this, () => _scans.ResolveLeagueRestAsync(me, id, dto?.Prefix ?? new(), dto?.WrittenFrom ?? 0));
    }

    /// <summary>Einlesung verwerfen: Foto und Lesung weg, die Zeile bleibt fürs Tageskontingent.</summary>
    [HttpDelete("scans/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<IActionResult> Discard(int id) => await _scans.CloseLeagueScanAsync(await MeAsync(), id) ? NoContent() : NotFound();
}

/// <summary>
/// Dieselben Upload-Wege OHNE Anmeldung, über einen gültigen Teilen-Link (Wunsch 2026-09-28: „auf dem Link für die
/// nächste Aufstellung soll es auch die Option geben, Spiele hochzuladen — ohne Anmeldung"). Der Link ist der Nachweis;
/// gespeicherte Partien tragen ihn als Hash und sind gedeckelt (<see cref="LeagueShareUploadQuota"/>, Codereview
/// 2026-09-29, A2-009);
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
        // Das Token der Link-Zeile, nicht der Routen-Wert: dieselbe Datenbank-Zeile findet sich auch mit anderer
        // Groß/Kleinschreibung (siehe LeagueService.ValidShareTokenAsync) — Vermerk und Deckel hängen am Link, nicht an
        // seiner Schreibweise.
        if (await _league.ValidShareTokenAsync(token, ct) is not { } link) return NotFound();
        return ClubUpload.CheckPgn(req?.Pgn) is { } bad ? BadRequest(bad) : Ok(await _club.ImportViaShareAsync(link, req!.Pgn, req.Games, ct));
    }

    /// <summary>Eine Partie aus einem Partieformular; <c>scanKey</c> = der Schlüssel der Einlesung (wird geschlossen).</summary>
    [HttpPost("games")]
    public async Task<IActionResult> Add(string token, [FromBody] LeagueClubGameRequest req, [FromQuery] string? scanKey, CancellationToken ct)
    {
        if (await _league.ValidShareTokenAsync(token, ct) is not { } link) return NotFound();   // wie beim Import
        if (req is null) return BadRequest(new { reason = "empty", message = "Body required." });
        req.ScanId = null;
        var (game, reason, message) = await _club.AddGameViaShareAsync(link, req, ct);
        if (game == null) return BadRequest(new { reason, message = message ?? "Game not accepted." });
        if (!string.IsNullOrWhiteSpace(scanKey)) await _scans.CloseLeagueScanAsync(Actor.Anonymous(scanKey), null);
        return Ok(new { id = game.Id, anonymized = game.Anonymized });
    }

    [HttpGet("players")]
    public async Task<ActionResult<List<LeagueRosterPersonDto>>> Players(string token, [FromQuery] string? q, [FromQuery] bool all,
        CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return Ok(string.IsNullOrWhiteSpace(q) ? new List<LeagueRosterPersonDto>() : await _club.SuggestAsync(q, all, ct));
    }

    [HttpPost("games/lichess")]
    public async Task<IActionResult> Lichess(string token, [FromBody] LeagueClubLichessRequest req, [FromServices] LichessStudySource lichess,
        CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        var (pgn, reason) = await lichess.FetchAsync(req?.Url, ct);
        return pgn == null ? BadRequest(new { reason, message = "Study not loaded." }) : Ok(new { pgn });
    }

    [HttpPost("games/chessbase")]
    [RequestSizeLimit(ChessBaseImportService.MaxBodyBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ChessBaseImportService.MaxBodyBytes + 1024 * 1024)]
    public async Task<IActionResult> ChessBaseUpload(string token, [FromServices] ChessBaseImportService chessBase, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return ClubUpload.ChessBaseResult(this, await chessBase.ConvertAsync(await ClubUpload.FormFilesAsync(Request, ct), ct));
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

    // ── Entwürfe ohne Konto (0.595.0): gehören dem Browser mit dem Schlüssel ─────────────────────

    [HttpPost("drafts")]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public async Task<IActionResult> CreateDraft(string token, [FromBody] LeagueClubDraftCreateRequest req,
        [FromServices] LeagueClubDraftService drafts, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        if (ClubUpload.CheckPgn(req?.Pgn) is { } bad) return BadRequest(bad);
        var (draft, reason) = await drafts.CreateAsync(null, IpHash, req!.Pgn, req.Source, req.Label, ct);
        return draft == null ? BadRequest(new { reason, message = "Too many open lists." }) : Ok(draft);
    }

    [HttpPost("drafts/lookup")]
    public async Task<IActionResult> DraftLookup(string token, [FromBody] KeysRequest req, [FromServices] LeagueClubDraftService drafts,
        CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return Ok(await drafts.ListAsync(DraftActor.Anonymous(null), req?.Keys ?? new(), ct));
    }

    [HttpGet("drafts/{key}")]
    public async Task<IActionResult> Draft(string token, string key, [FromServices] LeagueClubDraftService drafts, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return await drafts.GetAsync(DraftActor.Anonymous(key), null, ct) is { } d ? Ok(d) : NotFound();
    }

    [HttpPut("drafts/{key}")]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public async Task<IActionResult> SaveDraft(string token, string key, [FromBody] LeagueClubDraftSaveRequest req,
        [FromServices] LeagueClubDraftService drafts, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        var (found, reason) = await drafts.SaveAsync(DraftActor.Anonymous(key), null, req ?? new(), ct);
        return !found ? NotFound() : reason != null ? BadRequest(new { reason }) : NoContent();
    }

    [HttpDelete("drafts/{key}")]
    public async Task<IActionResult> DeleteDraft(string token, string key, [FromServices] LeagueClubDraftService drafts, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return await drafts.DeleteAsync(DraftActor.Anonymous(key), null, ct) ? NoContent() : NotFound();
    }

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

    [HttpPost("batches")]
    public async Task<IActionResult> BatchStart(string token, [FromBody] LeagueClubController.BatchStartRequest? req,
        [FromServices] LeagueBatchUploadService batches, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return Ok(await batches.StartAsync(LeagueBatchUploadService.Uploader.Share(token, IpHash), req?.Comment, ct));
    }

    [HttpPost("batches/{key}/files")]
    [RequestSizeLimit(ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    public async Task<IActionResult> BatchFile(string token, string key, IFormFile? file, [FromServices] LeagueBatchUploadService batches,
        CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        if (await ClubUpload.ReadAsync(file) is not { } data) return BadRequest(ClubUpload.FileError(file));
        return ClubUpload.BatchResult(this, await batches.AddFileAsync(LeagueBatchUploadService.Uploader.Share(token, IpHash), key, data,
            file!.ContentType, file.FileName, ct));
    }

    [HttpPost("batches/{key}/finish")]
    public async Task<IActionResult> BatchFinish(string token, string key, [FromServices] LeagueBatchUploadService batches, CancellationToken ct)
    {
        if (!await ValidAsync(token, ct)) return NotFound();
        return ClubUpload.BatchResult(this, await batches.FinishAsync(LeagueBatchUploadService.Uploader.Share(token, IpHash), key, ct));
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

    public static async Task<IReadOnlyCollection<IFormFile>?> FormFilesAsync(HttpRequest request, CancellationToken ct) =>
        request.HasFormContentType ? (await request.ReadFormAsync(ct)).Files : null;

    public static IActionResult ChessBaseResult(ControllerBase c,
        (LeagueClubChessBaseResultDto? Result, string? Reason, string? Message) r) =>
        r.Result != null ? c.Ok(r.Result)
        : r.Reason == "busy" ? new ObjectResult(new { reason = r.Reason, message = r.Message }) { StatusCode = StatusCodes.Status429TooManyRequests }
        : c.BadRequest(new { reason = r.Reason, message = r.Message });

    /// <summary>Antwort eines Stapel-Schritts: Stand, 404 (Stapel fremd/unbekannt) oder 400 mit Grund.</summary>
    public static IActionResult BatchResult(ControllerBase c, (LeagueBatchUploadService.BatchState? State, string? Reason) r) =>
        r.State is { } s ? c.Ok(s)
        : r.Reason == "notFound" ? c.NotFound()
        : c.BadRequest(new { reason = r.Reason, message = "Not accepted." });

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
