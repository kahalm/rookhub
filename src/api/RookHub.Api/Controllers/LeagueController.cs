using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
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

    [HttpGet("player/{fide}")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Player(string fide, CancellationToken ct) =>
        await _league.CardAsync(fide, onlySure: false, ct) is { } c ? Ok(c) : NotFound();

    [HttpGet("player/{fide}/pgn")]
    [HasPermission(Permissions.LeagueView)]
    public async Task<IActionResult> Pgn(string fide, CancellationToken ct) =>
        await _league.PgnAsync(fide, ct) is { } p ? PgnFile(fide, p.Name, p.Pgn) : NotFound();

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
            ? BadRequest(new { error = "Diese Runde hat keine Prognose zum Teilen." })
            : Ok(new { token = s.Token, expires = s.Expires.ToString("yyyy-MM-dd") });
    }

    [HttpGet("share")]
    [HasPermission(Permissions.LeagueManage)]
    public async Task<IActionResult> FindShare([FromQuery] int tnr, [FromQuery] int round, [FromQuery] string team, CancellationToken ct)
    {
        var s = await _league.FindShareAsync(tnr, round, team, ct);
        return Ok(new { token = s?.Token, expires = s?.Expires.ToString("yyyy-MM-dd") });
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

    /// <summary>Bestand übernehmen (JSON, gern gzip-komprimiert mit <c>Content-Encoding: gzip</c>).</summary>
    [HttpPost("admin/import")]
    [HasPermission(Permissions.LeagueManage)]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<IActionResult> Import([FromQuery] bool rebuild, CancellationToken ct)
    {
        Stream body = Request.Body;
        if (Request.Headers.ContentEncoding.ToString().Contains("gzip", StringComparison.OrdinalIgnoreCase))
            body = new GZipStream(Request.Body, CompressionMode.Decompress);
        LeagueImportService.Bundle? bundle;
        try { bundle = await JsonSerializer.DeserializeAsync<LeagueImportService.Bundle>(body, LeagueImportService.Json, ct); }
        catch (JsonException ex) { return BadRequest(new { error = ex.Message }); }
        if (bundle is null) return BadRequest(new { error = "leer" });
        var res = await _import.ImportAsync(bundle, ct);
        if (rebuild) res["views"] = await _league.RebuildViewsAsync(ct);
        return Ok(res);
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

    [HttpGet("{token}/player/{fide}")]
    public async Task<IActionResult> Player(string token, string fide, CancellationToken ct)
    {
        if (!await _league.ShareCoversAsync(token, fide, ct)) return NotFound();
        return await _league.CardAsync(fide, onlySure: true, ct) is { } c ? Ok(c) : NotFound();
    }

    [HttpGet("{token}/player/{fide}/pgn")]
    public async Task<IActionResult> Pgn(string token, string fide, CancellationToken ct)
    {
        if (!await _league.ShareCoversAsync(token, fide, ct)) return NotFound();
        return await _league.PgnAsync(fide, ct) is { } p ? LeagueController.PgnFile(fide, p.Name, p.Pgn) : NotFound();
    }
}
