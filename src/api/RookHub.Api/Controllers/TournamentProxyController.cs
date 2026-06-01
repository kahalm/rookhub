using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Filters;
using RookHub.Api.Services;
using RookHub.Api.Validation;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/tournaments")]
[Authorize]
[TypeFilter(typeof(CrawlerExceptionFilter))]
public class TournamentProxyController : ControllerBase
{
    private readonly CrawlerProxyService _proxy;

    public TournamentProxyController(CrawlerProxyService proxy) => _proxy = proxy;

    private IActionResult? ValidateId(string id)
        => TournamentIdValidator.IsValid(id) ? null : BadRequest(new { message = "Invalid tournament ID." });

    // Client-Abbruch (HttpContext.RequestAborted) an ausgehende Crawler-Calls durchreichen,
    // damit abgebrochene Requests nicht am Crawler weiterlaufen (Ressourcen-/DoS-Schutz).
    private CancellationToken RequestCt => HttpContext?.RequestAborted ?? default;

    // Vom Proxy akzeptierte Crawl-Job-Typen (CrawlJobType im Crawler) — gegen Injektion
    // beliebiger Felder/Werte ueber den durchgereichten Roh-Body.
    private static readonly HashSet<string> _allowedJobTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Full", "PlayersOnly", "PairingsOnly", "CheckNewRounds", "PlayerDetails" };

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;

        var result = await _proxy.GetAsync($"/api/tournaments?page={page}&pageSize={pageSize}", RequestCt);
        return Ok(result);
    }

    [AllowAnonymous]
    [EnableRateLimiting("anonymous-tournament")]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        if (ValidateId(id) is { } err) return err;
        var result = await _proxy.GetAsync($"/api/tournaments/{id}", RequestCt);
        return Ok(result);
    }

    [AllowAnonymous]
    [EnableRateLimiting("anonymous-tournament")]
    [HttpGet("{id}/players")]
    public async Task<IActionResult> GetPlayers(string id, [FromQuery] string? team, [FromQuery] string? sortBy)
    {
        if (ValidateId(id) is { } err) return err;
        var query = $"/api/tournaments/{id}/players";
        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(team)) queryParams.Add($"team={Uri.EscapeDataString(team)}");
        if (!string.IsNullOrEmpty(sortBy)) queryParams.Add($"sortBy={Uri.EscapeDataString(sortBy)}");
        if (queryParams.Count > 0) query += "?" + string.Join("&", queryParams);

        var result = await _proxy.GetAsync(query, RequestCt);
        return Ok(result);
    }

    [AllowAnonymous]
    [EnableRateLimiting("anonymous-tournament")]
    [HttpGet("{id}/teams")]
    public async Task<IActionResult> GetTeams(string id)
    {
        if (ValidateId(id) is { } err) return err;
        var result = await _proxy.GetAsync($"/api/tournaments/{id}/teams", RequestCt);
        return Ok(result);
    }

    [AllowAnonymous]
    [EnableRateLimiting("anonymous-tournament")]
    [HttpGet("{id}/teams/{snr}")]
    public async Task<IActionResult> GetTeamDetail(string id, int snr)
    {
        if (ValidateId(id) is { } err) return err;
        var result = await _proxy.GetAsync($"/api/tournaments/{id}/teams/{snr}", RequestCt);
        return Ok(result);
    }

    [AllowAnonymous]
    [EnableRateLimiting("anonymous-tournament")]
    [HttpGet("{id}/pairings")]
    public async Task<IActionResult> GetPairings(string id, [FromQuery] int? round)
    {
        if (ValidateId(id) is { } err) return err;
        var query = $"/api/tournaments/{id}/pairings";
        if (round.HasValue) query += $"?round={round.Value}";

        var result = await _proxy.GetAsync(query, RequestCt);
        return Ok(result);
    }

    [AllowAnonymous]
    [EnableRateLimiting("anonymous-tournament")]
    [HttpGet("{id}/players/{snr:int}/results")]
    public async Task<IActionResult> GetPlayerResults(string id, int snr)
    {
        if (ValidateId(id) is { } err) return err;
        var result = await _proxy.GetAsync($"/api/tournaments/{id}/players/{snr}/results", RequestCt);
        return Ok(result);
    }

    [HttpGet("{id}/rounds/check")]
    public async Task<IActionResult> CheckRounds(string id)
    {
        if (ValidateId(id) is { } err) return err;
        var result = await _proxy.GetAsync($"/api/tournaments/{id}/rounds/check", RequestCt);
        return Ok(result);
    }

    [HttpPost("crawl")]
    public async Task<IActionResult> Crawl([FromBody] JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object ||
            !body.TryGetProperty("chessResultsId", out var cidProp))
            return BadRequest(new { message = "Request body must contain chessResultsId." });

        var chessResultsId = cidProp.ValueKind == JsonValueKind.String ? cidProp.GetString() : cidProp.ToString();
        if (string.IsNullOrWhiteSpace(chessResultsId))
            return BadRequest(new { message = "chessResultsId must not be empty." });

        // jobType gegen Whitelist pruefen (Default: Full). Nur bekannte Felder weiterreichen
        // statt den Roh-Body durchzuschleusen — so kann kein beliebiges/zukuenftiges Feld injiziert werden.
        var jobType = "Full";
        if (body.TryGetProperty("jobType", out var jtProp))
        {
            var jt = jtProp.ValueKind == JsonValueKind.String ? jtProp.GetString() : null;
            if (string.IsNullOrEmpty(jt) || !_allowedJobTypes.Contains(jt))
                return BadRequest(new { message = "Invalid jobType." });
            jobType = jt;
        }

        var result = await _proxy.PostJsonAsync("/api/crawl", new { chessResultsId, jobType }, RequestCt);
        return Ok(result);
    }

    [HttpPost("crawl/player-details")]
    public async Task<IActionResult> CrawlPlayerDetails([FromBody] JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object ||
            !body.TryGetProperty("chessResultsId", out var cidProp) ||
            !body.TryGetProperty("playerSnrs", out var snrsProp) ||
            snrsProp.ValueKind != JsonValueKind.Array)
            return BadRequest(new { message = "Request body must contain chessResultsId and playerSnrs." });

        var chessResultsId = cidProp.ValueKind == JsonValueKind.String ? cidProp.GetString() : cidProp.ToString();
        if (string.IsNullOrWhiteSpace(chessResultsId))
            return BadRequest(new { message = "chessResultsId must not be empty." });

        var playerSnrs = new List<int>();
        foreach (var el in snrsProp.EnumerateArray())
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var snr))
                playerSnrs.Add(snr);

        var result = await _proxy.PostJsonAsync("/api/crawl/player-details", new { chessResultsId, playerSnrs }, RequestCt);
        return Ok(result);
    }

    [HttpGet("crawl/{jobId}")]
    public async Task<IActionResult> GetCrawlStatus(int jobId)
    {
        var result = await _proxy.GetAsync($"/api/crawl/{jobId}", RequestCt);
        return Ok(result);
    }

    [HttpGet("crawler/ip")]
    public async Task<IActionResult> GetCrawlerIp()
    {
        var result = await _proxy.GetAsync("/api/health/ip", RequestCt);
        return Ok(result);
    }
}
