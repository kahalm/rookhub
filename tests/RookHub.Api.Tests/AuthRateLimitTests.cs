using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Controllers;

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
        Assert.Equal("auth-session", policies[nameof(AuthController.LegacySharedSession)]);
        Assert.Equal("auth-session", policies[nameof(AuthController.LegacyEndSharedSession)]);

        // Alles, was ein Passwort oder einen Code prüft, bleibt im strengen Fenster.
        foreach (var strict in new[]
                 {
                     nameof(AuthController.Register), nameof(AuthController.Login), nameof(AuthController.Handoff),
                     nameof(AuthController.HandoffExchange), nameof(AuthController.ForgotPassword),
                     nameof(AuthController.ResetPassword), nameof(AuthController.ChangePassword),
                 })
            Assert.Equal("auth", policies[strict]);
    }

    /// <summary>Ein Attribut mit einer Policy, die Program.cs nicht registriert, ist kein fehlender Schutz, sondern ein
    /// 500 bei JEDEM Aufruf („no rate limiter policy") — deshalb gegen den Quelltext geprüft.</summary>
    [Fact]
    public void EveryRateLimitPolicyUsedByAController_IsRegisteredInProgramCs()
    {
        var src = File.ReadAllText(ProgramCs());
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
        Assert.Empty(used.Except(registered));
    }

    /// <summary>Die Rechte-Abfrage zählt je NUTZER (wie die Nutzersuche) — je IP hätten sich wieder alle hinter einem NAT
    /// ein Fenster geteilt.</summary>
    [Fact]
    public void PermissionPolicy_IsPartitionedPerUser()
    {
        var src = File.ReadAllText(ProgramCs());
        var start = src.IndexOf(@"AddPolicy(""auth-permissions""", StringComparison.Ordinal);
        Assert.True(start >= 0, "Policy auth-permissions fehlt in Program.cs");
        var next = src.IndexOf("AddPolicy(", start + 1, StringComparison.Ordinal);
        var block = src[start..(next < 0 ? src.Length : next)];
        Assert.Contains("ClaimTypes.NameIdentifier", block);
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
