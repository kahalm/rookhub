using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RookHub.Api.Middleware;

namespace RookHub.Api.Tests;

/// <summary>
/// Log-Feld <c>ForwardedFor</c> (Codereview 2026-09-29, A10-008): es soll die ROHE Weiterleitungs-Kette tragen, wie sie
/// ankam — samt vom Client vorangestellter Einträge. Vorher las die Anreicherung <c>X-Original-For</c>, und dort legt die
/// ForwardedHeadersMiddleware nur den Socket-Peer ab (frontend-nginx mit Port); <c>XRealIp</c> war immer das
/// Docker-Gateway, weil frontend-nginx den Kopf mit <c>$remote_addr</c> überschreibt.
/// </summary>
public class ForwardedChainCaptureTests
{
    private static async Task<HttpContext> RunPipelineAsync(string peer, string? xff)
    {
        var options = new ForwardedHeadersOptions();
        ForwardedHeadersSetup.Configure(options);
        var forwarded = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));

        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        ctx.Connection.RemotePort = 41234;
        if (xff != null) ctx.Request.Headers["X-Forwarded-For"] = xff;

        // Reihenfolge wie in Program.cs: erst merken, dann verbrauchen.
        ForwardedChainCapture.Capture(ctx);
        await forwarded.Invoke(ctx);
        return ctx;
    }

    [Fact]
    public async Task SpoofedEntry_StaysVisibleInTheLoggedChain_WhileTheClientIpIsResolved()
    {
        var ctx = await RunPipelineAsync("172.26.0.7", "203.0.113.66, 198.51.100.7, 172.26.0.1");

        Assert.Equal(IPAddress.Parse("198.51.100.7"), ctx.Connection.RemoteIpAddress);
        Assert.Equal("203.0.113.66, 198.51.100.7, 172.26.0.1", ForwardedChainCapture.Get(ctx));
        // Der frühere Lesepfad: X-Original-For ist nur der Socket-Peer, der Spoof stand dort nie.
        Assert.Equal("172.26.0.7:41234", ctx.Request.Headers["X-Original-For"].ToString());
    }

    [Fact]
    public void LongHeader_IsCappedForTheLog()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Forwarded-For"] = string.Join(", ", Enumerable.Repeat("203.0.113.66", 60));

        ForwardedChainCapture.Capture(ctx);

        Assert.Equal(ForwardedChainCapture.MaxLength, ForwardedChainCapture.Get(ctx)!.Length);
    }

    [Fact]
    public async Task WithoutHeader_NothingIsLogged()
    {
        var ctx = await RunPipelineAsync("172.26.0.7", null);
        Assert.Null(ForwardedChainCapture.Get(ctx));
    }

    /// <summary>Verdrahtung: gemerkt wird VOR UseForwardedHeaders, die Anreicherung liest die gemerkte Kette, und
    /// weder X-Original-For noch das konstante XRealIp-Feld kommen zurück.</summary>
    [Fact]
    public void ProgramCs_CapturesBeforeForwardedHeaders_AndLogsTheRawChain()
    {
        var src = File.ReadAllText(ProgramCs());
        var capture = src.IndexOf("app.UseForwardedChainCapture();", StringComparison.Ordinal);
        var forwarded = src.IndexOf("app.UseForwardedHeaders();", StringComparison.Ordinal);
        Assert.True(capture >= 0, "app.UseForwardedChainCapture() fehlt in Program.cs");
        Assert.True(forwarded >= 0, "app.UseForwardedHeaders() fehlt in Program.cs");
        Assert.True(capture < forwarded, "Die Kette muss VOR UseForwardedHeaders gemerkt werden");

        Assert.Contains("ForwardedChainCapture.Get(ctx)", src, StringComparison.Ordinal);
        Assert.DoesNotContain("Headers[\"X-Original-For\"]", src, StringComparison.Ordinal);
        Assert.DoesNotContain("\"XRealIp\"", src, StringComparison.Ordinal);
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
