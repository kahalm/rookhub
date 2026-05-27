using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Controllers;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class TournamentProxyControllerTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly CrawlerProxyService _proxy;
    private readonly TournamentProxyController _controller;

    public TournamentProxyControllerTests()
    {
        _handler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://crawler") };
        _proxy = new CrawlerProxyService(_httpClient);
        _controller = new TournamentProxyController(_proxy);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _handler.Dispose();
    }

    private void SetupResponse(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _handler.ResponseMessage = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private void SetupFailure()
    {
        _handler.ThrowOnSend = true;
    }

    // ---- GetAll ----

    [Fact]
    public async Task GetAll_ReturnsOk_WithCrawlerData()
    {
        SetupResponse("[{\"id\":1,\"name\":\"Test Tournament\"}]");

        var result = await _controller.GetAll() as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public async Task GetAll_Returns502_WhenCrawlerUnavailable()
    {
        SetupFailure();

        var result = await _controller.GetAll() as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(502, result.StatusCode);
    }

    // ---- GetById ----

    [Fact]
    public async Task GetById_ReturnsOk_WithData()
    {
        SetupResponse("{\"id\":1,\"name\":\"Test\"}");

        var result = await _controller.GetById("123") as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public async Task GetById_Returns502_WhenCrawlerUnavailable()
    {
        SetupFailure();

        var result = await _controller.GetById("123") as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(502, result.StatusCode);
    }

    // ---- GetPlayers ----

    [Fact]
    public async Task GetPlayers_ReturnsOk()
    {
        SetupResponse("[{\"name\":\"Player1\"}]");

        var result = await _controller.GetPlayers("123", null, null) as OkObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetPlayers_PassesQueryParams()
    {
        SetupResponse("[]");

        await _controller.GetPlayers("123", "TeamA", "rating");

        Assert.Contains("/api/tournaments/123/players?", _handler.LastRequestUri!);
        Assert.Contains("team=TeamA", _handler.LastRequestUri!);
        Assert.Contains("sortBy=rating", _handler.LastRequestUri!);
    }

    [Fact]
    public async Task GetPlayers_Returns502_WhenCrawlerUnavailable()
    {
        SetupFailure();

        var result = await _controller.GetPlayers("123", null, null) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(502, result.StatusCode);
    }

    // ---- GetTeams ----

    [Fact]
    public async Task GetTeams_ReturnsOk()
    {
        SetupResponse("[{\"name\":\"Team1\"}]");

        var result = await _controller.GetTeams("123") as OkObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetTeams_Returns502_WhenCrawlerUnavailable()
    {
        SetupFailure();

        var result = await _controller.GetTeams("123") as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(502, result.StatusCode);
    }

    // ---- GetTeamDetail ----

    [Fact]
    public async Task GetTeamDetail_ReturnsOk()
    {
        SetupResponse("{\"snr\":1,\"name\":\"Team1\"}");

        var result = await _controller.GetTeamDetail("123", 1) as OkObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetTeamDetail_Returns502_WhenCrawlerUnavailable()
    {
        SetupFailure();

        var result = await _controller.GetTeamDetail("123", 1) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(502, result.StatusCode);
    }

    // ---- GetPairings ----

    [Fact]
    public async Task GetPairings_ReturnsOk()
    {
        SetupResponse("[]");

        var result = await _controller.GetPairings("123", null) as OkObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetPairings_PassesRoundParam()
    {
        SetupResponse("[]");

        await _controller.GetPairings("123", 3);

        Assert.Contains("round=3", _handler.LastRequestUri!);
    }

    [Fact]
    public async Task GetPairings_Returns502_WhenCrawlerUnavailable()
    {
        SetupFailure();

        var result = await _controller.GetPairings("123", null) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(502, result.StatusCode);
    }

    // ---- GetPlayerResults ----

    [Fact]
    public async Task GetPlayerResults_ReturnsOk()
    {
        SetupResponse("[]");

        var result = await _controller.GetPlayerResults("123", 5) as OkObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetPlayerResults_Returns502_WhenCrawlerUnavailable()
    {
        SetupFailure();

        var result = await _controller.GetPlayerResults("123", 5) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(502, result.StatusCode);
    }

    // ---- CheckRounds ----

    [Fact]
    public async Task CheckRounds_ReturnsOk()
    {
        SetupResponse("{\"hasNewRound\":false}");

        var result = await _controller.CheckRounds("123") as OkObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task CheckRounds_Returns502_WhenCrawlerUnavailable()
    {
        SetupFailure();

        var result = await _controller.CheckRounds("123") as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(502, result.StatusCode);
    }

    // ---- Crawl ----

    [Fact]
    public async Task Crawl_ReturnsOk_WithValidBody()
    {
        SetupResponse("{\"jobId\":1}");
        var body = JsonSerializer.Deserialize<JsonElement>("{\"chessResultsId\":\"100\"}");

        var result = await _controller.Crawl(body) as OkObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Crawl_ReturnsBadRequest_WhenMissingChessResultsId()
    {
        var body = JsonSerializer.Deserialize<JsonElement>("{\"foo\":\"bar\"}");

        var result = await _controller.Crawl(body) as BadRequestObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Crawl_Returns502_WhenCrawlerUnavailable()
    {
        SetupFailure();
        var body = JsonSerializer.Deserialize<JsonElement>("{\"chessResultsId\":\"100\"}");

        var result = await _controller.Crawl(body) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(502, result.StatusCode);
    }

    // ---- CrawlPlayerDetails ----

    [Fact]
    public async Task CrawlPlayerDetails_ReturnsOk_WithValidBody()
    {
        SetupResponse("{\"jobId\":2}");
        var body = JsonSerializer.Deserialize<JsonElement>("{\"chessResultsId\":\"100\",\"playerSnrs\":[1,2]}");

        var result = await _controller.CrawlPlayerDetails(body) as OkObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task CrawlPlayerDetails_ReturnsBadRequest_WhenMissingPlayerSnrs()
    {
        var body = JsonSerializer.Deserialize<JsonElement>("{\"chessResultsId\":\"100\"}");

        var result = await _controller.CrawlPlayerDetails(body) as BadRequestObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task CrawlPlayerDetails_ReturnsBadRequest_WhenMissingChessResultsId()
    {
        var body = JsonSerializer.Deserialize<JsonElement>("{\"playerSnrs\":[1]}");

        var result = await _controller.CrawlPlayerDetails(body) as BadRequestObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task CrawlPlayerDetails_Returns502_WhenCrawlerUnavailable()
    {
        SetupFailure();
        var body = JsonSerializer.Deserialize<JsonElement>("{\"chessResultsId\":\"100\",\"playerSnrs\":[1]}");

        var result = await _controller.CrawlPlayerDetails(body) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(502, result.StatusCode);
    }

    // ---- GetCrawlStatus ----

    [Fact]
    public async Task GetCrawlStatus_ReturnsOk()
    {
        SetupResponse("{\"status\":\"completed\"}");

        var result = await _controller.GetCrawlStatus(1) as OkObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetCrawlStatus_Returns502_WhenCrawlerUnavailable()
    {
        SetupFailure();

        var result = await _controller.GetCrawlStatus(1) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(502, result.StatusCode);
    }

    // ---- GetCrawlerIp ----

    [Fact]
    public async Task GetCrawlerIp_ReturnsOk()
    {
        SetupResponse("{\"ip\":\"1.2.3.4\"}");

        var result = await _controller.GetCrawlerIp() as OkObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetCrawlerIp_Returns502_WhenCrawlerUnavailable()
    {
        SetupFailure();

        var result = await _controller.GetCrawlerIp() as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(502, result.StatusCode);
    }
}

/// <summary>
/// Mock HTTP handler for testing CrawlerProxyService without a real HTTP server.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    public HttpResponseMessage? ResponseMessage { get; set; }
    public bool ThrowOnSend { get; set; }
    public string? LastRequestUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri?.ToString();

        if (ThrowOnSend)
            throw new HttpRequestException("Simulated connection failure");

        return Task.FromResult(ResponseMessage ?? new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
    }
}
