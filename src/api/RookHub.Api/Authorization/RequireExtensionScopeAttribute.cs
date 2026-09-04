using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RookHub.Api.Services;

namespace RookHub.Api.Authorization;

/// <summary>
/// Wird die Anfrage mit einem PERSONAL ACCESS TOKEN gestellt (die Identität trägt dann einen
/// <c>scope</c>-Claim), muss dieser <see cref="ApiTokenService.DefaultScope"/> sein; JWT-Nutzer
/// (ohne scope-Claim) dürfen immer. Schützt davor, dass ein später dazukommender Token-Scope
/// versehentlich die Extension-Fläche liest.
///
/// <para><b>Warum ein Filter und nicht 17 Zeilen in den Actions:</b> genau so stand es vorher —
/// <c>if (ScopeGuard() is { } forbid) return forbid;</c> in jeder einzelnen Action, samt
/// <c>forbid2/3/4</c>, wo mehrere Guards in einer Methode landeten. Das ist keine
/// Verteidigungslinie, sondern eine Einladung: die nächste Action vergisst die Zeile, und niemand
/// sieht es. Als Klassen-Attribut gilt die Regel für ALLES in diesem Controller, auch für die
/// Action, die morgen dazukommt.</para>
///
/// <para>Zweite Schranke bleibt der zentrale <c>PatScopeFenceMiddleware</c> (ein Token mit
/// scope-Claim darf ohnehin nur <c>/api/extension</c>); dieser Filter ist die Prüfung IN der
/// Fläche selbst und damit unabhängig davon, ob der Zaun je umgebaut wird.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireExtensionScopeAttribute : Attribute, IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var scope = context.HttpContext.User.FindFirst("scope")?.Value;
        if (scope is null) return;                              // JWT-Nutzer
        if (scope == ApiTokenService.DefaultScope) return;      // erlaubter Token-Scope
        context.Result = new ForbidResult();
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
