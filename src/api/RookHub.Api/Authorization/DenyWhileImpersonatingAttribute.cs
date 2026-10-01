using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RookHub.Api.Authorization;

/// <summary>
/// Sperrt eine Action, solange das Token aus einer Admin-Impersonation stammt (<c>imp</c>-Claim): 403 mit
/// <c>{ message }</c>. Gehört an alles mit DAUERHAFTER Wirkung über die Sitzung hinaus — Passwort und API-Token,
/// Identitätsbindungen (Discord, Verein, Push-Subscription), Kontolöschung, Anmelde-Übergabe —, damit ein Admin
/// fremde Konten nicht dauerhaft verändert oder Zugänge in fremdem Namen erzeugt.
///
/// <para><b>Warum ein Attribut statt <c>if (IsImpersonating()) return …</c> in jeder Action:</b> dieselbe
/// Begründung wie bei <see cref="RequireTokenScopeAttribute"/> — die nächste Action vergisst die Zeile, und
/// niemand sieht es (so fehlte sie an <c>push/subscribe</c>: der Admin-Browser bekam danach die Pushes des
/// Zielkontos). Welche Actions es tragen MÜSSEN, hält <c>ImpersonationGuardTests</c> als Liste fest.</para>
///
/// <para>Sonderfall bleibt <c>PUT /api/profile</c>: dort zählt die ÄNDERUNG der E-Mail, nicht die Anwesenheit
/// (<see cref="IsImpersonating"/> direkt).</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DenyWhileImpersonatingAttribute : Attribute, IActionFilter
{
    /// <summary>Antworttext — derselbe, den die Actions vorher selbst zurückgaben.</summary>
    public const string Message = "Not allowed while impersonating another user.";

    /// <summary>True, wenn das Token aus einer Admin-Impersonation stammt (trägt den <c>imp</c>-Claim).</summary>
    public static bool IsImpersonating(ClaimsPrincipal? user) => user?.FindFirst("imp") is not null;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (IsImpersonating(context.HttpContext.User))
            context.Result = new ObjectResult(new { message = Message }) { StatusCode = StatusCodes.Status403Forbidden };
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
