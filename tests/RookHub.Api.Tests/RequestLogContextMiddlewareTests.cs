using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RookHub.Api.Logging;
using RookHub.Api.Middleware;
using Serilog.Events;

namespace RookHub.Api.Tests;

/// <summary>
/// <see cref="RequestLogContextMiddleware"/> (früher Inline-Lambda in Program.cs, Zerlegung Z2): jedes Log-Event
/// innerhalb der Anfrage trägt IpAddress/ForwardedFor/UserId/UserName/ImpersonatorId/VisitorId/RequestKind/
/// UserAgent/DeviceType, und nach der Anfrage ist der LogContext wieder leer.
/// </summary>
public class RequestLogContextMiddlewareTests
{
    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static (Serilog.ILogger Log, CollectingSink Sink) Logger()
    {
        var sink = new CollectingSink();
        var log = new Serilog.LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();
        return (log, sink);
    }

    private static string? Prop(LogEvent e, string name) =>
        e.Properties.TryGetValue(name, out var v) ? (string?)((ScalarValue)v).Value : null;

    [Fact]
    public async Task AuthenticatedRequest_PushesAllProperties_AndPopsThemAfterwards()
    {
        var (log, sink) = Logger();
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        ctx.Request.Path = "/api/profile";
        ctx.Request.Headers.UserAgent = new string('u', 600);
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Name, "anna"), new Claim("imp", "1")], "Jwt"));

        var mw = new RequestLogContextMiddleware(_ => { log.Information("innen"); return Task.CompletedTask; });
        await mw.InvokeAsync(ctx);
        log.Information("danach");

        var inner = sink.Events[0];
        Assert.Equal("203.0.113.9", Prop(inner, "IpAddress"));
        Assert.Equal("7", Prop(inner, "UserId"));
        Assert.Equal("anna", Prop(inner, "UserName"));
        Assert.Equal("1", Prop(inner, "ImpersonatorId"));
        Assert.Equal(VisitorIdResolver.Resolve(true, "anna", null), Prop(inner, "VisitorId"));
        Assert.Equal(SystemCallClassifier.Classify("/api/profile"), Prop(inner, "RequestKind"));
        Assert.Equal(512, Prop(inner, "UserAgent")!.Length);
        Assert.Equal(DeviceClassifier.Classify(new string('u', 600)), Prop(inner, "DeviceType"));
        Assert.False(inner.Properties.ContainsKey("ForwardedFor"));   // ohne gemerkte Kette kein Feld

        var after = sink.Events[1];
        Assert.Empty(after.Properties);
    }

    [Fact]
    public async Task AnonymousRequest_WithoutIpAndAgent_StillCarriesKindAndDevice()
    {
        var (log, sink) = Logger();
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/health";

        var mw = new RequestLogContextMiddleware(_ => { log.Information("innen"); return Task.CompletedTask; });
        await mw.InvokeAsync(ctx);

        var e = Assert.Single(sink.Events);
        Assert.False(e.Properties.ContainsKey("IpAddress"));
        Assert.False(e.Properties.ContainsKey("UserId"));
        Assert.False(e.Properties.ContainsKey("UserAgent"));
        Assert.Equal(SystemCallClassifier.Classify("/health"), Prop(e, "RequestKind"));
        Assert.Equal(DeviceClassifier.Classify(""), Prop(e, "DeviceType"));
    }

    [Fact]
    public async Task ThrowingNext_StillPopsTheContext()
    {
        var (log, sink) = Logger();
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
        var mw = new RequestLogContextMiddleware(_ => throw new InvalidOperationException("kaputt"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => mw.InvokeAsync(ctx));
        log.Information("danach");

        Assert.Empty(Assert.Single(sink.Events).Properties);
    }
}
