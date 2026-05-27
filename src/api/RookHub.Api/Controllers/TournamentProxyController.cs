using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;

        var result = await _proxy.GetAsync($"/api/tournaments?page={page}&pageSize={pageSize}");
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        if (ValidateId(id) is { } err) return err;
        var result = await _proxy.GetAsync($"/api/tournaments/{id}");
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{id}/players")]
    public async Task<IActionResult> GetPlayers(string id, [FromQuery] string? team, [FromQuery] string? sortBy)
    {
        if (ValidateId(id) is { } err) return err;
        var query = $"/api/tournaments/{id}/players";
        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(team)) queryParams.Add($"team={Uri.EscapeDataString(team)}");
        if (!string.IsNullOrEmpty(sortBy)) queryParams.Add($"sortBy={Uri.EscapeDataString(sortBy)}");
        if (queryParams.Count > 0) query += "?" + string.Join("&", queryParams);

        var result = await _proxy.GetAsync(query);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{id}/teams")]
    public async Task<IActionResult> GetTeams(string id)
    {
        if (ValidateId(id) is { } err) return err;
        var result = await _proxy.GetAsync($"/api/tournaments/{id}/teams");
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{id}/teams/{snr}")]
    public async Task<IActionResult> GetTeamDetail(string id, int snr)
    {
        if (ValidateId(id) is { } err) return err;
        var result = await _proxy.GetAsync($"/api/tournaments/{id}/teams/{snr}");
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{id}/pairings")]
    public async Task<IActionResult> GetPairings(string id, [FromQuery] int? round)
    {
        if (ValidateId(id) is { } err) return err;
        var query = $"/api/tournaments/{id}/pairings";
        if (round.HasValue) query += $"?round={round.Value}";

        var result = await _proxy.GetAsync(query);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{id}/players/{snr:int}/results")]
    public async Task<IActionResult> GetPlayerResults(string id, int snr)
    {
        if (ValidateId(id) is { } err) return err;
        var result = await _proxy.GetAsync($"/api/tournaments/{id}/players/{snr}/results");
        return Ok(result);
    }

    [HttpGet("{id}/rounds/check")]
    public async Task<IActionResult> CheckRounds(string id)
    {
        if (ValidateId(id) is { } err) return err;
        var result = await _proxy.GetAsync($"/api/tournaments/{id}/rounds/check");
        return Ok(result);
    }

    [HttpPost("crawl")]
    public async Task<IActionResult> Crawl([FromBody] JsonElement body)
    {
        if (body.ValueKind == JsonValueKind.Undefined ||
            !body.TryGetProperty("chessResultsId", out _))
            return BadRequest(new { message = "Request body must contain chessResultsId." });

        var result = await _proxy.PostAsync("/api/crawl", body);
        return Ok(result);
    }

    [HttpPost("crawl/player-details")]
    public async Task<IActionResult> CrawlPlayerDetails([FromBody] JsonElement body)
    {
        if (body.ValueKind == JsonValueKind.Undefined ||
            !body.TryGetProperty("chessResultsId", out _) ||
            !body.TryGetProperty("playerSnrs", out _))
            return BadRequest(new { message = "Request body must contain chessResultsId and playerSnrs." });

        var result = await _proxy.PostAsync("/api/crawl/player-details", body);
        return Ok(result);
    }

    [HttpGet("crawl/{jobId}")]
    public async Task<IActionResult> GetCrawlStatus(int jobId)
    {
        var result = await _proxy.GetAsync($"/api/crawl/{jobId}");
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("crawler/ip")]
    public async Task<IActionResult> GetCrawlerIp()
    {
        var result = await _proxy.GetAsync("/api/health/ip");
        return Ok(result);
    }
}
