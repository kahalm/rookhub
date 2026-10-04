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

    public LeagueController(LeagueService league, LeagueImportService import, LeagueUpdateService update)
    {
        _league = league; _import = import; _update = update;
    }

    [HttpGet("index")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Index(CancellationToken ct) => Ok(await _league.IndexAsync(ct));

    /// <summary>Fertig gerechnete Liga (alle Teams, alle Runden) — Feldnamen wie in der Python-Fassung.</summary>
    [HttpGet("{tnr:int}")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> League(int tnr, CancellationToken ct) =>
        await _league.LeagueJsonAsync(tnr, ct) is { } json ? Content(json, "application/json") : NotFound();

    /// <summary>Partien im Bestand je Quelle (0.626.0) → <c>{ board[{ key, label, games }], boardTotal, online[…], onlineTotal, countedAt }</c>;
    /// 30 min im Speicher. Seit 0.628.0: <c>?tnr=</c> fügt <c>league</c> hinzu (alle Meldelisten dieser Liga), <c>?fides=1,2,…</c>
    /// (die Meldeliste des Gegners, höchstens 40) <c>opponent</c>.</summary>
    [HttpGet("sources")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Sources([FromQuery] int? tnr, [FromQuery] string? fides, [FromServices] LeagueGameSources sources,
        CancellationToken ct) =>
        Ok(await sources.GetAsync(ct, fides?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            leagueTnr: tnr));

    /// <summary>Treffer der Prognose in den bisherigen Runden (0.650.0): je Runde, je Liga, gesamt — über alle Begegnungen.</summary>
    [HttpGet("forecast-stats")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> ForecastStats(CancellationToken ct) => Ok(await _league.ForecastStatsAsync(ct));

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

    [HttpPost("share")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> CreateShare([FromBody] ShareRequest req, CancellationToken ct)
    {
        var s = await _league.CreateShareAsync(req.Tnr, req.Round, req.Team, GetUserIdOrNull(), ct);
        return s is null
            ? BadRequest(new { message = "Diese Runde hat keine Prognose zum Teilen." })
            : Ok(new { token = s.Token, expires = s.Expires.ToString("yyyy-MM-dd") });
    }

    [HttpDelete("share/{token}")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> DeleteShare(string token, CancellationToken ct) =>
        await _league.DeleteShareAsync(token, ct) ? NoContent() : NotFound();

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
        if (await _league.PublicShareAsync(token, ct) is not { } share || await _league.ShareTnrAsync(token, ct) is not { } tnr) return NotFound();
        var fides = (share["fixture"]?["roster"] as JsonArray ?? []).Select(r => (string?)r?["fide"]);
        return Ok(await sources.GetAsync(ct, fides, onlySure: true, leagueTnr: tnr));
    }

    /// <summary>Dieselbe Treffer-Statistik über den Teilen-Link (0.650.0) — nur Zahlen und Liga-Namen.</summary>
    [HttpGet("{token}/forecast-stats")]
    public async Task<IActionResult> ForecastStats(string token, CancellationToken ct) =>
        await _league.ShareValidAsync(token, ct) ? Ok(await _league.ForecastStatsAsync(ct)) : NotFound();

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
