using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RookHub.Api.Services;

namespace RookHub.Api.Authorization;

/// <summary>
/// Wird die Anfrage mit einem PERSONAL ACCESS TOKEN gestellt (die Identität trägt dann einen
/// <c>scope</c>-Claim), muss dieser <see cref="Scope"/> sein; JWT-Nutzer (ohne scope-Claim) dürfen,
/// solange <see cref="AllowJwt"/> gilt (Standard). Schützt davor, dass ein Token eines anderen Scopes
/// die Fläche liest — Extension-Fläche (<see cref="ApiTokenService.DefaultScope"/>) wie Engine-Provider
/// (<see cref="ApiTokenService.EngineScope"/>; dort Anlegen/Ändern zusätzlich mit <c>AllowJwt = false</c>).
///
/// <para><b>Warum ein Filter und nicht Zeilen in den Actions:</b> genau so stand es vorher —
/// <c>if (ScopeGuard() is { } forbid) return forbid;</c> in jeder einzelnen Action (Extension, 17-mal),
/// <c>ForeignScope()</c>/<c>NotAProvider()</c> im ExternalEngineController. Das ist keine
/// Verteidigungslinie, sondern eine Einladung: die nächste Action vergisst die Zeile, und niemand
/// sieht es. Als Klassen-Attribut gilt die Regel für ALLES in diesem Controller, auch für die
/// Action, die morgen dazukommt.</para>
///
/// <para>Zweite Schranke bleibt der zentrale <c>PatScopeFenceMiddleware</c> (ein Token mit
/// scope-Claim darf ohnehin nur die Fläche seines Scopes); dieser Filter ist die Prüfung IN der
/// Fläche selbst und damit unabhängig davon, ob der Zaun je umgebaut wird.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireTokenScopeAttribute : Attribute, IActionFilter
{
    public RequireTokenScopeAttribute(string scope) => Scope = scope;

    /// <summary>Der verlangte Token-Scope.</summary>
    public string Scope { get; }

    /// <summary>Ob ein Browser-Login (JWT, kein scope-Claim) durchdarf. Standard: ja.</summary>
    public bool AllowJwt { get; set; } = true;

    /// <summary>Text der 403-Antwort (<c>{ message }</c>); <c>null</c> = leeres 403 (<see cref="ForbidResult"/>).</summary>
    public string? Message { get; set; }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var scope = context.HttpContext.User.FindFirst("scope")?.Value;
        if (scope is null ? AllowJwt : scope == Scope) return;
        context.Result = Message is null
            ? new ForbidResult()
            : new ObjectResult(new { message = Message }) { StatusCode = StatusCodes.Status403Forbidden };
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
