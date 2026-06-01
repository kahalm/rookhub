using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Controllers;
using RookHub.Api.Exceptions;
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

    private void SetupCrawlerError(HttpStatusCode status, string body = "{\"error\":\"test\"}")
    {
        _handler.ResponseMessage = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
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
    public async Task GetAll_PassesPaginationParams()
    {
        SetupResponse("[]");

        await _controller.GetAll(page: 2, pageSize: 25);

        Assert.Contains("page=2", _handler.LastRequestUri!);
        Assert.Contains("pageSize=25", _handler.LastRequestUri!);
    }

    [Fact]
    public async Task GetAll_ClampsPageSize()
    {
        SetupResponse("[]");

        await _controller.GetAll(page: 0, pageSize: 999);

        Assert.Contains("page=1", _handler.LastRequestUri!);
        Assert.Contains("pageSize=200", _handler.LastRequestUri!);
    }

    [Fact]
    public async Task GetAll_ThrowsHttpRequestException_WhenCrawlerUnavailable()
    {
        SetupFailure();

        // Without the CrawlerExceptionFilter, the exception propagates
        await Assert.ThrowsAsync<HttpRequestException>(() => _controller.GetAll());
    }

    [Fact]
    public async Task GetAll_ThrowsCrawlerRequestException_On4xx()
    {
        SetupCrawlerError(HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<CrawlerRequestException>(() => _controller.GetAll());
    }

    // ---- ID Validation ----

    [Theory]
    [InlineData("../admin")]
    [InlineData("../../etc/passwd")]
    [InlineData("123/../../other")]
    [InlineData("")]
    [InlineData("a-b-c")]
    [InlineData("id with spaces")]
    [InlineData("123456789012345678901")] // 21 chars
    public async Task GetById_ReturnsBadRequest_ForInvalidId(string id)
    {
        var result = await _controller.GetById(id) as BadRequestObjectResult;

        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("abc123")]
    [InlineData("12345678901234567890")] // 20 chars = max
    public async Task GetById_AcceptsValidId(string id)
    {
        SetupResponse("{\"id\":1,\"name\":\"Test\"}");

        var result = await _controller.GetById(id) as OkObjectResult;

        Assert.NotNull(result);
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
    public async Task GetById_ThrowsHttpRequestException_WhenCrawlerUnavailable()
    {
        SetupFailure();

        await Assert.ThrowsAsync<HttpRequestException>(() => _controller.GetById("123"));
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
    public async Task GetPlayers_ThrowsHttpRequestException_WhenCrawlerUnavailable()
    {
        SetupFailure();

        await Assert.ThrowsAsync<HttpRequestException>(() => _controller.GetPlayers("123", null, null));
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
    public async Task GetTeams_ThrowsHttpRequestException_WhenCrawlerUnavailable()
    {
        SetupFailure();

        await Assert.ThrowsAsync<HttpRequestException>(() => _controller.GetTeams("123"));
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
    public async Task GetTeamDetail_ThrowsHttpRequestException_WhenCrawlerUnavailable()
    {
        SetupFailure();

        await Assert.ThrowsAsync<HttpRequestException>(() => _controller.GetTeamDetail("123", 1));
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
    public async Task GetPairings_ThrowsHttpRequestException_WhenCrawlerUnavailable()
    {
        SetupFailure();

        await Assert.ThrowsAsync<HttpRequestException>(() => _controller.GetPairings("123", null));
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
    public async Task GetPlayerResults_ThrowsHttpRequestException_WhenCrawlerUnavailable()
    {
        SetupFailure();

        await Assert.ThrowsAsync<HttpRequestException>(() => _controller.GetPlayerResults("123", 5));
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
    public async Task CheckRounds_ThrowsHttpRequestException_WhenCrawlerUnavailable()
    {
        SetupFailure();

        await Assert.ThrowsAsync<HttpRequestException>(() => _controller.CheckRounds("123"));
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
    public async Task Crawl_ThrowsHttpRequestException_WhenCrawlerUnavailable()
    {
        SetupFailure();
        var body = JsonSerializer.Deserialize<JsonElement>("{\"chessResultsId\":\"100\"}");

        await Assert.ThrowsAsync<HttpRequestException>(() => _controller.Crawl(body));
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
    public async Task CrawlPlayerDetails_ThrowsHttpRequestException_WhenCrawlerUnavailable()
    {
        SetupFailure();
        var body = JsonSerializer.Deserialize<JsonElement>("{\"chessResultsId\":\"100\",\"playerSnrs\":[1]}");

        await Assert.ThrowsAsync<HttpRequestException>(() => _controller.CrawlPlayerDetails(body));
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
    public async Task GetCrawlStatus_ThrowsHttpRequestException_WhenCrawlerUnavailable()
    {
        SetupFailure();

        await Assert.ThrowsAsync<HttpRequestException>(() => _controller.GetCrawlStatus(1));
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
    public async Task GetCrawlerIp_ThrowsHttpRequestException_WhenCrawlerUnavailable()
    {
        SetupFailure();

        await Assert.ThrowsAsync<HttpRequestException>(() => _controller.GetCrawlerIp());
    }

    // ---- Auth/Rate-Limit-Attribute (Code-Audit Finding #5) ----

    private static MethodInfo M(string name) =>
        typeof(TournamentProxyController).GetMethod(name)
        ?? throw new InvalidOperationException($"Methode {name} nicht gefunden");

    [Theory]
    [InlineData(nameof(TournamentProxyController.GetById))]
    [InlineData(nameof(TournamentProxyController.GetPlayers))]
    [InlineData(nameof(TournamentProxyController.GetTeams))]
    [InlineData(nameof(TournamentProxyController.GetTeamDetail))]
    [InlineData(nameof(TournamentProxyController.GetPairings))]
    [InlineData(nameof(TournamentProxyController.GetPlayerResults))]
    public void AnonymousTournamentGets_StayPublicButRateLimited(string method)
    {
        var m = M(method);
        // Bleibt oeffentlich (oeffentliche Turnierseite / Teilen-Feature) ...
        Assert.NotNull(m.GetCustomAttribute<AllowAnonymousAttribute>());
        // ... aber gedrosselt gegen DoS auf den Crawler.
        var rl = m.GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.NotNull(rl);
        Assert.Equal("anonymous-tournament", rl!.PolicyName);
    }

    [Fact]
    public void GetAll_RequiresAuth_NotAnonymous()
    {
        Assert.Null(M(nameof(TournamentProxyController.GetAll))
            .GetCustomAttribute<AllowAnonymousAttribute>());
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
