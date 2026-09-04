using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Scope-Prüfung der Extension-Fläche: ein Personal Access Token trägt einen
/// <c>scope</c>-Claim und muss <c>extension</c> sein; JWT-Nutzer (ohne Claim) dürfen immer.
///
/// <para>Sie steckte vorher als <c>if (ScopeGuard() is { } forbid) return forbid;</c> in 17
/// einzelnen Actions — jede neue Action hätte sie vergessen können. Jetzt ist es EIN
/// Klassen-Attribut; getestet wird deshalb (1) das Filter-Verhalten selbst und (2) dass der
/// Controller das Attribut wirklich trägt (die Verdrahtung, die eine Änderung sonst still kippt).</para>
/// </summary>
public class RequireExtensionScopeTests
{
    private static ActionExecutingContext ContextWith(string? scope)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "7") };
        if (scope is not null) claims.Add(new Claim("scope", scope));
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
        };
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(),
            new Dictionary<string, object?>(), controller: new object());
    }

    [Fact]
    public void JwtUser_WithoutScopeClaim_PassesThrough()
    {
        var ctx = ContextWith(null);
        new RequireExtensionScopeAttribute().OnActionExecuting(ctx);
        Assert.Null(ctx.Result);
    }

    [Fact]
    public void Token_WithExtensionScope_PassesThrough()
    {
        var ctx = ContextWith(ApiTokenService.DefaultScope);
        new RequireExtensionScopeAttribute().OnActionExecuting(ctx);
        Assert.Null(ctx.Result);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("other")]
    [InlineData("")]
    public void Token_WithForeignScope_IsForbidden(string scope)
    {
        var ctx = ContextWith(scope);
        new RequireExtensionScopeAttribute().OnActionExecuting(ctx);
        Assert.IsType<ForbidResult>(ctx.Result);
    }

    [Fact]
    public void ExtensionController_CarriesTheFilter()
    {
        // Ohne diese Zeile wäre die Regel für die GANZE Fläche weg — und kein Action-Test würde es
        // merken, weil ein direkt instanziierter Controller ohnehin keine Filter ausführt.
        Assert.NotEmpty(typeof(ExtensionController)
            .GetCustomAttributes<RequireExtensionScopeAttribute>(inherit: true));
    }

    [Fact]
    public void NoControllerKeepsAPrivateScopeGuard()
    {
        // Gegenprobe zur Zusammenlegung: taucht der alte Per-Action-Guard irgendwo wieder auf,
        // gibt es zwei Wahrheiten über denselben Scope — genau die Drift, die vermieden werden soll.
        var guards = typeof(ExtensionController).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.Name == "ScopeGuard")
            .Select(m => m.DeclaringType!.Name)
            .ToList();
        Assert.True(guards.Count == 0, "ScopeGuard() lebt wieder in: " + string.Join(", ", guards));
    }
}
