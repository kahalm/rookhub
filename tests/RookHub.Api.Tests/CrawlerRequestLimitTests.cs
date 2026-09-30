using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Controllers;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Crawler-Budget je Konto (Codereview 2026-09-29, A5-004). Crawl, Spieler-Details, „Vereine nachtragen" und
/// „Monitor einschalten" liefen nur unter dem globalen Deckel von 100/min je IP — ein frei registriertes Konto
/// fuellte die geteilte Crawler-Warteschlange (500) in rund fuenf Minuten, danach bekamen Runden-Monitor,
/// Abo-Abgleich und Turnierverlauf 429.
/// </summary>
public class CrawlerRequestLimitTests
{
    [Theory]
    [InlineData(typeof(TournamentProxyController), nameof(TournamentProxyController.Crawl))]
    [InlineData(typeof(TournamentProxyController), nameof(TournamentProxyController.CrawlPlayerDetails))]
    [InlineData(typeof(TournamentProxyController), nameof(TournamentProxyController.FillClubs))]
    [InlineData(typeof(TournamentMonitorController), nameof(TournamentMonitorController.Activate))]
    public void CrawlerEndpoints_UseThePerUserCrawlerPolicy(Type controller, string action)
    {
        var method = controller.GetMethod(action)!;
        Assert.Equal(RateLimitPartitions.CrawlerRequestPolicy,
            method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Fact]
    public void CrawlerPolicy_IsRegisteredInProgram()
    {
        // Program.cs registriert mit dem Literal (AuthRateLimitTests liest die Namen aus dem Quelltext).
        var src = File.ReadAllText(ProgramCs());
        Assert.Contains($@"AddPolicy(""{RateLimitPartitions.CrawlerRequestPolicy}"", ctx => RookHub.Api.Services.RateLimitPartitions.CrawlerRequest(ctx, permitScale))", src);
    }

    /// <summary>Je KONTO: das 11. Crawl-Auftrag in der Minute wird abgewiesen, ein zweites Konto hinter derselben
    /// Adresse (Verein, Schule) hat sein eigenes Fenster.</summary>
    [Fact]
    public void CrawlerRequest_CapsOneAccount_ButNotItsNeighboursBehindTheSameAddress()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartitions.CrawlerRequest(c, 1));
        var permit = RateLimitPartitions.CrawlerRequestPermitPerMinute;

        Assert.Equal(permit, Acquired(limiter, Context(42), permit + 5));
        Assert.Equal(permit, Acquired(limiter, Context(43), permit));
        Assert.True(permit < RateLimitPartitions.GlobalPermitPerMinute);
    }

    private static HttpContext Context(int userId)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"));
        return ctx;
    }

    private static int Acquired(PartitionedRateLimiter<HttpContext> limiter, HttpContext ctx, int tries)
    {
        var ok = 0;
        for (var i = 0; i < tries; i++)
        {
            using var lease = limiter.AttemptAcquire(ctx);
            if (lease.IsAcquired) ok++;
        }
        return ok;
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
