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
/// sonst nur eigene Partien. Jede Aktion läuft im Verein der Anfrage (<see cref="LeagueClubResolver"/>, Mandanten-Schritt
/// 2026-10-07): eine Partie, ein Entwurf oder ein Formular eines anderen Vereins ist hier „nicht gefunden". Derselbe Weg
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
    private readonly LeagueClubResolver _clubs;
    private readonly PermissionResolver? _permissions;

    public LeagueClubController(LeagueClubService club, ScoresheetScanService scans, ScoresheetScanSignal signal,
        LeagueClubResolver clubs, PermissionResolver? permissions = null)
    {
        _club = club; _scans = scans; _signal = signal; _clubs = clubs; _permissions = permissions;
    }

    /// <summary>Verwalter (<c>league.manage</c>) — LIVE, wie <c>[HasPermission]</c> (0.589.0): eine eben vergebene oder
    /// entzogene Rolle gilt sofort, nicht erst nach dem nächsten Anmelden. Gilt im Verein der Anfrage — zu dem gehört das
    /// Konto, sonst wäre die Anfrage schon an <see cref="WithClubAsync"/> gescheitert.</summary>
    private async Task<bool> CanManageAsync() =>
        User.IsInRole("Admin") || (_permissions != null
            ? (await _permissions.GetAsync(GetUserId())).Has(Permissions.LeagueManage)
            : User.HasClaim(PermissionAuthorizationHandler.PermissionClaimType, Permissions.LeagueManage));

    /// <summary>Verwalter dürfen JEDE Liga-Einlesung ihres Vereins öffnen, übernehmen und verwerfen — sonst nur die eigenen.</summary>
    private async Task<Actor> MeAsync(LeagueClub club) =>
        (await CanManageAsync() ? Actor.ManagerOf(GetUserId()) : Actor.User(GetUserId())) with { ClubId = club.Id };

    /// <summary>Jede Aktion läuft im Verein der Anfrage (<see cref="BaseApiController.LeagueClubAsync"/>); ohne Verein die Absage.</summary>
    private async Task<IActionResult> WithClubAsync(CancellationToken ct, Func<LeagueClub, Task<IActionResult>> run, int? preferred = null)
    {
        var (club, error) = await LeagueClubAsync(_clubs, ct, preferred);
        return club is null ? error! : await run(club);
    }

    [HttpGet("games")]
    [HasPermission(Permissions.LeagueView)]
    public Task<IActionResult> List([FromQuery] string? fide, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] bool mine = false, CancellationToken ct = default) =>
        WithClubAsync(ct, async club => Ok(await _club.ListAsync(club, GetUserId(), await CanManageAsync(), fide, q, page, ct, mine)));

    // ── Korrigieren (0.660.0): Züge einer Vereinspartie nachbessern, mit dem aufbewahrten Formular, falls es noch da ist ──

    public sealed record ClubMovesRequest(List<string>? Moves, List<ScoresheetPly>? Plies);

    private async Task<IActionResult?> CorrectableAsync(LeagueClub club, int id, CancellationToken ct) =>
        await _club.CanCorrectAsync(club, GetUserId(), await CanManageAsync(), id, ct) ? null : NotFound();

    /// <summary>Formular-Einträge + Stand je Halbzug aus dem Archiv (wie <c>GET /api/games/{id}/scoresheet</c>); 404, wenn
    /// nichts mehr aufbewahrt ist oder die Partie nicht korrigiert werden darf (Hochladender/Verwalter).</summary>
    [HttpGet("games/{id:int}/sheet")]
    public Task<IActionResult> Sheet(int id, CancellationToken ct) => WithClubAsync(ct, async club =>
        await CorrectableAsync(club, id, ct) ?? (await _scans.ClubEditStateAsync(id, ct) is { } s ? Ok(s) : NotFound()));

    [HttpGet("games/{id:int}/sheet/photo")]
    public Task<IActionResult> SheetPhoto(int id, [FromQuery] int page = 1, CancellationToken ct = default) => WithClubAsync(ct, async club =>
    {
        if (await CorrectableAsync(club, id, ct) is { } denied) return denied;
        if (await _scans.ClubPhotoAsync(id, page, ct) is not { } p) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers["X-Page-Count"] = p.PageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return File(p.Data, p.ContentType);
    });

    [HttpPost("games/{id:int}/sheet/resolve")]
    public Task<IActionResult> SheetResolve(int id, [FromBody] ScoresheetResolveRequestDto dto, CancellationToken ct) => WithClubAsync(ct, async club =>
    {
        if (await CorrectableAsync(club, id, ct) is { } denied) return denied;
        try { return await _scans.ResolveClubRestAsync(id, dto?.Prefix ?? new(), dto?.WrittenFrom ?? 0, ct) is { } r ? Ok(r) : NotFound(); }
        catch (ArgumentException ex) { return BadRequest(new { reason = "illegalMove", message = ex.Message }); }
    });

    /// <summary>Züge korrigieren → die Partie; geht in alle verbundenen Kopien. 400 <c>noMoves</c>/<c>tooLong</c>/<c>illegal</c>.</summary>
    [HttpPut("games/{id:int}/moves")]
    public Task<IActionResult> CorrectMoves(int id, [FromBody] ClubMovesRequest req, [FromServices] ClubGameCorrectionService corrections,
        CancellationToken ct) => WithClubAsync(ct, async club =>
    {
        var manage = await CanManageAsync();
        var (game, reason) = await corrections.CorrectClubAsync(club, GetUserId(), manage, id, req?.Moves ?? new(), req?.Plies, null, ct);
        return reason switch
        {
            null => Ok(LeagueClubService.ToDto(game!, GetUserId(), manage)),
            "notFound" or "forbidden" => NotFound(),
            _ => BadRequest(new { reason }),
        };
    });

    /// <summary>„Meine Partien" (0.656.0): die eigenen im Verein der Anfrage.</summary>
    [HttpGet("games/mine")]
    public Task<IActionResult> Mine([FromQuery] string? q, [FromQuery] int page = 1, CancellationToken ct = default) =>
        WithClubAsync(ct, async club => Ok(await _club.ListAsync(club, GetUserId(), await CanManageAsync(), null, q, page, ct, mine: true)));

    public sealed record ClaimRequest(List<string>? Keys);

    // Zuordnen nach dem Anmelden (0.656.0) bleibt OHNE Verein: die Schlüssel (128 Bit) kennt nur der Browser, der hochgeladen
    // hat — er sagt, was ER hochgeladen hat, gleich über welchen Verein. Bearbeiten kann er die Partien danach nur im Verein
    // der Partie (alle anderen Wege laufen über den Verein der Anfrage).

    /// <summary>Was die Zuordnungs-Schlüssel dieses Browsers zuordnen würden → <c>{ games, anonymized }</c> (für die Rückfrage).</summary>
    [HttpPost("games/claims/preview")]
    public async Task<IActionResult> ClaimPreview([FromBody] ClaimRequest? req, CancellationToken ct)
    {
        var (games, anonymized) = await _club.ClaimPreviewAsync(req?.Keys, ct);
        return Ok(new { games, anonymized });
    }

    /// <summary>JA: die anonym hochgeladenen Partien dieser Schlüssel gehören ab jetzt dem Angemeldeten → <c>{ claimed }</c>.</summary>
    [HttpPost("games/claims")]
    public async Task<IActionResult> Claim([FromBody] ClaimRequest? req, CancellationToken ct) =>
        Ok(new { claimed = await _club.ClaimAsync(GetUserId(), req?.Keys, ct) });

    /// <summary>NEIN: die Schlüssel verfallen, die Partien bleiben ohne Hochladenden.</summary>
    [HttpPost("games/claims/forget")]
    public async Task<IActionResult> ForgetClaims([FromBody] ClaimRequest? req, CancellationToken ct) =>
        Ok(new { forgotten = await _club.ForgetClaimsAsync(req?.Keys, ct) });

    /// <summary>Eine Vereinspartie zum Nachspielen (PGN + Stand der Analyse); 404 unbekannt oder aus einem anderen Verein. Ohne
    /// <c>?club=</c> (RookHubs Partie-Seite <c>/club-games/{id}</c> kennt keinen) gilt bei mehreren Vereinen der der Partie.</summary>
    [HttpGet("games/{id:int}")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Get(int id, CancellationToken ct) =>
        await WithClubAsync(ct, async club => await _club.GetAsync(club, GetUserId(), await CanManageAsync(), id, ct) is { } g ? Ok(g) : NotFound(),
            await _club.ClubOfGameAsync(id, ct));

    /// <summary>Bewertungen aus der Hintergrund-Analyse (0.593.0) — für jeden, der die Vereinspartien sieht; 404 unbekannt.</summary>
    [HttpGet("games/{id:int}/evals")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Evals(int id, CancellationToken ct) =>
        await WithClubAsync(ct, async club => await _club.EvalsAsync(club, id, ct) is { } e ? Ok(e) : NotFound(),
            await _club.ClubOfGameAsync(id, ct));

    [HttpGet("games/pgn")]
    [HasPermission(Permissions.LeagueView)]
    public Task<IActionResult> Export([FromQuery] string? fide, [FromQuery] string? q, CancellationToken ct) => WithClubAsync(ct, async club =>
        File(Encoding.UTF8.GetBytes(await _club.ExportAsync(club, fide, q, ct)), "application/x-chess-pgn", "vereinspartien.pgn"));

    /// <summary>PGN lesen und zuordnen, NICHTS speichern → Übersicht; 400 <c>empty</c>/<c>tooLarge</c>.</summary>
    [HttpPost("games/preview")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public Task<IActionResult> Preview([FromBody] LeagueClubPreviewRequest req,
        [FromServices] LeagueClubDraftService drafts, CancellationToken ct) => WithClubAsync(ct, async club =>
        ClubUpload.CheckPgn(req?.Pgn) is { } bad ? BadRequest(bad)
            : Ok(await _club.PreviewAsync(club, await ActingUserAsync(club, req!.DraftId, drafts, ct), req.Pgn, ct)));

    /// <summary>Für wen gerechnet wird: ohne Entwurf der Aufrufer; mit Entwurf (auf den er Zugriff hat) der Einreicher — ein
    /// Verwalter, der fertigstellt, handelt für ihn (ohne Konto eingereicht: für niemanden).</summary>
    private async Task<int?> ActingUserAsync(LeagueClub club, int? draftId, LeagueClubDraftService drafts, CancellationToken ct)
    {
        if (draftId is not int id) return GetUserId();
        var (found, owner) = await drafts.ActingUserAsync(DraftActor.User(GetUserId(), await CanManageAsync(), club.Id), id, ct);
        return found ? owner : GetUserId();
    }

    // ── Entwürfe (0.595.0): jede eingereichte Partieliste liegt sofort online ────────────────────

    [HttpPost("drafts")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public Task<IActionResult> CreateDraft([FromBody] LeagueClubDraftCreateRequest req, [FromServices] LeagueClubDraftService drafts,
        CancellationToken ct) => WithClubAsync(ct, async club =>
    {
        if (ClubUpload.CheckPgn(req?.Pgn) is { } bad) return BadRequest(bad);
        var (draft, reason) = await drafts.CreateAsync(club.Id, GetUserId(), null, req!.Pgn, req.Source, req.Label, ct);
        return draft == null ? BadRequest(new { reason, message = "Too many open lists." }) : Ok(draft);
    });

    [HttpGet("drafts")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> Drafts([FromServices] LeagueClubDraftService drafts, CancellationToken ct) => WithClubAsync(ct, async club =>
        Ok(await drafts.ListAsync(DraftActor.User(GetUserId(), false, club.Id), null, ct)));

    [HttpGet("drafts/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> Draft(int id, [FromServices] LeagueClubDraftService drafts, CancellationToken ct) => WithClubAsync(ct, async club =>
        await drafts.GetAsync(DraftActor.User(GetUserId(), await CanManageAsync(), club.Id), id, ct) is { } d ? Ok(d) : NotFound());

    [HttpPut("drafts/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public Task<IActionResult> SaveDraft(int id, [FromBody] LeagueClubDraftSaveRequest req, [FromServices] LeagueClubDraftService drafts,
        CancellationToken ct) => WithClubAsync(ct, async club =>
    {
        var (found, reason) = await drafts.SaveAsync(DraftActor.User(GetUserId(), await CanManageAsync(), club.Id), id, req ?? new(), ct);
        return !found ? NotFound() : reason != null ? BadRequest(new { reason }) : NoContent();
    });

    [HttpDelete("drafts/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> DeleteDraft(int id, [FromServices] LeagueClubDraftService drafts, CancellationToken ct) => WithClubAsync(ct, async club =>
        await drafts.DeleteAsync(DraftActor.User(GetUserId(), await CanManageAsync(), club.Id), id, ct) ? NoContent() : NotFound());

    /// <summary>Alle offenen Entwürfe des Vereins (Verwalter) — auch über Teilen-Links, damit nichts liegen bleibt.</summary>
    [HttpGet("admin/drafts")]
    [HasPermission(Permissions.LeagueManage)]
    public Task<IActionResult> AllDrafts([FromServices] LeagueClubDraftService drafts, CancellationToken ct) => WithClubAsync(ct, async club =>
        Ok(await drafts.ListAllAsync(club.Id, GetUserId(), ct)));

    /// <summary>PGN übernehmen, je Partie mit den Entscheidungen aus der Übersicht → <see cref="LeagueClubImportResultDto"/>.</summary>
    [HttpPost("games/import")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public Task<IActionResult> Import([FromBody] LeagueClubImportRequest req,
        [FromServices] LeagueClubDraftService drafts, CancellationToken ct) => WithClubAsync(ct, async club =>
        ClubUpload.CheckPgn(req?.Pgn) is { } bad ? BadRequest(bad)
            : Ok(await _club.ImportPgnAsync(club, await ActingUserAsync(club, req!.DraftId, drafts, ct), req.Pgn, req.Games, ct)));

    /// <summary>Eine Partie (aus einem Partieformular). 400 mit <c>reason</c> wie beim Import, dazu <c>duplicate</c>;
    /// die Einlesung (<c>scanId</c>) wird danach geschlossen — ihr Foto verschwindet.</summary>
    [HttpPost("games")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> Add([FromBody] LeagueClubGameRequest req, CancellationToken ct) => WithClubAsync(ct, async club =>
    {
        if (req is null) return BadRequest(new { reason = "empty", message = "Body required." });
        var (game, reason, message) = await _club.AddGameAsync(club, GetUserId(), req, ct);
        if (game == null) return BadRequest(new { reason, message = message ?? "Game not accepted." });
        if (req.ScanId is { } scanId) await _scans.CloseLeagueScanAsync(await MeAsync(club), scanId, game.Pgn, game.Id);
        if (req.ScanId != null && req.Plies != null) await _scans.SaveClubEditStateAsync(game.Id, req.Plies, game.Pgn, ct);
        return Ok(new { id = game.Id, anonymized = game.Anonymized, replaced = game.Replaced });
    });

    /// <summary>Löschen: eigene (seit 0.656.0 nur mit Anmeldung — die Regel steht im Dienst) oder als Verwalter jede des Vereins.</summary>
    [HttpDelete("games/{id:int}")]
    public Task<IActionResult> Delete(int id, CancellationToken ct) => WithClubAsync(ct, async club =>
        await _club.DeleteAsync(club, GetUserId(), await CanManageAsync(), id, ct) switch
        {
            LeagueClubService.DeleteResult.Deleted => NoContent(),
            LeagueClubService.DeleteResult.Forbidden => Forbid(),
            _ => NotFound(),
        });

    /// <summary>„Alle Partien dieses Links entfernen" (Verwalter, Codereview 2026-09-29): alles, was über den Teilen-Link
    /// hochgeladen wurde — auch nach seinem Ablauf, in jeder Schreibweise, solange die Link-Zeile noch steht (sonst zählt
    /// der Wert, wie er kommt); nur Partien des eigenen Vereins. <c>dryRun=true</c> zählt nur → <c>{ count, dryRun }</c>.</summary>
    [HttpDelete("admin/shares/{token}/games")]
    [HasPermission(Permissions.LeagueManage)]
    public Task<IActionResult> DeleteShareGames(string token, [FromQuery] bool dryRun = false, CancellationToken ct = default) =>
        WithClubAsync(ct, async club =>
            string.IsNullOrWhiteSpace(token) || token.Length > 64 ? BadRequest(new { reason = "invalidToken", message = "Invalid link." })
                : Ok(new { count = await _club.DeleteByShareAsync(club, token, dryRun, ct), dryRun }));

    /// <summary>Ligaspieler zum Eintippen der Namen (ab zwei Buchstaben).</summary>
    [HttpGet("players")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> Players([FromQuery] string? q, [FromQuery] bool all = false,
        CancellationToken ct = default) => WithClubAsync(ct, async club =>
        Ok(string.IsNullOrWhiteSpace(q) ? new List<LeagueRosterPersonDto>() : await _club.SuggestAsync(club, q, all, ct)));

    /// <summary>PGN einer öffentlichen Lichess-Studie holen → <c>{ pgn }</c> (danach wie ein Upload: Übersicht, Import).
    /// 400 <c>invalidUrl</c>/<c>lichessNotFound</c>/<c>lichessFailed</c>/<c>tooLarge</c>. Speichert nichts — ohne Verein.</summary>
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
    /// <see cref="ChessBaseImportService.ConvertAsync"/>), 429 <c>busy</c>. Speichert nichts — ohne Verein.</summary>
    [HttpPost("games/chessbase")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ChessBaseImportService.MaxBodyBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ChessBaseImportService.MaxBodyBytes + 1024 * 1024)]
    public async Task<IActionResult> ChessBaseUpload([FromServices] ChessBaseImportService chessBase, CancellationToken ct) =>
        ClubUpload.ChessBaseResult(this, await chessBase.ConvertAsync(await ClubUpload.FormFilesAsync(Request, ct), ct));

    /// <summary>Stehen diese Namen in einer Meldeliste? Spieler des eigenen Vereins?</summary>
    [HttpPost("match")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> Match([FromBody] LeagueClubMatchRequest req, CancellationToken ct) => WithClubAsync(ct, async club =>
        Ok(await _club.MatchAsync(club, req?.White, req?.Black, ct)));

    /// <summary>Welche Brettpaarung könnte diese (noch nicht gespeicherte) Partie sein? (0.678.0)</summary>
    [HttpPost("pairings")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> Pairings([FromBody] LeagueClubPairingQuery req, CancellationToken ct) => WithClubAsync(ct, async club =>
        Ok(await _club.SuggestPairingsAsync(club, req ?? new LeagueClubPairingQuery(), ct)));

    /// <summary>Brettpaarungen für eine gespeicherte Partie (Bearbeiten, 0.678.0) — nur wer sie bearbeiten darf, sonst 404.</summary>
    [HttpGet("games/{id:int}/pairings")]
    [HasPermission(Permissions.LeagueView)]
    public Task<IActionResult> GamePairings(int id, CancellationToken ct) => WithClubAsync(ct, async club =>
        await _club.PairingsForGameAsync(club, GetUserId(), await CanManageAsync(), id, ct) is { } list ? Ok(list) : NotFound());

    // ── Partieformular (dieselbe Einlesung wie in RookHub, aber ohne „Meine Partien") ─────────────

    /// <summary>Tageszahl dieses Wegs: <see cref="ScoresheetScanService.DefaultLeagueDailyLimit"/> je Nutzer (über alle Vereine).</summary>
    [HttpGet("scoresheet/status")]
    [HasPermission(Permissions.LeagueContribute)]
    public async Task<ActionResult<ScoresheetStatusDto>> ScoresheetStatus() =>
        Ok(await _scans.StatusAsync(GetUserId(), ScoresheetScan.PurposeLeague));

    /// <summary>Namen und Ergebnis korrigieren → die Partie; 400 <c>reason</c> (<c>anonymous</c>, <c>noLeaguePlayer</c>,
    /// <c>onlyOwnClub</c>, <c>invalidResult</c>), 403 fremde, 404 unbekannt.</summary>
    [HttpPut("games/{id:int}")]   // eigene oder als Verwalter (Regel im Dienst) — seit 0.656.0 ohne league.contribute
    public Task<IActionResult> Update(int id, [FromBody] LeagueClubGameUpdateRequest req, CancellationToken ct) => WithClubAsync(ct, async club =>
    {
        var manage = await CanManageAsync();
        var (game, reason) = await _club.UpdateAsync(club, GetUserId(), manage, id, req ?? new(), ct);
        return reason switch
        {
            null => Ok(LeagueClubService.ToDto(game!, GetUserId(), manage)),
            "notFound" => NotFound(),
            "forbidden" => Forbid(),
            _ => BadRequest(new { reason }),
        };
    });

    [HttpGet("scans")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> Scans(CancellationToken ct) => WithClubAsync(ct, async club =>
        Ok(await _scans.LeagueScansAsync(GetUserId(), club.Id)));

    /// <summary>Foto hochladen (multipart <c>file</c>, <c>language</c>, <c>side</c>) — Absagen wie <c>POST /api/scoresheets</c>.
    /// Ein Formular über mehrere Blätter: mehrere Teile <c>file</c> in Seitenreihenfolge (höchstens
    /// <see cref="ScoresheetScanService.MaxPages"/>, 0.690.1) — EINE Einlesung.</summary>
    [HttpPost("scans")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ScoresheetScanService.MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ScoresheetScanService.MaxUploadRequestBytes)]
    public Task<IActionResult> Upload([FromForm] List<IFormFile>? file, [FromForm] string? language, [FromForm] string? side,
        [FromForm] string? layout, CancellationToken ct) => WithClubAsync(ct, async club =>
    {
        var (pages, error) = await ClubUpload.ReadPagesAsync(file, layout);
        if (error != null) return BadRequest(error);
        var (scan, reason) = await _scans.CreateAsync(GetUserId(), pages!, language, side, ScoresheetScan.PurposeLeague, club.Id);
        if (ClubUpload.Refusal(reason) is { } refused) return refused;
        _signal.Wake();
        return Accepted(scan);
    });

    // ── Stapel-Upload (0.651.0): Bilder nur ablegen, nicht einlesen — die Admins bekommen eine Nachricht ──

    public sealed record BatchStartRequest(string? Comment);

    [HttpPost("batches")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> BatchStart([FromBody] BatchStartRequest? req, [FromServices] LeagueBatchUploadService batches,
        CancellationToken ct) => WithClubAsync(ct, async club =>
        Ok(await batches.StartAsync(LeagueBatchUploadService.Uploader.User(GetUserId(), club.Id), req?.Comment, ct)));

    [HttpPost("batches/{key}/files")]
    [HasPermission(Permissions.LeagueContribute)]
    [RequestSizeLimit(ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    public Task<IActionResult> BatchFile(string key, IFormFile? file, [FromServices] LeagueBatchUploadService batches, CancellationToken ct) =>
        WithClubAsync(ct, async club =>
        {
            if (await ClubUpload.ReadAsync(file) is not { } data) return BadRequest(ClubUpload.FileError(file));
            return ClubUpload.BatchResult(this, await batches.AddFileAsync(LeagueBatchUploadService.Uploader.User(GetUserId(), club.Id), key, data,
                file!.ContentType, file.FileName, ct));
        });

    [HttpPost("batches/{key}/finish")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> BatchFinish(string key, [FromServices] LeagueBatchUploadService batches, CancellationToken ct) =>
        WithClubAsync(ct, async club =>
            ClubUpload.BatchResult(this, await batches.FinishAsync(LeagueBatchUploadService.Uploader.User(GetUserId(), club.Id), key, ct)));

    /// <summary>Alle offenen Liga-Einlesungen des Vereins, auch fremde und über Teilen-Links (Verwalter).</summary>
    [HttpGet("admin/scans")]
    [HasPermission(Permissions.LeagueManage)]
    public Task<IActionResult> OpenScans(CancellationToken ct) => WithClubAsync(ct, async club =>
        Ok(await _scans.LeagueOpenScansAsync(GetUserId(), club.Id, ct)));

    [HttpGet("scans/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> Scan(int id, CancellationToken ct) => WithClubAsync(ct, async club =>
        await _scans.LeagueScanStateAsync(await MeAsync(club), id, ct) is { } s ? Ok(s) : NotFound());

    [HttpGet("scans/{id:int}/photo")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> Photo(int id, [FromQuery] int page = 1, [FromQuery] int view = 0, CancellationToken ct = default) =>
        WithClubAsync(ct, async club => ClubUpload.PhotoResult(this, await _scans.LeagueScanPhotoAsync(await MeAsync(club), id, page, view)));

    public sealed record MergeScanRequest(int OtherId);

    /// <summary>Zwei Einlesungen desselben Formulars zusammenführen (0.736.0): die Fotos von <c>otherId</c> werden weitere
    /// Fotos dieser Einlesung, die andere wird geschlossen, diese wird neu gelesen → der neue Stand; 400 <c>reason</c> ∈
    /// same/busy/tooManyViews, 404 fremd/unbekannt.</summary>
    [HttpPost("scans/{id:int}/merge")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> Merge(int id, [FromBody] MergeScanRequest req, CancellationToken ct) => WithClubAsync(ct, async club =>
    {
        var me = await MeAsync(club);
        var reason = await _scans.MergeLeagueScansAsync(me, id, req?.OtherId ?? 0, ct);
        if (reason == "notFound") return NotFound();
        if (reason != null) return BadRequest(new { reason });
        _signal.Wake();
        return await _scans.LeagueScanStateAsync(me, id, ct) is { } s ? Ok(s) : NotFound();
    });

    [HttpPost("scans/{id:int}/resolve")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> Resolve(int id, [FromBody] ScoresheetResolveRequestDto dto, CancellationToken ct) => WithClubAsync(ct, async club =>
    {
        var me = await MeAsync(club);
        return await ClubUpload.ResolveAsync(this, () => _scans.ResolveLeagueRestAsync(me, id, dto?.Prefix ?? new(), dto?.WrittenFrom ?? 0));
    });

    /// <summary>Einlesung verwerfen: Foto und Lesung weg, die Zeile bleibt fürs Tageskontingent.</summary>
    [HttpDelete("scans/{id:int}")]
    [HasPermission(Permissions.LeagueContribute)]
    public Task<IActionResult> Discard(int id, CancellationToken ct) => WithClubAsync(ct, async club =>
        await _scans.CloseLeagueScanAsync(await MeAsync(club), id) ? NoContent() : NotFound());
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

    /// <summary>Der gültige Link samt SEINEM Verein (<see cref="LeagueShare.ClubId"/>, Mandanten-Schritt 2026-10-07) — alles,
    /// was über den Link hereinkommt, gehört diesem Verein. <c>null</c> = kein gültiger Link (404).</summary>
    private Task<(string Token, LeagueClub Club)?> LinkAsync(string token, CancellationToken ct) => _league.ShareContextAsync(token, ct);
    private string IpHash => _scans.AnonIpHash(HttpContext.Connection.RemoteIpAddress);

    [HttpPost("games/preview")]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public async Task<IActionResult> Preview(string token, [FromBody] LeagueClubPreviewRequest req, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        return ClubUpload.CheckPgn(req?.Pgn) is { } bad ? BadRequest(bad) : Ok(await _club.PreviewAsync(link.Club, null, req!.Pgn, ct));
    }

    [HttpPost("games/import")]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public async Task<IActionResult> Import(string token, [FromBody] LeagueClubImportRequest req, CancellationToken ct)
    {
        // Das Token der Link-Zeile, nicht der Routen-Wert: dieselbe Datenbank-Zeile findet sich auch mit anderer
        // Groß/Kleinschreibung (siehe LeagueService.ValidShareTokenAsync) — Vermerk und Deckel hängen am Link, nicht an
        // seiner Schreibweise.
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        return ClubUpload.CheckPgn(req?.Pgn) is { } bad ? BadRequest(bad)
            : Ok(await _club.ImportViaShareAsync(link.Club, link.Token, req!.Pgn, req.Games, ct));
    }

    /// <summary>Eine Partie aus einem Partieformular; <c>scanKey</c> = der Schlüssel der Einlesung (wird geschlossen).</summary>
    [HttpPost("games")]
    public async Task<IActionResult> Add(string token, [FromBody] LeagueClubGameRequest req, [FromQuery] string? scanKey, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();   // wie beim Import
        if (req is null) return BadRequest(new { reason = "empty", message = "Body required." });
        req.ScanId = null;
        var claimKey = LeagueClubService.NewClaimKey();
        var (game, reason, message) = await _club.AddGameViaShareAsync(link.Club, link.Token, req, ct, claimKey);
        if (game == null) return BadRequest(new { reason, message = message ?? "Game not accepted." });
        if (!string.IsNullOrWhiteSpace(scanKey))
            await _scans.CloseLeagueScanAsync(Actor.Anonymous(scanKey) with { ClubId = link.Club.Id }, null, game.Pgn, game.Id);
        if (!string.IsNullOrWhiteSpace(scanKey) && req.Plies != null) await _scans.SaveClubEditStateAsync(game.Id, req.Plies, game.Pgn, ct);
        return Ok(new { id = game.Id, anonymized = game.Anonymized, claimKey, replaced = game.Replaced });
    }

    [HttpGet("players")]
    public async Task<IActionResult> Players(string token, [FromQuery] string? q, [FromQuery] bool all,
        CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        return Ok(string.IsNullOrWhiteSpace(q) ? new List<LeagueRosterPersonDto>() : await _club.SuggestAsync(link.Club, q, all, ct));
    }

    [HttpPost("games/lichess")]
    public async Task<IActionResult> Lichess(string token, [FromBody] LeagueClubLichessRequest req, [FromServices] LichessStudySource lichess,
        CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is null) return NotFound();
        var (pgn, reason) = await lichess.FetchAsync(req?.Url, ct);
        return pgn == null ? BadRequest(new { reason, message = "Study not loaded." }) : Ok(new { pgn });
    }

    [HttpPost("games/chessbase")]
    [RequestSizeLimit(ChessBaseImportService.MaxBodyBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ChessBaseImportService.MaxBodyBytes + 1024 * 1024)]
    public async Task<IActionResult> ChessBaseUpload(string token, [FromServices] ChessBaseImportService chessBase, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is null) return NotFound();
        return ClubUpload.ChessBaseResult(this, await chessBase.ConvertAsync(await ClubUpload.FormFilesAsync(Request, ct), ct));
    }

    [HttpPost("match")]
    public async Task<IActionResult> Match(string token, [FromBody] LeagueClubMatchRequest req, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        return Ok(await _club.MatchAsync(link.Club, req?.White, req?.Black, ct));
    }

    /// <summary>Brettpaarungen für das Formular über den Teilen-Link (0.678.0) — die Paarungen sind öffentlich (chess-results).</summary>
    [HttpPost("pairings")]
    public async Task<IActionResult> Pairings(string token, [FromBody] LeagueClubPairingQuery req, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        return Ok(await _club.SuggestPairingsAsync(link.Club, req ?? new LeagueClubPairingQuery(), ct));
    }

    [HttpGet("scoresheet/status")]
    public async Task<IActionResult> ScoresheetStatus(string token, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is null) return NotFound();
        return Ok(await _scans.AnonStatusAsync(IpHash));
    }

    public sealed record KeysRequest(List<string>? Keys);

    // ── Entwürfe ohne Konto (0.595.0): gehören dem Browser mit dem Schlüssel ─────────────────────

    [HttpPost("drafts")]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public async Task<IActionResult> CreateDraft(string token, [FromBody] LeagueClubDraftCreateRequest req,
        [FromServices] LeagueClubDraftService drafts, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        if (ClubUpload.CheckPgn(req?.Pgn) is { } bad) return BadRequest(bad);
        var (draft, reason) = await drafts.CreateAsync(link.Club.Id, null, IpHash, req!.Pgn, req.Source, req.Label, ct);
        return draft == null ? BadRequest(new { reason, message = "Too many open lists." }) : Ok(draft);
    }

    [HttpPost("drafts/lookup")]
    public async Task<IActionResult> DraftLookup(string token, [FromBody] KeysRequest req, [FromServices] LeagueClubDraftService drafts,
        CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        return Ok(await drafts.ListAsync(DraftActor.Anonymous(null, link.Club.Id), req?.Keys ?? new(), ct));
    }

    [HttpGet("drafts/{key}")]
    public async Task<IActionResult> Draft(string token, string key, [FromServices] LeagueClubDraftService drafts, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        return await drafts.GetAsync(DraftActor.Anonymous(key, link.Club.Id), null, ct) is { } d ? Ok(d) : NotFound();
    }

    [HttpPut("drafts/{key}")]
    [RequestSizeLimit(ClubUpload.MaxBodyBytes)]
    public async Task<IActionResult> SaveDraft(string token, string key, [FromBody] LeagueClubDraftSaveRequest req,
        [FromServices] LeagueClubDraftService drafts, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        var (found, reason) = await drafts.SaveAsync(DraftActor.Anonymous(key, link.Club.Id), null, req ?? new(), ct);
        return !found ? NotFound() : reason != null ? BadRequest(new { reason }) : NoContent();
    }

    [HttpDelete("drafts/{key}")]
    public async Task<IActionResult> DeleteDraft(string token, string key, [FromServices] LeagueClubDraftService drafts, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        return await drafts.DeleteAsync(DraftActor.Anonymous(key, link.Club.Id), null, ct) ? NoContent() : NotFound();
    }

    /// <summary>Die offenen Einlesungen zu den Schlüsseln, die der Browser sich gemerkt hat (nur die des Vereins des Links).</summary>
    [HttpPost("scans/lookup")]
    public async Task<IActionResult> Lookup(string token, [FromBody] KeysRequest req, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        var found = await _scans.LeagueScansByKeysAsync(req?.Keys ?? new(), link.Club.Id);
        return Ok(found.Select(f => new { key = f.Key, scan = f.Scan }));
    }

    /// <summary>Foto OHNE Konto hochladen → <c>{ key, scan }</c>. Absagen wie angemeldet, dazu <c>anonDailyLimit</c>.</summary>
    [HttpPost("scans")]
    [RequestSizeLimit(ScoresheetScanService.MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ScoresheetScanService.MaxUploadRequestBytes)]
    public async Task<IActionResult> Upload(string token, [FromForm] List<IFormFile>? file, [FromForm] string? language,
        [FromForm] string? side, [FromForm] string? layout, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        var (pages, error) = await ClubUpload.ReadPagesAsync(file, layout);
        if (error != null) return BadRequest(error);
        var (scan, key, reason) = await _scans.CreateAnonymousAsync(pages!, language, side, IpHash, link.Club.Id);
        if (ClubUpload.Refusal(reason) is { } refused) return refused;
        _signal.Wake();
        return Accepted(new { key, scan });
    }

    [HttpPost("batches")]
    public async Task<IActionResult> BatchStart(string token, [FromBody] LeagueClubController.BatchStartRequest? req,
        [FromServices] LeagueBatchUploadService batches, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        return Ok(await batches.StartAsync(LeagueBatchUploadService.Uploader.Share(link.Token, IpHash, link.Club.Id), req?.Comment, ct));
    }

    [HttpPost("batches/{key}/files")]
    [RequestSizeLimit(ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ScoresheetScanService.MaxUploadBytes + 1024 * 1024)]
    public async Task<IActionResult> BatchFile(string token, string key, IFormFile? file, [FromServices] LeagueBatchUploadService batches,
        CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        if (await ClubUpload.ReadAsync(file) is not { } data) return BadRequest(ClubUpload.FileError(file));
        return ClubUpload.BatchResult(this, await batches.AddFileAsync(LeagueBatchUploadService.Uploader.Share(link.Token, IpHash, link.Club.Id), key, data,
            file!.ContentType, file.FileName, ct));
    }

    [HttpPost("batches/{key}/finish")]
    public async Task<IActionResult> BatchFinish(string token, string key, [FromServices] LeagueBatchUploadService batches, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        return ClubUpload.BatchResult(this, await batches.FinishAsync(LeagueBatchUploadService.Uploader.Share(link.Token, IpHash, link.Club.Id), key, ct));
    }

    [HttpGet("scans/{key}")]
    public async Task<IActionResult> Scan(string token, string key, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        return await _scans.LeagueScanStateAsync(Actor.Anonymous(key) with { ClubId = link.Club.Id }, null, ct) is { } s ? Ok(s) : NotFound();
    }

    [HttpGet("scans/{key}/photo")]
    public async Task<IActionResult> Photo(string token, string key, [FromQuery] int page = 1, [FromQuery] int view = 0,
        CancellationToken ct = default)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        return ClubUpload.PhotoResult(this,
            await _scans.LeagueScanPhotoAsync(Actor.Anonymous(key) with { ClubId = link.Club.Id }, null, page, view));
    }

    [HttpPost("scans/{key}/resolve")]
    public async Task<IActionResult> Resolve(string token, string key, [FromBody] ScoresheetResolveRequestDto dto,
        CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        return await ClubUpload.ResolveAsync(this,
            () => _scans.ResolveLeagueRestAsync(Actor.Anonymous(key) with { ClubId = link.Club.Id }, null, dto?.Prefix ?? new(), dto?.WrittenFrom ?? 0));
    }

    [HttpDelete("scans/{key}")]
    public async Task<IActionResult> Discard(string token, string key, CancellationToken ct)
    {
        if (await LinkAsync(token, ct) is not { } link) return NotFound();
        return await _scans.CloseLeagueScanAsync(Actor.Anonymous(key) with { ClubId = link.Club.Id }, null) ? NoContent() : NotFound();
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

    /// <summary>Die Fotos EINES Formulars (1 bis <see cref="ScoresheetScanService.MaxPages"/> Teile <c>file</c>, in
    /// Seitenreihenfolge) — sonst die Absage (<c>noFile</c>/<c>tooManyPages</c>/<c>tooLarge</c>/<c>invalidLayout</c>).
    /// <paramref name="layout"/> (0.736.0): je Teil die Seite, zu der es gehört („1,1,2" = zwei Fotos von Seite 1, eins von
    /// Seite 2); leer = jedes Foto eine Seite.</summary>
    public static async Task<(List<ScoresheetUpload>? Pages, object? Error)> ReadPagesAsync(List<IFormFile>? file, string? layout = null)
    {
        var files = (file ?? new()).Where(f => f.Length > 0).ToList();
        if (files.Count == 0) return (null, new { reason = "noFile", message = "No file." });
        var sheetPages = new List<int>();
        if (!string.IsNullOrWhiteSpace(layout))
        {
            foreach (var part in layout.Split(',', StringSplitOptions.TrimEntries))
                if (int.TryParse(part, out var n) && n is >= 1 and <= ScoresheetScanService.MaxPages) sheetPages.Add(n);
                else return (null, new { reason = "invalidLayout", message = "Invalid layout." });
            if (sheetPages.Count != files.Count) return (null, new { reason = "invalidLayout", message = "Invalid layout." });
        }
        var maxFiles = sheetPages.Count > 0 ? ScoresheetScanService.MaxPages * ScoresheetScanService.MaxPhotosPerPage
            : ScoresheetScanService.MaxPages;
        if (files.Count > maxFiles)
            return (null, new { reason = "tooManyPages", message = $"At most {maxFiles} photos." });
        if (files.Any(f => f.Length > ScoresheetScanService.MaxUploadBytes))
            return (null, new { reason = "tooLarge", message = "File too large." });
        var pages = new List<ScoresheetUpload>();
        foreach (var f in files)
        {
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms);
            pages.Add(new ScoresheetUpload(ms.ToArray(), f.ContentType, f.FileName, sheetPages.Count > 0 ? sheetPages[pages.Count] : 0));
        }
        return (pages, null);
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

    public static async Task<IActionResult> ResolveAsync(ControllerBase c,
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
