using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Rate-Limit-Absagen (Codereview 2026-09-29, A10-010): vorher setzte der Limiter nur den Status 429 — kein
/// <c>Retry-After</c>, kein Rumpf, und weil <c>UseRateLimiter</c> vor dem Request-Log steht, in KEINEM Log. Jetzt
/// antwortet <see cref="RateLimitRejectionHandler"/> mit Wartezeit und Rumpf und meldet je (Adresse, Policy) und Minute
/// eine Warnung, die Absagen dazwischen gezählt.
/// </summary>
public class RateLimitRejectionHandlerTests
{
    private sealed record Rejection(string message, int? retryAfterSeconds);

    /// <summary>Echte RateLimitingMiddleware hinter einem TestServer: globaler Deckel 1/min, dazu ein Endpunkt mit
    /// eigener Policy (ebenfalls 1/min). Program.cs startet im Test nicht (Migrate braucht MariaDB).</summary>
    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(CapturingLogger<RateLimitRejectionHandler> log)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new RateLimitRejectionHandler(log));
        static RateLimitPartition<string> OnePerMinute(string key) =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 1,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
        builder.Services.AddRateLimiter(o =>
        {
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                ctx.Request.Path.StartsWithSegments("/tight")
                    ? RateLimitPartition.GetNoLimiter("tight")
                    : OnePerMinute("all"));
            o.AddPolicy("tight", _ => OnePerMinute("tight"));
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = RateLimitRejectionHandler.OnRejectedAsync;
        });

        var app = builder.Build();
        app.Use((ctx, next) =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
            return next(ctx);
        });
        app.UseRouting();
        app.UseRateLimiter();
        app.MapGet("/open", () => "ok");
        app.MapGet("/tight", () => "ok").RequireRateLimiting("tight");
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    [Fact]
    public async Task Rejection_CarriesRetryAfterAndBody_AndIsLoggedOncePerWindow()
    {
        var log = new CapturingLogger<RateLimitRejectionHandler>();
        var (app, client) = await StartAsync(log);
        await using var _ = app;

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/open")).StatusCode);

        var rejected = await client.GetAsync("/open");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var retryAfter = rejected.Headers.RetryAfter?.Delta;
        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter!.Value.TotalSeconds, 1, 60);
        var body = await rejected.Content.ReadFromJsonAsync<Rejection>();
        Assert.Equal(RateLimitRejectionHandler.Message, body!.message);
        Assert.Equal((int)retryAfter.Value.TotalSeconds, body.retryAfterSeconds);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/open")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/open")).StatusCode);

        var warning = Assert.Single(log.Events);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal("203.0.113.9", warning.State["IpAddress"]);
        Assert.Equal("/open", warning.State["RequestPath"]);
        Assert.Equal("GET", warning.State["RequestMethod"]);
        Assert.Equal("global", warning.State["RateLimitPolicy"]);
        Assert.Equal(0, warning.State["SuppressedRejections"]);
    }

    [Fact]
    public async Task Rejection_NamesTheEndpointPolicy()
    {
        var log = new CapturingLogger<RateLimitRejectionHandler>();
        var (app, client) = await StartAsync(log);
        await using var _ = app;

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tight")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/tight")).StatusCode);

        Assert.Equal("tight", Assert.Single(log.Events).State["RateLimitPolicy"]);
    }

    /// <summary>Ein Limiter ohne Wartezeit (reine Gleichzeitigkeit) → kein Retry-After, <c>retryAfterSeconds: null</c>.</summary>
    [Fact]
    public async Task LeaseWithoutRetryAfter_SendsNoHeader()
    {
        var handler = new RateLimitRejectionHandler(new CapturingLogger<RateLimitRejectionHandler>());
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        await handler.HandleAsync(ctx, new NoMetadataLease(), CancellationToken.None);

        Assert.False(ctx.Response.Headers.ContainsKey("Retry-After"));
        ctx.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(ctx.Response.Body);
        Assert.Equal(RateLimitRejectionHandler.Message, json.RootElement.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("retryAfterSeconds").ValueKind);
    }

    [Fact]
    public void Throttle_CountsSuppressedRejections_AndReportsThemWithTheNextWarning()
    {
        var handler = new RateLimitRejectionHandler(new CapturingLogger<RateLimitRejectionHandler>());
        var t0 = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal((true, 0), handler.Register("203.0.113.9|auth", t0));
        Assert.Equal((false, 0), handler.Register("203.0.113.9|auth", t0.AddSeconds(10)));
        Assert.Equal((false, 0), handler.Register("203.0.113.9|auth", t0.AddSeconds(50)));
        // Andere Policy derselben Adresse = eigener Eintrag.
        Assert.Equal((true, 0), handler.Register("203.0.113.9|global", t0.AddSeconds(20)));

        Assert.Equal((true, 2), handler.Register("203.0.113.9|auth", t0.AddSeconds(61)));
        Assert.Equal((false, 0), handler.Register("203.0.113.9|auth", t0.AddSeconds(62)));
    }

    [Fact]
    public void Throttle_TableStaysBounded()
    {
        var handler = new RateLimitRejectionHandler(new CapturingLogger<RateLimitRejectionHandler>());
        var t0 = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < RateLimitRejectionHandler.MaxEntries + 300; i++)
            handler.Register($"10.0.{i / 256}.{i % 256}|global", t0.AddMilliseconds(i));

        Assert.True(handler.EntryCount <= RateLimitRejectionHandler.MaxEntries);
    }

    /// <summary>Verdrahtung in der echten Pipeline: Program.cs registriert den Dienst und hängt ihn als OnRejected ein.</summary>
    [Fact]
    public void ProgramCs_WiresTheRejectionHandler()
    {
        var src = File.ReadAllText(ProgramCs());
        Assert.Contains("builder.Services.AddSingleton<RateLimitRejectionHandler>();", src, StringComparison.Ordinal);
        Assert.Contains("options.OnRejected = RateLimitRejectionHandler.OnRejectedAsync;", src, StringComparison.Ordinal);
    }

    private sealed class NoMetadataLease : RateLimitLease
    {
        public override bool IsAcquired => false;
        public override IEnumerable<string> MetadataNames => [];
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }

    private static string ProgramCs([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "src", "api", "RookHub.Api", "Program.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        Assert.Fail("Program.cs nicht gefunden");
        return "";
    }
}
