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
/// Die Scope-Prüfung für Personal Access Tokens: ein PAT trägt einen <c>scope</c>-Claim und muss
/// den verlangten Scope haben; JWT-Nutzer (ohne Claim) dürfen, außer <c>AllowJwt = false</c>.
///
/// <para>Sie steckte vorher als <c>if (ScopeGuard() is { } forbid) return forbid;</c> in 17
/// einzelnen Extension-Actions und als <c>ForeignScope()</c>/<c>NotAProvider()</c> in vier
/// Engine-Actions — jede neue Action hätte sie vergessen können. Jetzt ist es EIN Attribut
/// (Codereview 2026-09-29, A1-016); getestet wird deshalb (1) das Filter-Verhalten selbst und
/// (2) dass die Controller das Attribut wirklich tragen (die Verdrahtung, die eine Änderung sonst
/// still kippt).</para>
/// </summary>
public class RequireTokenScopeTests
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
        new RequireTokenScopeAttribute(ApiTokenService.DefaultScope).OnActionExecuting(ctx);
        Assert.Null(ctx.Result);
    }

    [Fact]
    public void Token_WithExtensionScope_PassesThrough()
    {
        var ctx = ContextWith(ApiTokenService.DefaultScope);
        new RequireTokenScopeAttribute(ApiTokenService.DefaultScope).OnActionExecuting(ctx);
        Assert.Null(ctx.Result);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("other")]
    [InlineData("engine")]
    [InlineData("")]
    public void Token_WithForeignScope_IsForbidden(string scope)
    {
        var ctx = ContextWith(scope);
        new RequireTokenScopeAttribute(ApiTokenService.DefaultScope).OnActionExecuting(ctx);
        Assert.IsType<ForbidResult>(ctx.Result);
    }

    [Fact]
    public void WithMessage_ForbidsWith403AndTheMessage_AndAllowJwtFalse_RejectsTheBrowserLogin()
    {
        var foreign = ContextWith(ApiTokenService.DefaultScope);
        new RequireTokenScopeAttribute(ApiTokenService.EngineScope) { Message = "API token scope 'engine' required" }
            .OnActionExecuting(foreign);
        var result = Assert.IsType<ObjectResult>(foreign.Result);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(new { message = "API token scope 'engine' required" }),
            System.Text.Json.JsonSerializer.Serialize(result.Value));

        var jwt = ContextWith(null);
        new RequireTokenScopeAttribute(ApiTokenService.EngineScope) { AllowJwt = false, Message = "x" }.OnActionExecuting(jwt);
        Assert.Equal(403, Assert.IsType<ObjectResult>(jwt.Result).StatusCode);

        var provider = ContextWith(ApiTokenService.EngineScope);
        new RequireTokenScopeAttribute(ApiTokenService.EngineScope) { AllowJwt = false, Message = "x" }.OnActionExecuting(provider);
        Assert.Null(provider.Result);
    }

    [Fact]
    public void ExtensionController_CarriesTheFilter()
    {
        // Ohne diese Zeile wäre die Regel für die GANZE Fläche weg — und kein Action-Test würde es
        // merken, weil ein direkt instanziierter Controller ohnehin keine Filter ausführt.
        Assert.Contains(typeof(ExtensionController).GetCustomAttributes<RequireTokenScopeAttribute>(inherit: true),
            a => a.Scope == ApiTokenService.DefaultScope && a.AllowJwt);
    }

    [Fact]
    public void ExternalEngineController_CarriesTheFilter_RegistrationOnlyForTheProvider()
    {
        Assert.Contains(typeof(ExternalEngineController).GetCustomAttributes<RequireTokenScopeAttribute>(inherit: true),
            a => a.Scope == ApiTokenService.EngineScope && a.AllowJwt);
        foreach (var action in new[] { nameof(ExternalEngineController.Create), nameof(ExternalEngineController.Update) })
            Assert.Contains(typeof(ExternalEngineController).GetMethod(action)!.GetCustomAttributes<RequireTokenScopeAttribute>(),
                a => a.Scope == ApiTokenService.EngineScope && !a.AllowJwt);
    }

    [Fact]
    public void NoControllerKeepsAPrivateScopeGuard()
    {
        // Gegenprobe zur Zusammenlegung: taucht der alte Per-Action-Guard irgendwo wieder auf,
        // gibt es zwei Wahrheiten über denselben Scope — genau die Drift, die vermieden werden soll.
        var guards = typeof(ExtensionController).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.Name is "ScopeGuard" or "ForeignScope" or "NotAProvider")
            .Select(m => m.DeclaringType!.Name)
            .ToList();
        Assert.True(guards.Count == 0, "Privater Scope-Guard lebt wieder in: " + string.Join(", ", guards));
    }
}
