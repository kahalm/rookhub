using System.Net;
using System.Text;
using System.Text.Json;
using RookHub.Api.Exceptions;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class CrawlerProxyServiceTests
{
    private static CrawlerProxyService CreateProxy(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new MockHandler(responseJson, statusCode);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        return new CrawlerProxyService(httpClient);
    }

    [Fact]
    public async Task GetAsync_200_ReturnsJsonElement()
    {
        var proxy = CreateProxy("""{"id": 1}""");

        var result = await proxy.GetAsync("/api/test");

        Assert.Equal(1, result.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task GetAsync_400_ThrowsCrawlerRequestException()
    {
        var proxy = CreateProxy("""{"error":"bad request"}""", HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<CrawlerRequestException>(() => proxy.GetAsync("/api/test"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("bad request", ex.ResponseBody);
    }

    [Fact]
    public async Task GetAsync_404_ThrowsCrawlerRequestException()
    {
        var proxy = CreateProxy("""{"error":"not found"}""", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<CrawlerRequestException>(() => proxy.GetAsync("/api/test"));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetAsync_500_ThrowsCrawlerRequestException()
    {
        var proxy = CreateProxy("""{"error":"server error"}""", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<CrawlerRequestException>(() => proxy.GetAsync("/api/test"));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    [Fact]
    public async Task PostAsync_200_ReturnsJsonElement()
    {
        var proxy = CreateProxy("""{"status":"ok"}""");
        var body = JsonSerializer.Deserialize<JsonElement>("""{"key":"value"}""");

        var result = await proxy.PostAsync("/api/test", body);

        Assert.Equal("ok", result.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PostAsync_400_ThrowsCrawlerRequestException()
    {
        var proxy = CreateProxy("""{"error":"invalid"}""", HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<CrawlerRequestException>(() => proxy.PostAsync("/api/test"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task PostJsonAsync_200_ReturnsJsonElement()
    {
        var proxy = CreateProxy("""{"result":"created"}""");

        var result = await proxy.PostJsonAsync("/api/test", new { name = "test" });

        Assert.Equal("created", result.GetProperty("result").GetString());
    }

    [Fact]
    public async Task PostJsonAsync_500_ThrowsCrawlerRequestException()
    {
        var proxy = CreateProxy("""{"error":"fail"}""", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<CrawlerRequestException>(() =>
            proxy.PostJsonAsync("/api/test", new { name = "test" }));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    private class MockHandler : HttpMessageHandler
    {
        private readonly string _response;
        private readonly HttpStatusCode _statusCode;

        public MockHandler(string response, HttpStatusCode statusCode)
        {
            _response = response;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            });
        }
    }
}
