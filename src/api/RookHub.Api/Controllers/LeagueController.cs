using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.League;

namespace RookHub.Api.Controllers;

/// <summary>
/// LeagueHub (leaguehub(-dev).oberschmid.homes): Aufstellungs-Prognosen der Tiroler Mannschaftsmeisterschaft.
/// Lesen hinter <see cref="Permissions.LeagueView"/>, Aktualisieren/Teilen/Import hinter
/// <see cref="Permissions.LeagueManage"/> — vorerst haben beides nur Admins (Wunsch 2026-09-27).
/// </summary>
[ApiController]
[Route("api/league")]
[Authorize]
public class LeagueController : BaseApiController
{
    private readonly LeagueService _league;
    private readonly LeagueImportService _import;
    private readonly LeagueUpdateService _update;
    private readonly LeagueClubResolver _clubs;

    public LeagueController(LeagueService league, LeagueImportService import, LeagueUpdateService update, LeagueClubResolver clubs)
    {
        _league = league; _import = import; _update = update; _clubs = clubs;
    }

    /// <summary>Im Verein der Anfrage (<c>?club=</c>, <see cref="LeagueClubResolver"/>), sonst die Absage.</summary>
    private async Task<IActionResult> WithClubAsync(CancellationToken ct, Func<LeagueClub, Task<IActionResult>> run)
    {
        var (club, error) = await LeagueClubAsync(_clubs, ct);
        return club is null ? error! : await run(club);
    }

    /// <summary>
    /// Die Vereine des Kontos (Mandanten-Schritt 2026-10-07) → <c>{ clubs[{ id, name, anonName, teamPrefix, source }], current }</c>
    /// — <c>current</c> = der Verein ohne <c>?club=</c> (bei mehreren und ohne Vorgabe <c>null</c>; die Seite nimmt dann den
    /// gemerkten bzw. ersten). Nur angemeldet; ohne Verein eine leere Liste. LeagueHub schickt die Id danach als <c>?club=</c>
    /// an jeden Aufruf.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var mine = await _clubs.ClubsOfAsync(GetUserId(), IsAdmin, ct);
        var current = await _clubs.ResolveAsync(GetUserId(), IsAdmin, null, ct);
        return Ok(new JsonObject
        {
            ["clubs"] = new JsonArray(mine.Select(c => (JsonNode)LeagueService.ClubJson(c)).ToArray()),
            ["current"] = current.Club?.Id,
        });
    }

    /// <summary>Startseite: die Ligen der Region, in denen der Verein eine Mannschaft hat (0.710.0). <c>?all=true</c> = alle Ligen
    /// der Region — nur für Verwalter (<see cref="Permissions.LeagueManage"/>, live wie bei den Paarungen); ohne das Recht wird der
    /// Schalter STILL übergangen (gefiltert, <c>filtered: true</c>) statt 400: ein gemerkter Schalter nach entzogenem Recht soll die
    /// Seite nicht leer machen.</summary>
    [HttpGet("index")]
    [HasPermission(Permissions.LeagueView)]
    public Task<IActionResult> Index([FromQuery] bool? all, [FromServices] PermissionResolver permissions, CancellationToken ct) =>
        WithClubAsync(ct, async club =>
        {
            var everything = all == true
                && (User.IsInRole("Admin") || (await permissions.GetAsync(GetUserId())).Has(Permissions.LeagueManage));
            return Ok(await _league.IndexAsync(club, ct, everything));
        });

    /// <summary>Fertig gerechnete Liga (alle Teams, alle Runden) — Feldnamen wie in der Python-Fassung.</summary>
    [HttpGet("{tnr:int}")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> League(int tnr, CancellationToken ct) =>
        await _league.LeagueJsonAsync(tnr, ct) is { } json ? Content(json, "application/json") : NotFound();

    /// <summary>Brettpaarungen einer gespielten Begegnung samt Partie, wo es eine gibt (0.673.0, <see cref="LeagueFixtureGames"/>).</summary>
    [HttpGet("{tnr:int}/round/{round:int}/games")]
    [HasPermission(Permissions.LeagueView)]
    public Task<IActionResult> FixtureGames(int tnr, int round, [FromQuery] string? team, [FromServices] LeagueFixtureGames games,
        [FromServices] PermissionResolver permissions, CancellationToken ct) => WithClubAsync(ct, async club =>
    {
        if (string.IsNullOrWhiteSpace(team)) return BadRequest();
        var me = GetUserId();
        // Verwalter LIVE wie in LeagueClubController (0.589.0) — eine eben vergebene Rolle gilt sofort
        var manage = User.IsInRole("Admin") || (await permissions.GetAsync(me)).Has(Permissions.LeagueManage);
        // Vereinspartien nur aus der Vereins-Datenbank des Vereins der Anfrage
        return Ok(await games.ForFixtureAsync(club, tnr, round, team, ct, me, manage));
    });

    /// <summary>Partien im Bestand je Quelle (0.626.0) → <c>{ board[{ key, label, games }], boardTotal, online[…], onlineTotal, countedAt }</c>;
    /// 30 min im Speicher. Seit 0.628.0: <c>?tnr=</c> fügt <c>league</c> hinzu (alle Meldelisten dieser Liga), <c>?fides=1,2,…</c>
    /// (die Meldeliste des Gegners, höchstens 40) <c>opponent</c>.</summary>
    [HttpGet("sources")]
    [HasPermission(Permissions.LeagueView)]
    public Task<IActionResult> Sources([FromQuery] int? tnr, [FromQuery] string? fides, [FromServices] LeagueGameSources sources,
        CancellationToken ct) => WithClubAsync(ct, async club =>
        Ok(await sources.GetAsync(club.Id, ct, fides?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            leagueTnr: tnr)));

    /// <summary>Treffer der Prognose in den bisherigen Runden (0.650.0): je Runde, je Liga, gesamt — über alle Begegnungen.</summary>
    [HttpGet("forecast-stats")]
    [HasPermission(Permissions.LeagueView)]
    public Task<IActionResult> ForecastStats(CancellationToken ct) => WithClubAsync(ct, async club =>
        Ok(await _league.ForecastStatsAsync(club.Region, ct)));

    [HttpGet("player/{fide}")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Player(string fide, CancellationToken ct) =>
        await _league.CardAsync(fide, onlySure: false, ct, reveal: IsAdmin) is { } c ? Ok(c) : NotFound();

    [HttpGet("player/{fide}/pgn")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Pgn(string fide, CancellationToken ct) =>
        await _league.PgnAsync(fide, ct) is { } p ? PgnFile(fide, p.Name, p.Pgn) : NotFound();

    /// <summary>Die letzten Partien der Karte samt PGN (zum Nachspielen); <c>color</c> w/s = nur mit dieser Farbe.</summary>
    [HttpGet("player/{fide}/recent")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Recent(string fide, [FromQuery] string? color, CancellationToken ct) =>
        await _league.RecentAsync(fide, ct, color) is { } r ? Ok(r) : NotFound();

    /// <summary>Eröffnungsbaum: <c>color</c> w/s, <c>line</c> = Züge mit Leerzeichen (englische SAN); Filter (0.605.0)
    /// <c>source</c> board/both/online, <c>speeds</c> = Komma-Liste für Online-Partien, <c>years</c> = nur die letzten x Jahre,
    /// Online-Partien nur gesicherter Konten — <c>unsure=true</c> nimmt die unsicheren dazu (0.612.0; über einen Teilen-Link nie).</summary>
    [HttpGet("player/{fide}/tree")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Tree(string fide, [FromQuery] string? color, [FromQuery] string? line, [FromQuery] string? source,
        [FromQuery] string? speeds, [FromQuery] int? years, [FromQuery] bool? unsure, CancellationToken ct) =>
        await _league.TreeAsync(fide, color ?? "w", line, ct, LeagueProfileStore.TreeFilter.Parse(source, speeds, years, onlySure: unsure != true))
            is { } t ? Ok(t) : NotFound();

    /// <summary>Eröffnungsprofil der Karte über gefilterte Partien (0.617.0) — dieselben Filter wie der Baum; ohne <c>unsure=true</c>
    /// nur Online-Partien gesicherter Konten.</summary>
    [HttpGet("player/{fide}/profile")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Profile(string fide, [FromQuery] string? source, [FromQuery] string? speeds, [FromQuery] int? years,
        [FromQuery] bool? unsure, CancellationToken ct) =>
        await _league.ProfileAsync(fide, ct, LeagueProfileStore.TreeFilter.Parse(source, speeds, years, onlySure: unsure != true))
            is { } p ? Ok(p) : NotFound();

    // ---- Online-Konten (0.605.0) ----------------------------------------------------------------

    /// <summary>Konto anlegen <c>{ site, user (Name oder Profiladresse), sure, comment }</c> → das Konto; 400 <c>reason</c> ∈
    /// <c>invalidSite</c>/<c>invalidUser</c>/<c>duplicate</c>/<c>tooMany</c>, 404 <c>unknownPlayer</c>.</summary>
    [HttpPost("player/{fide}/accounts")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> AddAccount(string fide, [FromBody] LeagueOnlineAccountService.Input req,
        [FromServices] LeagueOnlineAccountService accounts, CancellationToken ct) =>
        await AccountResultAsync(await accounts.CreateAsync(fide, req ?? new(null, null, null, null), ct, User.Identity?.Name), accounts, ct);

    /// <summary>Ändern — fehlende Felder bleiben; ein anderer Name/eine andere Seite holt die Partien neu.</summary>
    [HttpPut("accounts/{id:int}")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> UpdateAccount(int id, [FromBody] LeagueOnlineAccountService.Input req,
        [FromServices] LeagueOnlineAccountService accounts, CancellationToken ct) =>
        await AccountResultAsync(await accounts.UpdateAsync(id, req ?? new(null, null, null, null), ct), accounts, ct);

    [HttpDelete("accounts/{id:int}")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> DeleteAccount(int id, [FromServices] LeagueOnlineAccountService accounts, CancellationToken ct) =>
        await accounts.DeleteAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>Partien dieses Kontos gleich (neu) abrufen lassen.</summary>
    /// <summary>Konto-Prüfung (i) eines eingetragenen Kontos (0.619.0) → <c>{ site, user, url, player, elo, checkedAt, profileLoaded,
    /// items[{ key, label, status (ok/weak/warn/fail/none/info), text }] }</c>; 404 unbekannt oder verborgen (minderjährig — außer für Admins).</summary>
    [HttpGet("accounts/{id:int}/checks")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> AccountChecks(int id, [FromServices] LeagueAccountChecks checks, CancellationToken ct) =>
        await checks.ForAccountAsync(id, ct, reveal: IsAdmin) is { } r ? Ok(r) : NotFound();

    [HttpPost("accounts/{id:int}/sync")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> SyncAccount(int id, [FromServices] LeagueOnlineAccountService accounts, CancellationToken ct) =>
        await accounts.RequestSyncAsync(id, ct) is { } a ? Ok(await accounts.JsonAsync(a, ct, reveal: IsAdmin)) : NotFound();

    // ---- Konto-Vorschläge (0.607.0) ------------------------------------------------------------

    /// <summary>Alle offenen Vorschläge (stärkste zuerst) samt Stand der Suche <c>{ items, scanned, total }</c>.</summary>
    [HttpGet("suggestions")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> Suggestions([FromServices] LeagueOnlineAccountService accounts, CancellationToken ct) =>
        Ok(await accounts.SuggestionsAsync(null, ct, reveal: IsAdmin));

    [HttpGet("player/{fide}/suggestions")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> PlayerSuggestions(string fide, [FromServices] LeagueOnlineAccountService accounts, CancellationToken ct) =>
        Ok(await accounts.SuggestionsAsync(fide, ct, reveal: IsAdmin));

    /// <summary>Für diesen Spieler jetzt suchen → <c>{ items, found, skipped }</c> (<c>skipped</c> bleibt seit 0.610.0 leer; früher „minderjährig" /
    /// „Jahrgang unbekannt"); 404 unbekannter Spieler, 503 <c>rateLimited</c>/<c>unreachable</c>.</summary>
    [HttpPost("player/{fide}/suggestions/scan")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> ScanSuggestions(string fide, [FromServices] LeagueAccountFinder finder,
        [FromServices] LeagueOnlineAccountService accounts, CancellationToken ct)
    {
        if (await finder.PlayerAsync(fide, ct) is not { } player) return NotFound(new { reason = "unknownPlayer" });
        LeagueAccountFinder.ScanResult r;
        try
        {
            r = await finder.ScanAsync(player, ct);
        }
        catch (LeagueOnlineSync.RateLimitedException)
        {
            return StatusCode(503, new { reason = "rateLimited" });
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            return StatusCode(503, new { reason = "unreachable" });
        }
        var res = await accounts.SuggestionsAsync(fide, ct, reveal: IsAdmin);
        res["found"] = r.Found;
        res["skipped"] = r.Skipped;
        return Ok(res);
    }

    public sealed record AcceptRequest(bool Sure);

    /// <summary>Übernehmen <c>{ sure }</c> → das neue Konto; 404 unbekannt/erledigt, 400 wie beim Anlegen.</summary>
    [HttpPost("suggestions/{id:int}/accept")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> AcceptSuggestion(int id, [FromBody] AcceptRequest? req, [FromServices] LeagueOnlineAccountService accounts,
        CancellationToken ct) =>
        await AccountResultAsync(await accounts.AcceptSuggestionAsync(id, req?.Sure == true, ct, User.Identity?.Name), accounts, ct);

    /// <summary>Konto-Prüfung (i) eines Vorschlags — wie bei einem Konto, die Partien für den Repertoire-Vergleich werden geholt.</summary>
    [HttpGet("suggestions/{id:int}/checks")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> SuggestionChecks(int id, [FromServices] LeagueAccountChecks checks, CancellationToken ct) =>
        await checks.ForSuggestionAsync(id, ct, reveal: IsAdmin) is { } r ? Ok(r) : NotFound();

    /// <summary>Selbstmeldungen einer Quelle einspielen (ersetzt die Einträge dieser Quelle) <c>{ source, items[{ fide, site, user, team }] }</c>
    /// → <c>{ added, updated, unchanged, removed, skipped[{ index, reason }], dryRun }</c>; 400 <c>noSource</c>/<c>tooMany</c>/<c>invalidReporter</c>.
    /// Seit 0.629.0 optional <c>reporter</c> (wer die Liste gemeldet hat — dann im (i) „Gemeldet von …" statt Selbstmeldung) und je
    /// Eintrag <c>note</c>.</summary>
    [HttpPost("admin/self-reports")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> ImportSelfReports([FromBody] LeagueSelfReportImport.Request? req, [FromQuery] bool dryRun,
        [FromServices] Data.AppDbContext db, CancellationToken ct)
    {
        var (outcome, reason) = await LeagueSelfReportImport.ImportAsync(db, req ?? new(null, null), dryRun, ct);
        return outcome is not null ? Ok(outcome) : BadRequest(new { reason });
    }

    [HttpPost("suggestions/{id:int}/reject")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> RejectSuggestion(int id, [FromServices] LeagueOnlineAccountService accounts, CancellationToken ct) =>
        await accounts.RejectSuggestionAsync(id, ct) ? NoContent() : NotFound();

    private async Task<IActionResult> AccountResultAsync((Models.LeagueOnlineAccount? Account, string? Reason) r,
        LeagueOnlineAccountService accounts, CancellationToken ct) => r switch
    {
        ({ } a, _) => Ok(await accounts.JsonAsync(a, ct, reveal: IsAdmin)),
        (_, "notFound" or "unknownPlayer") => NotFound(new { reason = r.Reason }),
        _ => BadRequest(new { reason = r.Reason }),
    };

    internal static FileContentResult PgnFile(string fide, string name, string pgn) =>
        new(Encoding.UTF8.GetBytes(pgn), "application/x-chess-pgn")
        {
            FileDownloadName = $"{string.Join("_", name.Split(',').Select(x => x.Trim()))}_{fide}.pgn"
                .Replace(' ', '_'),
        };

    // ---- Teilen-Links -------------------------------------------------------------------------------

    public sealed record ShareRequest(int Tnr, int Round, string Team);

    /// <summary>Teilen-Link anlegen — er gehört dem Verein der Anfrage (Uploads darüber landen in dessen Vereins-Datenbank).</summary>
    [HttpPost("share")]
    [HasPermission(Permissions.LeagueManage)]
    public Task<IActionResult> CreateShare([FromBody] ShareRequest req, CancellationToken ct) => WithClubAsync(ct, async club =>
    {
        var s = await _league.CreateShareAsync(club, req.Tnr, req.Round, req.Team, GetUserIdOrNull(), ct);
        return s is null
            ? BadRequest(new { message = "Diese Runde hat keine Prognose zum Teilen." })
            : Ok(new { token = s.Token, expires = s.Expires.ToString("yyyy-MM-dd") });
    });

    [HttpDelete("share/{token}")]
    [HasPermission(Permissions.LeagueManage)]
    public Task<IActionResult> DeleteShare(string token, CancellationToken ct) => WithClubAsync(ct, async club =>
        await _league.DeleteShareAsync(club, token, ct) ? NoContent() : NotFound());

    // ---- Vereine (Mandanten-Schritt 2026-10-07): nur Admins mit league.manage ------------------------------

    public sealed record ClubRequest(string? Name, string? TeamPrefix, string? AnonName, string? Region);

    /// <summary>Alle Vereine samt Gruppen und Partienzahl → <c>[{ id, name, anonName, teamPrefix, source, createdAt, clubGames,
    /// groups[{ id, name, members }] }]</c> (<see cref="LeagueClubAdminService.ListAsync"/>).</summary>
    [HttpGet("admin/clubs")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> Clubs([FromServices] LeagueClubAdminService admin, CancellationToken ct) =>
        IsAdmin ? Ok(await admin.ListAsync(ct)) : Forbid();

    /// <summary>Verein anlegen <c>{ name, teamPrefix, anonName, source }</c> → der Verein; 400 <c>reason</c> ∈
    /// <c>invalidName</c>/<c>invalidTeamPrefix</c>/<c>invalidAnonName</c>/<c>invalidRegion</c>, 409 <c>duplicate</c>.</summary>
    [HttpPost("admin/clubs")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> CreateClub([FromBody] ClubRequest? req, [FromServices] LeagueClubAdminService admin, CancellationToken ct)
    {
        if (!IsAdmin) return Forbid();
        var (club, reason) = await admin.CreateAsync(req?.Name, req?.TeamPrefix, req?.AnonName, req?.Region, ct);
        return ClubResult(club, reason);
    }

    /// <summary>Verein ändern (fehlende Felder bleiben; <c>source</c> leer = chess-results) → der Verein; 404 unbekannt.</summary>
    [HttpPut("admin/clubs/{id:int}")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> UpdateClub(int id, [FromBody] ClubRequest? req, [FromServices] LeagueClubAdminService admin, CancellationToken ct)
    {
        if (!IsAdmin) return Forbid();
        var (club, reason) = await admin.UpdateAsync(id, req?.Name, req?.TeamPrefix, req?.AnonName, req?.Region, ct);
        return ClubResult(club, reason);
    }

    /// <summary>Gruppe dem Verein zuordnen (eine Gruppe gehört zu höchstens einem Verein — eine andere Zuordnung wird ersetzt);
    /// der Taktik-Kurs des Vereins wird für die Gruppe freigegeben. 404 Verein/Gruppe unbekannt, 400 <c>everyone</c>.</summary>
    [HttpPost("admin/clubs/{id:int}/groups/{groupId:int}")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> AddClubGroup(int id, int groupId, [FromServices] LeagueClubAdminService admin, CancellationToken ct)
    {
        if (!IsAdmin) return Forbid();
        return await admin.AddGroupAsync(id, groupId, ct) switch
        {
            null => NoContent(),
            "everyone" => BadRequest(new { reason = "everyone" }),
            var r => NotFound(new { reason = r }),
        };
    }

    /// <summary>Gruppe vom Verein lösen → 204; 404, wenn sie nicht dazugehört.</summary>
    [HttpDelete("admin/clubs/{id:int}/groups/{groupId:int}")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> RemoveClubGroup(int id, int groupId, [FromServices] LeagueClubAdminService admin, CancellationToken ct)
    {
        if (!IsAdmin) return Forbid();
        return await admin.RemoveGroupAsync(id, groupId, ct) ? NoContent() : NotFound();
    }

    private IActionResult ClubResult(LeagueClub? club, string? reason) => (club, reason) switch
    {
        ({ } c, _) => Ok(LeagueService.ClubJson(c)),
        (_, "notFound") => NotFound(new { reason }),
        (_, "duplicate") => Conflict(new { reason }),
        _ => BadRequest(new { reason }),
    };

    // ---- Aktualisieren (Knopf, kein Zeitplan — Wunsch des Nutzers) ------------------------------------

    [HttpPost("update")]
    [HasPermission(Permissions.LeagueManage)]
    public IActionResult StartUpdate() => _update.TryStart() switch
    {
        LeagueUpdateService.StartResult.Started => Accepted(_update.Status()),
        LeagueUpdateService.StartResult.Running => Conflict(_update.Status()),
        _ => StatusCode(429, _update.Status()),
    };

    [HttpGet("update/status")]
    [HasPermission(Permissions.LeagueView)]
    public IActionResult UpdateStatus() => Ok(_update.Status());

    // ---- Übernahme aus der Python-Fassung --------------------------------------------------------------

    /// <summary>Rumpf-Obergrenze von <c>admin/import</c> — unkomprimiert UND (bei gzip) entpackt.</summary>
    internal const long ImportMaxBytes = 200L * 1024 * 1024;
    /// <summary>Rumpf-Obergrenze von <c>admin/games</c> und <c>admin/mega-players</c> — unkomprimiert UND entpackt.
    /// Real (28.09.): Megabase-Auswahl 52 MB PGN, Spielerverzeichnis 15 MB TSV.</summary>
    internal const long CollectionMaxBytes = 400L * 1024 * 1024;
    // Entpackt-Grenzen; Tests verkleinern sie, statt Hunderte MB zu erzeugen.
    internal long ImportUnpackedLimit { get; init; } = ImportMaxBytes;
    internal long CollectionUnpackedLimit { get; init; } = CollectionMaxBytes;

    /// <summary>Der Rumpf, bei <c>Content-Encoding: gzip</c> entpackt — aber höchstens <paramref name="limit"/> Bytes:
    /// <c>RequestSizeLimit</c> zählt nur die komprimierten Bytes, eine gzip-Bombe hätte sonst den ganzen API-Prozess
    /// (alle Nutzer) in den Speicherdruck getrieben. Darüber wirft das Lesen <see cref="LimitedReadStream.LimitExceededException"/>.</summary>
    private Stream AdminBody(long limit) =>
        Request.Headers.ContentEncoding.ToString().Contains("gzip", StringComparison.OrdinalIgnoreCase)
            ? new LimitedReadStream(new GZipStream(Request.Body, CompressionMode.Decompress), limit)
            : Request.Body;

    private ObjectResult UnpackedTooLarge(long limit) =>
        StatusCode(StatusCodes.Status413PayloadTooLarge, new { message = $"entpackt größer als {limit / (1024 * 1024)} MB — bitte aufteilen" });

    /// <summary>Bestand übernehmen (JSON, gern gzip-komprimiert mit <c>Content-Encoding: gzip</c>); 413, wenn der Rumpf
    /// entpackt größer als <see cref="ImportMaxBytes"/> ist.</summary>
    [HttpPost("admin/import")]
    [HasPermission(Permissions.LeagueManage)]
    [RequestSizeLimit(ImportMaxBytes)]
    public async Task<IActionResult> Import([FromQuery] bool rebuild, CancellationToken ct)
    {
        var body = AdminBody(ImportUnpackedLimit);
        LeagueImportService.Bundle? bundle;
        try { bundle = await JsonSerializer.DeserializeAsync<LeagueImportService.Bundle>(body, LeagueImportService.Json, ct); }
        catch (JsonException ex) { return BadRequest(new { message = ex.Message }); }
        catch (LimitedReadStream.LimitExceededException) { return UnpackedTooLarge(ImportUnpackedLimit); }
        if (bundle is null) return BadRequest(new { message = "leer" });
        var res = await _import.ImportAsync(bundle, ct);
        if (rebuild) res["views"] = await _league.RebuildViewsAsync(ct);
        return Ok(res);
    }

    /// <summary>
    /// Eine fremde Partiesammlung einspielen (PGN, gern gzip mit <c>Content-Encoding: gzip</c>), z. B. die aus der
    /// ChessBase-Megabase gefilterten Partien der TMM-Spieler (<c>source=Mega</c>, Skript <c>mega_decide.py</c> im
    /// league-analyzer). Zugeordnet wird NUR über die FIDE-ID im Kopf. 413, wenn der Rumpf entpackt größer als
    /// <see cref="CollectionMaxBytes"/> ist.
    /// </summary>
    [HttpPost("admin/games")]
    [HasPermission(Permissions.LeagueManage)]
    [RequestSizeLimit(CollectionMaxBytes)]
    public async Task<IActionResult> ImportGames([FromQuery] string? source, CancellationToken ct)
    {
        var src = (source ?? "").Trim();
        if (src.Length is 0 or > 20 || !src.All(char.IsLetterOrDigit)) return BadRequest(new { message = "source fehlt/ungültig" });
        var body = AdminBody(CollectionUnpackedLimit);
        string pgn;
        try
        {
            using var reader = new StreamReader(body, Encoding.UTF8);
            pgn = await reader.ReadToEndAsync(ct);
        }
        catch (LimitedReadStream.LimitExceededException) { return UnpackedTooLarge(CollectionUnpackedLimit); }
        var (games, players) = await _league.ImportGamesAsync(pgn, src, ct);
        return Ok(new { games, players });
    }

    /// <summary>Lichess-Übertragungen, die eingespielt werden (0.608.0) — jüngste zuerst, mit Stand.</summary>
    [HttpGet("admin/broadcasts")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> Broadcasts([FromServices] LeagueBroadcastImport broadcasts, CancellationToken ct) =>
        Ok(await broadcasts.ListAsync(ct));

    public sealed record BroadcastRequest(string? Url);

    /// <summary>Eine Übertragung per Link (Turnier oder Runde) hinzufügen und gleich einspielen → ihr Stand; 400
    /// <c>invalidUrl</c>, 404 <c>notFound</c>, 503 <c>rateLimited</c>/<c>unreachable</c>.</summary>
    [HttpPost("admin/broadcasts")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> AddBroadcast([FromBody] BroadcastRequest? req, [FromServices] LeagueBroadcastImport broadcasts,
        CancellationToken ct)
    {
        try
        {
            var (b, reason) = await broadcasts.AddAsync(req?.Url, ct);
            if (b is null) return reason == "notFound" ? NotFound(new { reason }) : BadRequest(new { reason });
            return Ok(new { tourId = b.TourId, name = b.Name, games = b.Games, finished = b.Finished, error = b.Error });
        }
        catch (LeagueOnlineSync.RateLimitedException)
        {
            return StatusCode(503, new { reason = "rateLimited" });
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            return StatusCode(503, new { reason = "unreachable" });
        }
    }

    /// <summary>Spielerverzeichnis der ganzen Megabase ersetzen (TSV, gern gzip) — Skript <c>scan_mega_players.py</c>.
    /// 413, wenn der Rumpf entpackt größer als <see cref="CollectionMaxBytes"/> ist (das alte Verzeichnis bleibt dann — wie
    /// bei jedem Abbruch mitten im Rumpf — vollständig stehen, <see cref="LeagueMegaPlayers.ReplaceAsync"/> ersetzt in EINER
    /// Transaktion; einfach mit der richtigen Datei wiederholen).</summary>
    [HttpPost("admin/mega-players")]
    [HasPermission(Permissions.LeagueManage)]
    [RequestSizeLimit(CollectionMaxBytes)]
    public async Task<IActionResult> ImportMegaPlayers([FromServices] LeagueMegaPlayers mega, CancellationToken ct)
    {
        using var reader = new StreamReader(AdminBody(CollectionUnpackedLimit), Encoding.UTF8);
        try { return Ok(new { players = await mega.ReplaceAsync(reader, ct) }); }
        catch (LimitedReadStream.LimitExceededException) { return UnpackedTooLarge(CollectionUnpackedLimit); }
    }

    [HttpPost("admin/rebuild")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> Rebuild(CancellationToken ct) => Ok(new { views = await _league.RebuildViewsAsync(ct) });

    /// <summary>Eine Liga des bayerischen Ligamanagers: Adresse ODER Region + Saison + Slug (mit Id); <c>Boards</c> = Bretter je
    /// Begegnung, solange noch keine Runde gespielt ist und keine frühere Saison derselben Liga eingespielt ist.</summary>
    public sealed record LigamanagerImportRequest(string? Url, string? Region, string? Season, string? Slug, int? Boards);

    /// <summary>
    /// Eine Liga aus dem SBV-Ligamanager einspielen (2026-10-07, <see cref="LigamanagerSource"/>) — Runden, Begegnungen,
    /// Brettpartien, Meldelisten und die Partien in die Spielerkarten; danach die Ansichten. <c>?dryRun=true</c> liest und zählt
    /// nur. 400 <c>invalidLeague</c>, 404 <c>notFound</c>, 409 <c>conflict</c> (Nummer gehört einer chess-results-Liga),
    /// 503 <c>unreachable</c>.
    /// </summary>
    [HttpPost("admin/ligamanager/import")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> LigamanagerImport([FromBody] LigamanagerImportRequest? req, [FromQuery] bool dryRun,
        [FromServices] LigamanagerSource ligamanager, CancellationToken ct)
    {
        var lref = LigamanagerSource.LeagueRef.Parse(req?.Url) ?? LigamanagerSource.LeagueRef.Of(req?.Region, req?.Season, req?.Slug);
        if (lref is null) return BadRequest(new { reason = "invalidLeague" });
        try
        {
            return Ok(await ligamanager.ImportAsync(lref, dryRun, ct, req?.Boards));
        }
        catch (LigamanagerSource.NotFoundException e) { return NotFound(new { reason = "notFound", message = e.Message }); }
        catch (LigamanagerSource.ConflictException e) { return Conflict(new { reason = "conflict", message = e.Message }); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return StatusCode(503, new { reason = "unreachable" });
        }
    }

    /// <summary>Eine chess-results-Liga (0.720.0): Turniernummer, Saison „2026/27", Tiroler Stufe (1 = 1. Bundesliga … 6 =
    /// Gebietsklasse), Liga, Gruppe, Phase („Liga" | „Playoff"), Name (leer = „{Liga} {Gruppe} {Saison}").</summary>
    public sealed record ChessResultsImportRequest(int? Tnr, string? Season, int? Level, string? League, string? Grp, string? Stage, string? Name);

    /// <summary>
    /// Eine chess-results-Liga über den Crawler einspielen (Österreichische Bundesliga, 0.720.0, <see cref="ChessResultsLeagueImport"/>) —
    /// laufende Saison oder Vorsaison; danach die Ansichten. <c>?dryRun=true</c> holt und zählt nur (u. a. FIDE-IDs ohne Spielerkarte).
    /// 400 <c>invalidLeague</c>, 404 <c>notFound</c>, 409 <c>conflict</c>, 502 <c>incomplete</c>, 503 <c>unreachable</c>.
    /// </summary>
    [HttpPost("admin/chessresults/import")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> ChessResultsImport([FromBody] ChessResultsImportRequest? req, [FromQuery] bool dryRun,
        [FromServices] ChessResultsLeagueImport import, CancellationToken ct)
    {
        if (req?.Tnr is not { } tnr || req.Level is not { } level || string.IsNullOrWhiteSpace(req.Season) || string.IsNullOrWhiteSpace(req.League))
            return BadRequest(new { reason = "invalidLeague", message = "tnr, season, level und league sind Pflicht" });
        try
        {
            return Ok(await import.ImportAsync(new ChessResultsLeagueImport.Request(tnr, req.Season, level, req.League, req.Grp, req.Stage, req.Name), dryRun, ct));
        }
        catch (ChessResultsLeagueImport.InvalidException e) { return BadRequest(new { reason = "invalidLeague", message = e.Message }); }
        catch (ChessResultsLeagueImport.NotFoundException e) { return NotFound(new { reason = "notFound", message = e.Message }); }
        catch (ChessResultsLeagueImport.ConflictException e) { return Conflict(new { reason = "conflict", message = e.Message }); }
        catch (ChessResultsLeagueImport.IncompleteException e) { return StatusCode(502, new { reason = "incomplete", message = e.Message }); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            return StatusCode(503, new { reason = "unreachable" });
        }
    }

    /// <summary>Eine Liga des Schachkreises Zugspitze: Adresse (<c>https://schachkreis-zugspitze.de/ergebnisse/?Saison=2026&amp;Liga=1</c>)
    /// ODER Liga-Id + Saison („2026/27", leer = laufende); <c>Boards</c> wie beim Ligamanager.</summary>
    public sealed record ZugspitzeImportRequest(string? Url, int? LigaId, string? Season, int? Boards);

    /// <summary>
    /// Eine Liga des Schachkreises Zugspitze einspielen (2026-10-07, <see cref="ZugspitzeSource"/>) — Runden, Begegnungen,
    /// Brettpartien (ohne Züge, die Quelle hat kein PGN), Meldelisten; FIDE-IDs aus der Region Bayern nachgefüllt; danach die
    /// Ansichten. <c>?dryRun=true</c> liest und zählt nur. 400 <c>invalidLeague</c> / <c>unsupportedLeague</c> (Senioren,
    /// Jugend, Pokal, Verbandsliga), 404 <c>notFound</c>, 409 <c>conflict</c>, 503 <c>unreachable</c>.
    /// </summary>
    /// <summary>
    /// Meldungen Dritter aus dem Online-Bereich des Schachkreises Zugspitze / Bezirks Oberbayern (0.716.0) für EINE Saison
    /// (<c>season</c> = Jahr + Quartal: 20204, 20211, 20212, 20213, 20221): Turnierliste → je Turnier Ergebnisseite + Lichess-Ergebnisse,
    /// Zuordnung über Wertung + Punkte, dann Name + Verein gegen die bayerischen Meldelisten → Selbstmeldungen der Quelle
    /// „Online-Schach Oberbayern {season}" (Reporter „Schachkreis Zugspitze") + je neuer Meldung ein Vorschlag. <c>dryRun</c>: nur zählen,
    /// mit Liste. 400 <c>invalidSeason</c>, 404 <c>notFound</c>, 503 <c>rateLimited</c>/<c>unreachable</c>.
    /// </summary>
    [HttpPost("admin/online-reports/zugspitze")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> ImportZugspitzeOnlineReports([FromQuery] string? season, [FromQuery] bool dryRun,
        [FromServices] ZugspitzeOnlineReports reports, CancellationToken ct)
    {
        if (!Services.League.ZugspitzeOnlineReports.ValidSeason(season)) return BadRequest(new { reason = "invalidSeason" });
        try
        {
            return Ok(await reports.ImportAsync(season!, dryRun, ct));
        }
        catch (Services.League.ZugspitzeOnlineReports.NotFoundException e) { return NotFound(new { reason = "notFound", message = e.Message }); }
        catch (LeagueOnlineSync.RateLimitedException) { return StatusCode(503, new { reason = "rateLimited" }); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return StatusCode(503, new { reason = "unreachable" });
        }
    }

    [HttpPost("admin/zugspitze/import")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> ZugspitzeImport([FromBody] ZugspitzeImportRequest? req, [FromQuery] bool dryRun,
        [FromServices] ZugspitzeSource zugspitze, CancellationToken ct)
    {
        var lref = !string.IsNullOrWhiteSpace(req?.Url) ? ZugspitzeSource.LeagueRef.Parse(req.Url)
            : ZugspitzeSource.LeagueRef.Of(req?.LigaId, req?.Season);
        if (lref is null) return BadRequest(new { reason = "invalidLeague" });
        try
        {
            return Ok(await zugspitze.ImportAsync(lref, dryRun, ct, req?.Boards));
        }
        catch (ZugspitzeSource.NotFoundException e) { return NotFound(new { reason = "notFound", message = e.Message }); }
        catch (ZugspitzeSource.UnsupportedException e) { return BadRequest(new { reason = "unsupportedLeague", message = e.Message }); }
        catch (ZugspitzeSource.ConflictException e) { return Conflict(new { reason = "conflict", message = e.Message }); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return StatusCode(503, new { reason = "unreachable" });
        }
    }
}

/// <summary>Öffentliche Ansicht eines Teilen-Links: genau EINE Begegnung, ohne Anmeldung, Token = Geheimnis.</summary>
[ApiController]
[Route("api/league/s")]
[AllowAnonymous]
[EnableRateLimiting("anonymous-tournament")]
public class LeagueShareController : ControllerBase
{
    private readonly LeagueService _league;
    public LeagueShareController(LeagueService league) => _league = league;

    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token, CancellationToken ct) =>
        await _league.PublicShareAsync(token, ct) is { } v ? Ok(v) : NotFound();

    /// <summary>Partien im Bestand je Quelle wie auf der Startseite (0.627.0, Wunsch: „die Info auch auf den Link hin") — nur Zahlen,
    /// kein Konto, kein Name; derselbe 30-min-Speicher. Seit 0.628.0 mit <c>league</c> (die Liga des Links) und <c>opponent</c> (die
    /// Meldeliste des Gegners der GETEILTEN Begegnung) — beides vom Server bestimmt, nicht frei wählbar; online nur gesicherte Konten
    /// wie auf der Karte des Links.</summary>
    [HttpGet("{token}/sources")]
    public async Task<IActionResult> Sources(string token, [FromServices] LeagueGameSources sources, CancellationToken ct)
    {
        if (await _league.PublicShareAsync(token, ct) is not { } share || await _league.ShareTnrAsync(token, ct) is not { } tnr
            || await _league.ShareContextAsync(token, ct) is not { } link) return NotFound();
        var fides = (share["fixture"]?["roster"] as JsonArray ?? []).Select(r => (string?)r?["fide"]);
        return Ok(await sources.GetAsync(link.Club.Id, ct, fides, onlySure: true, leagueTnr: tnr));
    }

    /// <summary>Brettpaarungen der GETEILTEN Begegnung samt Partie (0.673.0) — Liga, Runde und Mannschaft bestimmt der Link,
    /// die Vereinspartien kommen aus der Vereins-Datenbank SEINES Vereins.</summary>
    [HttpGet("{token}/games")]
    public async Task<IActionResult> FixtureGames(string token, [FromServices] LeagueFixtureGames games, CancellationToken ct) =>
        await _league.ShareFixtureAsync(token, ct) is { } s && await _league.ShareContextAsync(token, ct) is { } link
            ? Ok(await games.ForFixtureAsync(link.Club, s.Tnr, s.Round, s.Team, ct)) : NotFound();

    /// <summary>Dieselbe Treffer-Statistik über den Teilen-Link (0.650.0) — nur Zahlen und Liga-Namen der Quelle seines Vereins.</summary>
    [HttpGet("{token}/forecast-stats")]
    public async Task<IActionResult> ForecastStats(string token, CancellationToken ct) =>
        await _league.ShareContextAsync(token, ct) is { } link ? Ok(await _league.ForecastStatsAsync(link.Club.Region, ct)) : NotFound();

    [HttpGet("{token}/player/{fide}")]
    public async Task<IActionResult> Player(string token, string fide, CancellationToken ct)
    {
        if (!await _league.ShareCoversAsync(token, fide, ct)) return NotFound();
        return await _league.CardAsync(fide, onlySure: true, ct) is { } c ? Ok(c) : NotFound();
    }

    public sealed record ShareAccountRequest(string? Site, string? User, string? Comment);

    /// <summary>
    /// Online-Konto für einen Spieler der geteilten Begegnung eintragen — OHNE Anmeldung (0.630.0): sofort „gesichert", vermerkt als
    /// „anonym" (+ Hash des Links) → das Konto (bei einem Minderjährigen verborgen); 404 Spieler gehört nicht zum Link, 400 wie beim
    /// Anlegen und <c>takenElsewhere</c> (steht schon bei einem anderen Spieler).
    /// </summary>
    [HttpPost("{token}/player/{fide}/accounts")]
    public async Task<IActionResult> AddAccount(string token, string fide, [FromBody] ShareAccountRequest? req,
        [FromServices] LeagueOnlineAccountService accounts, CancellationToken ct)
    {
        if (!await _league.ShareCoversAsync(token, fide, ct)) return NotFound();
        var r = await accounts.CreateViaShareAsync(fide, req?.Site, req?.User, req?.Comment, LeagueClubService.ShareHashOf(token), ct);
        return r switch
        {
            ({ } a, _) => Ok(await accounts.JsonAsync(a, ct)),
            (_, "unknownPlayer") => NotFound(new { reason = r.Reason }),
            _ => BadRequest(new { reason = r.Reason }),
        };
    }

    [HttpGet("{token}/player/{fide}/tree")]
    public async Task<IActionResult> Tree(string token, string fide, [FromQuery] string? color, [FromQuery] string? line,
        [FromQuery] string? source, [FromQuery] string? speeds, [FromQuery] int? years, CancellationToken ct)
    {
        if (!await _league.ShareCoversAsync(token, fide, ct)) return NotFound();
        return await _league.TreeAsync(fide, color ?? "w", line, ct, LeagueProfileStore.TreeFilter.Parse(source, speeds, years, onlySure: true))
            is { } t ? Ok(t) : NotFound();
    }

    [HttpGet("{token}/player/{fide}/profile")]
    public async Task<IActionResult> Profile(string token, string fide, [FromQuery] string? source, [FromQuery] string? speeds,
        [FromQuery] int? years, CancellationToken ct)
    {
        if (!await _league.ShareCoversAsync(token, fide, ct)) return NotFound();
        return await _league.ProfileAsync(fide, ct, LeagueProfileStore.TreeFilter.Parse(source, speeds, years, onlySure: true))
            is { } p ? Ok(p) : NotFound();
    }

    [HttpGet("{token}/player/{fide}/recent")]
    public async Task<IActionResult> Recent(string token, string fide, [FromQuery] string? color, CancellationToken ct)
    {
        if (!await _league.ShareCoversAsync(token, fide, ct)) return NotFound();
        return await _league.RecentAsync(fide, ct, color) is { } r ? Ok(r) : NotFound();
    }

    [HttpGet("{token}/player/{fide}/pgn")]
    public async Task<IActionResult> Pgn(string token, string fide, CancellationToken ct)
    {
        if (!await _league.ShareCoversAsync(token, fide, ct)) return NotFound();
        return await _league.PgnAsync(fide, ct) is { } p ? LeagueController.PgnFile(fide, p.Name, p.Pgn) : NotFound();
    }
}
