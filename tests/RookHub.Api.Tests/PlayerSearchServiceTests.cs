using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class PlayerSearchServiceTests
{
    private static CrawlerProxyService CreateCrawlerProxy(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        return new CrawlerProxyService(client);
    }

    private static IHttpClientFactory CreateFideClientFactory(HttpMessageHandler handler)
    {
        var factory = new MockHttpClientFactory("FideSearch",
            new HttpClient(handler) { BaseAddress = new Uri("https://api.chesstools.org") });
        return factory;
    }

    [Fact]
    public async Task SearchAsync_ReturnsChessResultsAndFideResults()
    {
        var crResponse = JsonSerializer.Serialize(new[]
        {
            new { name = "Huber, Johann", fideId = "123", chessResultsId = "456", elo = 2400, country = "AUT", title = "GM" }
        });
        var fideResponse = JsonSerializer.Serialize(new[]
        {
            new { name = "Huber, Johann", fideid = 123, rating = 2400, country = "AUT", title = "GM" }
        });

        var crHandler = new MockHttpMessageHandler(crResponse);
        var fideHandler = new MockHttpMessageHandler(fideResponse);

        var service = new PlayerSearchService(
            CreateCrawlerProxy(crHandler),
            CreateFideClientFactory(fideHandler),
            new LoggerFactory().CreateLogger<PlayerSearchService>());

        var result = await service.SearchAsync("Huber", "Johann");

        Assert.Single(result.ChessResultsResults);
        Assert.Equal("Huber, Johann", result.ChessResultsResults[0].Name);
        Assert.Equal("123", result.ChessResultsResults[0].FideId);
        Assert.Equal("456", result.ChessResultsResults[0].ChessResultsId);

        Assert.Single(result.FideResults);
        Assert.Equal("Huber, Johann", result.FideResults[0].Name);
        Assert.Equal("123", result.FideResults[0].FideId);
    }

    [Fact]
    public async Task SearchAsync_ChessResultsUnavailable_ReturnsFideOnly()
    {
        var fideResponse = JsonSerializer.Serialize(new[]
        {
            new { name = "Huber, Johann", fideid = 123, rating = 2400, country = "AUT", title = "GM" }
        });

        var crHandler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        var fideHandler = new MockHttpMessageHandler(fideResponse);

        var service = new PlayerSearchService(
            CreateCrawlerProxy(crHandler),
            CreateFideClientFactory(fideHandler),
            new LoggerFactory().CreateLogger<PlayerSearchService>());

        var result = await service.SearchAsync("Huber", "Johann");

        Assert.Empty(result.ChessResultsResults);
        Assert.Single(result.FideResults);
    }

    [Fact]
    public async Task SearchAsync_ExactMatch_FiltersOutOtherResults()
    {
        var crResponse = JsonSerializer.Serialize(new[]
        {
            new { name = "Oberschmid, Patrik", fideId = "111", chessResultsId = "999", elo = 2000, country = "AUT", title = (string?)null },
            new { name = "Oberschmid, Patrick", fideId = "222", chessResultsId = "888", elo = 1900, country = "GER", title = (string?)null },
            new { name = "Obermaier, Patrik", fideId = "333", chessResultsId = "777", elo = 1800, country = "GER", title = (string?)null },
        });
        var fideResponse = JsonSerializer.Serialize(new[]
        {
            new { name = "Oberschmid, Patrik", fideid = 111, rating = 2000, country = "AUT", title = (string?)null },
            new { name = "Obermann, Patrik", fideid = 444, rating = 1700, country = "GER", title = (string?)null },
        });

        var service = new PlayerSearchService(
            CreateCrawlerProxy(new MockHttpMessageHandler(crResponse)),
            CreateFideClientFactory(new MockHttpMessageHandler(fideResponse)),
            new LoggerFactory().CreateLogger<PlayerSearchService>());

        var result = await service.SearchAsync("Oberschmid", "Patrik");

        // Only exact match "Oberschmid, Patrik" should remain
        Assert.Single(result.ChessResultsResults);
        Assert.Equal("Oberschmid, Patrik", result.ChessResultsResults[0].Name);

        Assert.Single(result.FideResults);
        Assert.Equal("Oberschmid, Patrik", result.FideResults[0].Name);
    }

    [Fact]
    public async Task SearchAsync_NoExactMatch_ReturnsAllResults()
    {
        var crResponse = JsonSerializer.Serialize(new[]
        {
            new { name = "Huber, Johannes", fideId = "111", chessResultsId = "999", elo = 2000, country = "AUT", title = (string?)null },
            new { name = "Huber, Johan", fideId = "222", chessResultsId = "888", elo = 1900, country = "GER", title = (string?)null },
        });
        var fideResponse = JsonSerializer.Serialize(Array.Empty<object>());

        var service = new PlayerSearchService(
            CreateCrawlerProxy(new MockHttpMessageHandler(crResponse)),
            CreateFideClientFactory(new MockHttpMessageHandler(fideResponse)),
            new LoggerFactory().CreateLogger<PlayerSearchService>());

        var result = await service.SearchAsync("Huber", "Johann");

        // No exact match for "Huber, Johann" -> return all
        Assert.Equal(2, result.ChessResultsResults.Count);
    }

    [Fact]
    public async Task SearchAsync_FideUnavailable_ReturnsChessResultsOnly()
    {
        var crResponse = JsonSerializer.Serialize(new[]
        {
            new { name = "Huber, Johann", fideId = "123", chessResultsId = "456", elo = 2400, country = "AUT", title = "GM" }
        });

        var crHandler = new MockHttpMessageHandler(crResponse);
        var fideHandler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);

        var service = new PlayerSearchService(
            CreateCrawlerProxy(crHandler),
            CreateFideClientFactory(fideHandler),
            new LoggerFactory().CreateLogger<PlayerSearchService>());

        var result = await service.SearchAsync("Huber", "Johann");

        Assert.Single(result.ChessResultsResults);
        Assert.Empty(result.FideResults);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string? _response;
        private readonly HttpStatusCode _statusCode;

        public MockHttpMessageHandler(string response)
        {
            _response = response;
            _statusCode = HttpStatusCode.OK;
        }

        public MockHttpMessageHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode);
            if (_response != null)
                response.Content = new StringContent(_response, System.Text.Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        }
    }

    private class MockHttpClientFactory : IHttpClientFactory
    {
        private readonly string _name;
        private readonly HttpClient _client;

        public MockHttpClientFactory(string name, HttpClient client)
        {
            _name = name;
            _client = client;
        }

        public HttpClient CreateClient(string name)
        {
            if (name == _name) return _client;
            return new HttpClient();
        }
    }
}
