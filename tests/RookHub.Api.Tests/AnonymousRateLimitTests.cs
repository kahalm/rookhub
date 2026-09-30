using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Controllers;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die offenen Endpunkte nach ZWECK getrennt (Codereview 2026-09-29, A10-003/A2-005). Vorher hingen 21 davon an EINER
/// Policy „anonymous-puzzle" (30/min je IP) — eine Schulklasse hinter einer NAT-Adresse, die KidHub öffnet (levels +
/// courses je Kind), bekam ab dem 16. Kind 429 und sah das Fehlerbild; ein zweiter anonymer Endless-Spieler im selben
/// Netz verlor still seine Server-Stände.
/// </summary>
public class AnonymousRateLimitTests
{
    private const string Ip = "203.0.113.7";
    private const string VisitorA = "0f8fad5b-d9cb-469f-a165-70867728950e";
    private const string VisitorB = "7c9e6679-7425-40de-944b-e07fc1f90ae7";

    /// <summary>Die Policy, die die Middleware je Aktion WIRKLICH nimmt — aus den echten MVC-Endpunkt-Metadaten
    /// (Aktions-Attribut schlägt Klassen-Attribut), Schlüssel „Controller.Aktion".</summary>
    private static Dictionary<string, string?> EffectivePolicies()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(KidsController).Assembly);
        using var app = builder.Build();
        app.MapControllers();

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .Select(e => (endpoint: e, action: e.Metadata.GetMetadata<ControllerActionDescriptor>()))
            .Where(x => x.action is not null)
            .GroupBy(x => x.action!.ControllerName + "." + x.action.ActionName)
            .ToDictionary(g => g.Key, g => g.First().endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Fact]
    public void AnonymousEndpoints_UseTheirPurposePolicy_InsteadOfOneSharedBucket()
    {
        var p = EffectivePolicies();

        // Kinderseite: die für alle gleichen Lesezugriffe — eigener, hoher Deckel je Adresse.
        foreach (var a in new[] { "GetLanguageHint", "GetLevels", "GetLevel", "GetCourses", "GetCoursePuzzles" })
            Assert.Equal("kids-read", p["Kids." + a]);
        Assert.Equal("anonymous-read", p["Kids.GetEndlessBatch"]);   // teure Suche, bleibt im globalen Topf

        // Anonyme Lesezugriffe der App.
        foreach (var key in new[] { "Puzzle.GetAnonymousStats", "Endless.GetAnonymousProgress", "GameAnalysis.ListPublic", "GuessTree.Branch" })
            Assert.Equal("anonymous-read", p[key]);

        // Anonyme Stände/Versuche — je Spieler statt je Adresse.
        foreach (var key in new[]
                 {
                     "Puzzle.RecordAnonymousAttempt", "BookPuzzle.RecordAnonymousAttempt", "BookPuzzle.Track",
                     "Endless.SaveAnonymousProgress", "Endless.RecordAnonymousSession", "Endless.BulkImportAnonymousSessions",
                 })
            Assert.Equal("anonymous-write", p[key]);
        var guess = p.Where(kv => kv.Key.StartsWith("GuessSessionAnonymous.", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(guess);
        Assert.All(guess, kv => Assert.Equal("anonymous-write", kv.Value));

        // Der Rest bleibt, wie er war (Client-Log, Bot-Statistik, Token-Test, Extension-Senke, Bestandssuche).
        foreach (var key in new[]
                 {
                     "BotStats.GetPlayerProgress", "ClientLog.Post", "Token.Test", "Extension.ChessableReviewLinesAnon",
                     "LibraryGame.Search", "LibraryGame.Semantic",
                 })
            Assert.Equal("anonymous-puzzle", p[key]);
    }

    private static HttpContext Context(string? visitor = null, int? userId = null, string? policy = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(Ip);
        if (visitor is not null) ctx.Request.Headers["X-Visitor-Id"] = visitor;
        if (userId is { } id)
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Test"));
        if (policy is not null)
            ctx.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
                new EndpointMetadataCollection(new EnableRateLimitingAttribute(policy)), policy));
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

    /// <summary>Das Fehlerszenario: 30 Kinder hinter EINER Adresse öffnen KidHub (levels + courses + ggf. language-hint
    /// je Kind = 90 Anfragen in einer Minute). Weder die Policy noch der globale 100/min-Topf dürfen sie abweisen.</summary>
    [Fact]
    public void KidsRead_LetsAWholeClassBehindOneAddressThrough_AndLeavesTheGlobalBucketAlone()
    {
        using var global = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartitions.Global(c, 1));
        using var kids = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartitions.KidsRead(c, 1));
        var ctx = Context(policy: "kids-read");

        Assert.Equal(90, Acquired(global, ctx, 90));
        Assert.Equal(90, Acquired(kids, ctx, 90));

        // Der globale Topf derselben Adresse ist danach unberührt — die übrige App (Anmeldung, Sitzung) läuft weiter.
        Assert.Equal(RateLimitPartitions.GlobalPermitPerMinute, Acquired(global, Context(policy: "auth-session"), 150));
    }

    /// <summary>Die Obergrenze je Adresse bleibt: kids-read ist aus dem globalen Topf genommen, also MUSS die Policy
    /// selbst deckeln.</summary>
    [Fact]
    public void KidsRead_StillCapsOneAddress()
    {
        using var kids = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartitions.KidsRead(c, 1));
        Assert.Equal(RateLimitPartitions.KidsReadPermitPerMinute, Acquired(kids, Context(), RateLimitPartitions.KidsReadPermitPerMinute + 20));
        Assert.True(RateLimitPartitions.KidsReadPermitPerMinute > RateLimitPartitions.GlobalPermitPerMinute);
    }

    [Fact]
    public void Global_ExemptsOnlyKidsRead()
    {
        Assert.True(RateLimitPartitions.HasOwnIpCeiling(Context(policy: "kids-read")));
        foreach (var other in new[] { "anonymous-read", "anonymous-write", "anonymous-puzzle", "auth" })
            Assert.False(RateLimitPartitions.HasOwnIpCeiling(Context(policy: other)));
        Assert.False(RateLimitPartitions.HasOwnIpCeiling(Context()));
    }

    /// <summary>Zwei anonyme Endless-Spieler im selben Netz: jeder hat sein eigenes Fenster (die App schickt X-Visitor-Id).
    /// Vorher verlor der zweite still seine Server-Stände.</summary>
    [Fact]
    public void AnonymousWrite_CountsPerVisitor_WithinOneAddress()
    {
        using var write = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartitions.AnonymousWrite(c, 1));
        var permit = RateLimitPartitions.AnonymousWritePermitPerMinute;

        Assert.Equal(permit, Acquired(write, Context(VisitorA), permit + 5));
        Assert.Equal(permit, Acquired(write, Context(VisitorB), permit));
        Assert.Equal(permit, Acquired(write, Context(userId: 42), permit));   // angemeldet: je Konto
    }

    [Fact]
    public void PartitionKeys_UserVisitorOrIp()
    {
        Assert.Equal("u:42", RateLimitPartitions.UserVisitorOrIp(Context(VisitorA, userId: 42)));
        Assert.Equal($"ip:{Ip}|a:{VisitorA}", RateLimitPartitions.UserVisitorOrIp(Context(VisitorA)));
        Assert.Equal($"ip:{Ip}", RateLimitPartitions.UserVisitorOrIp(Context()));
        // Eine ungültige Visitor-Id (Form wie die Anon-Session-Id) zählt nicht — sonst wäre sie eine freie Partition.
        Assert.Equal($"ip:{Ip}", RateLimitPartitions.UserVisitorOrIp(Context("abc")));

        // Der Rest („anonymous-puzzle") trennt angemeldete je Konto, anonyme bleiben je Adresse (ohne Visitor-Id).
        Assert.Equal("u:42", RateLimitPartitions.UserOrIp(Context(VisitorA, userId: 42)));
        Assert.Equal($"ip:{Ip}", RateLimitPartitions.UserOrIp(Context(VisitorA)));
    }
}
