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
/// Welche Client-IP die API aus <c>X-Forwarded-For</c> übernimmt (Codereview 2026-09-29, A10-002). Mit
/// <c>ForwardLimit = null</c> und allen privaten Netzen als „Proxy" rollte die Middleware bei einem Client mit privater
/// Adresse (LAN, VPN) bis zu dem Eintrag zurück, den er selbst mitgeschickt hatte — je Anfrage eine erfundene Adresse,
/// je Anfrage eine frische Rate-Limit-Partition. Die Tests laufen gegen die ECHTE <see cref="ForwardedHeadersMiddleware"/>
/// mit genau der Konfiguration, die <c>Program.cs</c> registriert.
/// </summary>
public class ForwardedHeadersSetupTests
{
    /// <summary>frontend-nginx im Prod-Stack (Netz rookhub-schach_rookhub) und das Docker-Gateway, über das NPM kommt.</summary>
    private const string FrontendNginx = "172.26.0.7";
    private const string DockerGateway = "172.26.0.1";

    private static async Task<HttpContext> RunAsync(string peer, string? xff)
    {
        var options = new ForwardedHeadersOptions();
        ForwardedHeadersSetup.Configure(options);
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));

        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        ctx.Connection.RemotePort = 41234;
        if (xff != null) ctx.Request.Headers["X-Forwarded-For"] = xff;
        await middleware.Invoke(ctx);
        return ctx;
    }

    [Fact]
    public async Task PublicClient_ThroughNpmAndFrontendNginx_IsTheClientIp()
    {
        var ctx = await RunAsync(FrontendNginx, $"198.51.100.7, {DockerGateway}");
        Assert.Equal(IPAddress.Parse("198.51.100.7"), ctx.Connection.RemoteIpAddress);
    }

    /// <summary>IPv4-gemappter Peer, wie ihn Kestrel im Container meldet (<c>[::ffff:172.26.0.7]</c>).</summary>
    [Fact]
    public async Task MappedIpv4Peer_IsStillTrustedAsProxy()
    {
        var ctx = await RunAsync("::ffff:" + FrontendNginx, $"198.51.100.7, {DockerGateway}");
        Assert.Equal(IPAddress.Parse("198.51.100.7"), ctx.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task SpoofedPrefix_FromPublicClient_IsIgnored()
    {
        var ctx = await RunAsync(FrontendNginx, $"203.0.113.50, 198.51.100.7, {DockerGateway}");
        Assert.Equal(IPAddress.Parse("198.51.100.7"), ctx.Connection.RemoteIpAddress);
    }

    /// <summary>Der Fund: LAN-/VPN-Client über NPM schickt eine erfundene Adresse vor seine echte. Vorher stand die
    /// erfundene als Client-IP da (je Anfrage gewechselt = je Anfrage ein frisches Login-Fenster).</summary>
    [Theory]
    [InlineData("10.24.12.5")]
    [InlineData("10.8.0.2")]
    [InlineData("192.168.1.20")]
    [InlineData("172.25.0.2")]
    public async Task PrivateClient_ThroughNpm_CannotSpoofItsAddress(string realClient)
    {
        var ctx = await RunAsync(FrontendNginx, $"203.0.113.50, {realClient}, {DockerGateway}");
        Assert.Equal(IPAddress.Parse(realClient), ctx.Connection.RemoteIpAddress);
    }

    /// <summary>Gerät im LAN spricht den veröffentlichten API-Port (0.0.0.0:5001) direkt an: es ist kein Proxy, sein
    /// <c>X-Forwarded-For</c> zählt nicht.</summary>
    [Fact]
    public async Task LanDevice_OnThePublishedApiPort_CannotSpoofItsAddress()
    {
        var ctx = await RunAsync("10.24.12.5", "203.0.113.50");
        Assert.Equal(IPAddress.Parse("10.24.12.5"), ctx.Connection.RemoteIpAddress);
    }

    /// <summary>Stacks aus Dockers 192.168.x/20-Pool (auf dem Server z. B. leaguehub_default) bleiben vertraute Proxys.</summary>
    [Fact]
    public async Task ProxyInDockers192168Pool_IsTrusted()
    {
        var ctx = await RunAsync("192.168.224.3", "198.51.100.7");
        Assert.Equal(IPAddress.Parse("198.51.100.7"), ctx.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task PublicPeer_IsNeverTrustedAsProxy()
    {
        var ctx = await RunAsync("198.51.100.9", "203.0.113.50");
        Assert.Equal(IPAddress.Parse("198.51.100.9"), ctx.Connection.RemoteIpAddress);
    }

    [Fact]
    public void Options_StopAfterTheTwoDocumentedHops()
    {
        var options = new ForwardedHeadersOptions();
        ForwardedHeadersSetup.Configure(options);

        Assert.Equal(2, options.ForwardLimit);
        Assert.Empty(options.KnownProxies);
        Assert.DoesNotContain(options.KnownIPNetworks, n => n.Contains(IPAddress.Parse("10.24.12.5")));
        Assert.DoesNotContain(options.KnownIPNetworks, n => n.Contains(IPAddress.Loopback));
    }

    /// <summary>Program.cs registriert genau diese Konfiguration — keine zweite Inline-Fassung, die abdriften könnte.</summary>
    [Fact]
    public void ProgramCs_UsesTheSharedSetup()
    {
        var src = File.ReadAllText(ProgramCs());
        Assert.Contains("Configure<ForwardedHeadersOptions>(ForwardedHeadersSetup.Configure)", src, StringComparison.Ordinal);
        Assert.DoesNotContain("ForwardLimit", src, StringComparison.Ordinal);
        Assert.DoesNotContain("KnownNetworks", src, StringComparison.Ordinal);
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
