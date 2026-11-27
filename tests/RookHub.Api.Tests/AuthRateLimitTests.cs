using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Controllers;
using RookHub.Api.Controllers.Accounts;
using RookHub.Api.Services;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace RookHub.Api.Tests;

/// <summary>
/// Welcher Rate-Limiter an welchem Auth-Endpunkt hängt. Benannte Limiter partitionieren nach (Policy, Schlüssel):
/// alles, was auf „auth" liegt, teilt sich EIN 10/min-Fenster je IP. Lagen dort auch die Rechte-Abfrage (jede sichtbare
/// Oberfläche alle 2 min) und die Sitzungs-Probe (jeder App-Start ohne Anmeldung), bekam hinter einem NAT (Verein,
/// Schulklasse) die Anmeldung 429 — derselbe Fehler, den Program.cs für die Nutzersuche schon einmal behoben hat.
/// </summary>
public class AuthRateLimitTests
{
    /// <summary>Die Policy, die die Middleware für jede AuthController-Aktion WIRKLICH nimmt: aus den Endpunkt-Metadaten
    /// der echten MVC-Endpunkte (<c>GetMetadata</c> liefert das zuletzt eingetragene Attribut — das der Aktion schlägt
    /// das der Klasse), nicht aus einer nachgebauten Regel.</summary>
    private static Dictionary<string, string?> EffectiveAuthPolicies()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        using var app = builder.Build();
        app.MapControllers();

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .Select(e => (endpoint: e, action: e.Metadata.GetMetadata<ControllerActionDescriptor>()))
            .Where(x => x.action?.ControllerTypeInfo.AsType() == typeof(AuthController))
            .ToDictionary(x => x.action!.ActionName, x => x.endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Fact]
    public void AuthEndpoints_KeepLoginApartFromPermissionPollingAndSessionProbe()
    {
        var policies = EffectiveAuthPolicies();

        Assert.Equal("auth-permissions", policies[nameof(AuthController.GetPermissions)]);
        Assert.Equal("auth-session", policies[nameof(AuthController.SharedSession)]);
        Assert.Equal("auth-session", policies[nameof(AuthController.EndSharedSession)]);
        // Hub-Wechsel (Code holen + einlösen) nicht mehr im Login-Fenster: jeder Sprung kostete dort zwei der zehn
        // Versuche (Prod 27.11.2026). Der Code ist 256 Bit Zufall, kurzlebig und ans geteilte Cookie gebunden.
        Assert.Equal(RateLimitPartitions.AuthHandoffPolicy, policies[nameof(AuthController.Handoff)]);
        Assert.Equal(RateLimitPartitions.AuthHandoffPolicy, policies[nameof(AuthController.HandoffExchange)]);

        // Alles, was ein Passwort prüft, bleibt im strengen Fenster (Brute-Force).
        foreach (var strict in new[]
                 {
                     nameof(AuthController.Register), nameof(AuthController.Login),
                     nameof(AuthController.ForgotPassword),
                     nameof(AuthController.ResetPassword), nameof(AuthController.ChangePassword),
                 })
            Assert.Equal("auth", policies[strict]);
    }

    /// <summary>Ein Attribut mit einer Policy, die Program.cs nicht registriert, ist kein fehlender Schutz, sondern ein
    /// 500 bei JEDEM Aufruf („no rate limiter policy") — deshalb gegen den Quelltext geprüft.</summary>
    [Fact]
    public void EveryRateLimitPolicyUsedByAController_IsRegisteredInProgramCs()
    {
        var src = StartupSource.All();
        var registered = Regex.Matches(src, @"AddPolicy\(""([^""]+)""").Select(m => m.Groups[1].Value).ToHashSet();

        var used = typeof(AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetCustomAttributes<EnableRateLimitingAttribute>(inherit: true)
                .Concat(t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(m => m.GetCustomAttributes<EnableRateLimitingAttribute>())))
            .Select(a => a.PolicyName!)
            .ToHashSet();

        Assert.Contains("auth-permissions", used);
        Assert.Contains("auth-session", used);
        Assert.Contains(RateLimitPartitions.AuthHandoffPolicy, used);
        Assert.Empty(used.Except(registered));
    }

    /// <summary>Die Rechte-Abfrage zählt je NUTZER (wie die Nutzersuche) — je IP hätten sich wieder alle hinter einem NAT
    /// ein Fenster geteilt.</summary>
    [Fact]
    public void PermissionPolicy_IsPartitionedPerUser()
    {
        var src = StartupSource.All();
        var start = src.IndexOf(@"AddPolicy(""auth-permissions""", StringComparison.Ordinal);
        Assert.True(start >= 0, "Policy auth-permissions fehlt in Program.cs");
        var next = src.IndexOf("AddPolicy(", start + 1, StringComparison.Ordinal);
        var block = src[start..(next < 0 ? src.Length : next)];
        Assert.Contains("ClaimTypes.NameIdentifier", block);
    }

    /// <summary>Die Deckel nach dem Prod-Fall vom 27.11.2026 (ein Nutzer, Hub-Wechsel): global 300, Rechte 90, Sitzung 180,
    /// Übergabe 180 — Login bleibt bei 10.</summary>
    [Fact]
    public void Ceilings_AfterHubSwitchIncident()
    {
        Assert.Equal(300, RateLimitPartitions.GlobalPermitPerMinute);
        Assert.Equal(90, RateLimitPartitions.AuthPermissionsPermitPerMinute);
        Assert.Equal(180, RateLimitPartitions.AuthSessionPermitPerMinute);
        Assert.Equal(180, RateLimitPartitions.AuthHandoffPermitPerMinute);
        Assert.Contains(@"AddPolicy(""auth"", ctx => PerIpFixedWindow(ctx, 10 * permitScale))", StartupSource.All());
    }

    /// <summary>Code holen zählt je KONTO (ein Nutzer hinter einem NAT sperrt nicht die anderen), Code einlösen — ohne
    /// Anmeldung — je Adresse; beide Fenster sind getrennt.</summary>
    [Fact]
    public void HandoffPolicy_IssueCountsPerAccount_RedeemPerAddress()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartitions.AuthHandoff(c, 1));
        var permit = RateLimitPartitions.AuthHandoffPermitPerMinute;

        Assert.Equal(permit, Acquire(limiter, Ctx(userId: 1), permit + 5));
        Assert.Equal(permit, Acquire(limiter, Ctx(userId: 2), permit + 5));   // anderes Konto, gleiche Adresse
        Assert.Equal(permit, Acquire(limiter, Ctx(), permit + 5));            // anonym einlösen: eigenes Adressfenster
        Assert.Equal(0, Acquire(limiter, Ctx(), 1));
    }

    private static HttpContext Ctx(int? userId = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.9");
        if (userId is { } id)
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) }, "test"));
        return ctx;
    }

    private static int Acquire(PartitionedRateLimiter<HttpContext> limiter, HttpContext ctx, int count)
    {
        var ok = 0;
        for (var i = 0; i < count; i++)
        {
            using var lease = limiter.AttemptAcquire(ctx);
            if (lease.IsAcquired) ok++;
        }
        return ok;
    }
}
