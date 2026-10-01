using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;

namespace RookHub.Api.Controllers;

public abstract class BaseApiController : ControllerBase
{
    protected int GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (claim is null || !int.TryParse(claim, out var userId))
            throw new UnauthorizedAccessException("User ID claim is missing or invalid.");
        return userId;
    }

    /// <summary>UserId des aktuellen Tokens, oder <c>null</c> wenn nicht (gültig) eingeloggt — für
    /// <c>[AllowAnonymous]</c>-Endpoints, die optional einen eingeloggten Nutzer berücksichtigen.</summary>
    protected int? GetUserIdOrNull()
    {
        // Ohne HttpContext (Controller direkt instanziiert, z. B. in Tests) ist `User` null — dann: anonym.
        var claim = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out var userId) ? userId : null;
    }

    /// <summary>
    /// True, wenn das aktuelle Token aus einer Admin-Impersonation stammt (trägt den <c>imp</c>-Claim).
    /// Actions mit dauerhafter Wirkung sperrt <see cref="DenyWhileImpersonatingAttribute"/> als Ganzes; diese
    /// Abfrage bleibt für Sonderfälle, die nur einen TEIL der Action sperren (E-Mail-Änderung in <c>PUT /api/profile</c>).
    /// </summary>
    protected bool IsImpersonating() => DenyWhileImpersonatingAttribute.IsImpersonating(User);

    /// <summary>Ob der aktuelle Nutzer die Admin-Rolle trägt. Zentral hier, damit alle Controller
    /// dieselbe Prüfung nutzen (statt sie je Controller zu duplizieren).</summary>
    protected bool IsAdmin => User.IsInRole("Admin");

    /// <summary>Dieselbe Prüfung wie <see cref="HasPermissionAttribute"/>, aber im Rumpf einer Action — für Stellen, an
    /// denen das Recht nur einen TEIL der Antwort freischaltet (unveröffentlichte Wochenposts, Push-Bereich „admin") oder
    /// die einen Forbid-Zweig selbst führen. Admin-Rolle erfüllt alles; sonst der Live-Stand des
    /// <see cref="Services.PermissionResolver"/>, ohne ihn (Tests) der <c>perm</c>-Claim (F5-005: vorher hing das am
    /// Admin-Flag, eine Rolle mit dem Recht wirkte dort nicht).</summary>
    protected async Task<bool> HasPermissionAsync(Services.PermissionResolver? resolver, string permission)
    {
        if (User?.IsInRole("Admin") == true) return true;
        if (User?.Identity?.IsAuthenticated != true) return false;
        if (resolver != null)
            return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                && (await resolver.GetAsync(userId)).Has(permission);
        return User.HasClaim(PermissionAuthorizationHandler.PermissionClaimType, permission);
    }
}
