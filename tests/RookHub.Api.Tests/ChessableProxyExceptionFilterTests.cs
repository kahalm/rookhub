using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using RookHub.Api.Controllers;
using RookHub.Api.Filters;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// EINE Abbildung der piratechess-Fehler auf HTTP (Codereview 2026-09-29, A3-015): vorher 3× 400/502 in der
/// Extension, 5× pauschal 400 in den Chessable-Controllern, und ein nicht erreichbares piratechess
/// (HttpRequestException) war überall ein nackter 500.
/// </summary>
public class ChessableProxyExceptionFilterTests
{
    private static (ExceptionContext Ctx, CapturingLogger<ChessableProxyExceptionFilter> Log) Run(Exception ex)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var ctx = new ExceptionContext(actionContext, new List<IFilterMetadata>()) { Exception = ex };
        var log = new CapturingLogger<ChessableProxyExceptionFilter>();
        new ChessableProxyExceptionFilter(log).OnException(ctx);
        return (ctx, log);
    }

    [Fact]
    public void Upstream400_IsBadRequest_WithItsMessage()
    {
        var result = ChessableProxyExceptionFilter.ResultFor(new ChessableProxyException(HttpStatusCode.BadRequest, "Invalid bearer"));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(new { message = "Invalid bearer" }), JsonSerializer.Serialize(result.Value));
    }

    /// <summary>Alles außer 400 ist kein Fehler des Aufrufers — und ein 401 darf nie durchgereicht werden
    /// (RepCheck liest 401 als „RookHub-Token ungültig").</summary>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public void OtherUpstreamStatuses_Are502(HttpStatusCode upstream)
    {
        var result = ChessableProxyExceptionFilter.ResultFor(new ChessableProxyException(upstream, "x"));
        Assert.Equal(502, result.StatusCode);
    }

    [Fact]
    public void Filter_UncaughtProxyException_UsesTheSameRule()
    {
        var (ctx, _) = Run(new ChessableProxyException(HttpStatusCode.ServiceUnavailable, "down"));

        Assert.True(ctx.ExceptionHandled);
        Assert.Equal(502, Assert.IsType<ObjectResult>(ctx.Result).StatusCode);
    }

    [Fact]
    public void Filter_TransportError_Is502_WithWarning()
    {
        var (ctx, log) = Run(new HttpRequestException("Connection refused (piratechess:8080)"));

        Assert.True(ctx.ExceptionHandled);
        var obj = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(502, obj.StatusCode);
        // Keine Interna (Hostname) an den Client.
        Assert.Equal(JsonSerializer.Serialize(new { message = ChessableProxyException.UnreachableMessage }),
            JsonSerializer.Serialize(obj.Value));
        Assert.Contains(log.Events, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Filter_OtherExceptions_StayUnhandled()
    {
        var (ctx, _) = Run(new InvalidOperationException("bug"));

        Assert.False(ctx.ExceptionHandled);
        Assert.Null(ctx.Result);
    }

    /// <summary>Der Filter wirkt nur dort, wo er hängt: an beiden Chessable-Controllern und an den drei
    /// Ingest-Actions der Extension (die einzigen, die piratechess hart brauchen).</summary>
    [Fact]
    public void Filter_IsAttached_ToTheChessableSurface()
    {
        static bool Has(MemberInfo m) => m.GetCustomAttributes<TypeFilterAttribute>()
            .Any(a => a.ImplementationType == typeof(ChessableProxyExceptionFilter));

        Assert.True(Has(typeof(ChessableController)));
        Assert.True(Has(typeof(ChessableAdminController)));
        foreach (var action in new[]
                 {
                     nameof(ExtensionController.ChessableIngest),
                     nameof(ExtensionController.ChessableIngestChunk),
                     nameof(ExtensionController.ChessableIngestLive),
                 })
            Assert.True(Has(typeof(ExtensionController).GetMethod(action)!), action);
    }
}
