using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Exceptions;
using RookHub.Api.Filters;

namespace RookHub.Api.Tests;

/// <summary>
/// Mapping der Crawler-Fehler auf Antworten an den Aufrufer. Wichtig: Gateway-Status des Crawlers
/// (502/503/504 — vom UpstreamErrorMiddleware des Crawlers gesetzt) werden 1:1 durchgereicht statt
/// pauschal auf 502 zu kollabieren; uneindeutige 5xx werden auf 502 normalisiert.
/// </summary>
public class CrawlerExceptionFilterTests
{
    private static async Task<ObjectResult> RunAsync(Exception ex)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var ctx = new ExceptionContext(actionContext, new List<IFilterMetadata>()) { Exception = ex };
        var filter = new CrawlerExceptionFilter(NullLogger<CrawlerExceptionFilter>.Instance);

        await filter.OnExceptionAsync(ctx);

        Assert.True(ctx.ExceptionHandled);
        return Assert.IsType<ObjectResult>(ctx.Result);
    }

    [Theory]
    [InlineData(HttpStatusCode.GatewayTimeout, 504)]      // chess-results.com-Timeout
    [InlineData(HttpStatusCode.ServiceUnavailable, 503)]  // Crawler-Rate-Limiter gesaettigt
    [InlineData(HttpStatusCode.BadGateway, 502)]          // Upstream weg
    [InlineData(HttpStatusCode.NotFound, 404)]            // 4xx unveraendert
    [InlineData(HttpStatusCode.BadRequest, 400)]
    public async Task GatewayAndClientStatuses_ArePassedThrough(HttpStatusCode crawlerStatus, int expected)
    {
        var result = await RunAsync(new CrawlerRequestException(crawlerStatus, "{\"message\":\"x\"}"));
        Assert.Equal(expected, result.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task AmbiguousServerErrors_NormalizedTo502(HttpStatusCode crawlerStatus)
    {
        var result = await RunAsync(new CrawlerRequestException(crawlerStatus, "boom"));
        Assert.Equal(502, result.StatusCode);
    }

    [Fact]
    public async Task ConnectivityError_MapsTo502()
    {
        var result = await RunAsync(new HttpRequestException("connection refused"));
        Assert.Equal(502, result.StatusCode);
    }

    [Fact]
    public async Task Timeout_MapsTo504()
    {
        var result = await RunAsync(new TaskCanceledException("timeout"));
        Assert.Equal(504, result.StatusCode);
    }
}
