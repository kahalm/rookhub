using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/tournaments")]
[Authorize]
public class TournamentProxyController : ControllerBase
{
    private readonly CrawlerProxyService _proxy;

    public TournamentProxyController(CrawlerProxyService proxy) => _proxy = proxy;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var result = await _proxy.GetAsync("/api/tournaments");
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { message = "Crawler service unavailable." });
        }
    }

    [AllowAnonymous]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        try
        {
            var result = await _proxy.GetAsync($"/api/tournaments/{id}");
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { message = "Crawler service unavailable." });
        }
    }

    [AllowAnonymous]
    [HttpGet("{id}/players")]
    public async Task<IActionResult> GetPlayers(string id, [FromQuery] string? team, [FromQuery] string? sortBy)
    {
        try
        {
            var query = $"/api/tournaments/{id}/players";
            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(team)) queryParams.Add($"team={Uri.EscapeDataString(team)}");
            if (!string.IsNullOrEmpty(sortBy)) queryParams.Add($"sortBy={Uri.EscapeDataString(sortBy)}");
            if (queryParams.Count > 0) query += "?" + string.Join("&", queryParams);

            var result = await _proxy.GetAsync(query);
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { message = "Crawler service unavailable." });
        }
    }

    [AllowAnonymous]
    [HttpGet("{id}/teams")]
    public async Task<IActionResult> GetTeams(string id)
    {
        try
        {
            var result = await _proxy.GetAsync($"/api/tournaments/{id}/teams");
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { message = "Crawler service unavailable." });
        }
    }

    [AllowAnonymous]
    [HttpGet("{id}/teams/{snr}")]
    public async Task<IActionResult> GetTeamDetail(string id, int snr)
    {
        try
        {
            var result = await _proxy.GetAsync($"/api/tournaments/{id}/teams/{snr}");
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { message = "Crawler service unavailable." });
        }
    }

    [AllowAnonymous]
    [HttpGet("{id}/pairings")]
    public async Task<IActionResult> GetPairings(string id, [FromQuery] int? round)
    {
        try
        {
            var query = $"/api/tournaments/{id}/pairings";
            if (round.HasValue) query += $"?round={round.Value}";

            var result = await _proxy.GetAsync(query);
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { message = "Crawler service unavailable." });
        }
    }

    [AllowAnonymous]
    [HttpGet("{id}/players/{snr:int}/results")]
    public async Task<IActionResult> GetPlayerResults(string id, int snr)
    {
        try
        {
            var result = await _proxy.GetAsync($"/api/tournaments/{id}/players/{snr}/results");
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { message = "Crawler service unavailable." });
        }
    }

    [HttpGet("{id}/rounds/check")]
    public async Task<IActionResult> CheckRounds(string id)
    {
        try
        {
            var result = await _proxy.GetAsync($"/api/tournaments/{id}/rounds/check");
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { message = "Crawler service unavailable." });
        }
    }

    [HttpPost("crawl")]
    public async Task<IActionResult> Crawl([FromBody] JsonElement body)
    {
        if (body.ValueKind == JsonValueKind.Undefined ||
            !body.TryGetProperty("chessResultsId", out _))
            return BadRequest(new { message = "Request body must contain chessResultsId." });

        try
        {
            var result = await _proxy.PostAsync("/api/crawl", body);
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { message = "Crawler service unavailable." });
        }
    }

    [HttpPost("crawl/player-details")]
    public async Task<IActionResult> CrawlPlayerDetails([FromBody] JsonElement body)
    {
        if (body.ValueKind == JsonValueKind.Undefined ||
            !body.TryGetProperty("chessResultsId", out _) ||
            !body.TryGetProperty("playerSnrs", out _))
            return BadRequest(new { message = "Request body must contain chessResultsId and playerSnrs." });

        try
        {
            var result = await _proxy.PostAsync("/api/crawl/player-details", body);
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { message = "Crawler service unavailable." });
        }
    }

    [HttpGet("crawl/{jobId}")]
    public async Task<IActionResult> GetCrawlStatus(int jobId)
    {
        try
        {
            var result = await _proxy.GetAsync($"/api/crawl/{jobId}");
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { message = "Crawler service unavailable." });
        }
    }

    [AllowAnonymous]
    [HttpGet("crawler/ip")]
    public async Task<IActionResult> GetCrawlerIp()
    {
        try
        {
            var result = await _proxy.GetAsync("/api/health/ip");
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { message = "Crawler service unavailable." });
        }
    }
}
