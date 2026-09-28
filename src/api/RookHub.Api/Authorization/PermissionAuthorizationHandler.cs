using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using RookHub.Api.Services;

namespace RookHub.Api.Authorization;

/// <summary>
/// Erfüllt eine <see cref="PermissionRequirement"/>, wenn der Nutzer entweder die Superuser-Rolle
/// „Admin" trägt (erfüllt IMMER alles — so bleiben umgestellte Endpoints für Admins unverändert
/// erreichbar, auch bevor die Permission-Claims im JWT stehen) ODER einen <c>perm</c>-Claim mit dem
/// geforderten Schlüssel besitzt.
///
/// <para>Seit 0.589.0 entscheidet der LIVE-Stand (<see cref="PermissionResolver"/>: eigene Rollen + Gruppenrollen, 60 s
/// gespeichert, bei jeder Änderung verworfen), nicht mehr der Claim im Token — der stand bis zum nächsten Anmelden, und
/// ein entzogenes Recht galt so bis zu 30 Tage weiter. Ohne Resolver (Tests) bleibt der Claim der Maßstab.</para>
/// </summary>
public sealed class PermissionAuthorizationHandler(PermissionResolver? resolver = null) : AuthorizationHandler<PermissionRequirement>
{
    /// <summary>Claim-Typ, unter dem aufgelöste Permissions im JWT landen (ab Phase 3).</summary>
    public const string PermissionClaimType = "perm";

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var user = context.User;
        if (user.IsInRole("Admin")) { context.Succeed(requirement); return; }
        if (resolver != null && user.Identity?.IsAuthenticated == true
            && int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            if ((await resolver.GetAsync(userId)).Has(requirement.Permission)) context.Succeed(requirement);
            return;
        }
        if (user.HasClaim(PermissionClaimType, requirement.Permission)) context.Succeed(requirement);
    }
}
