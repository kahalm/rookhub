using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace RookHub.Api.Authorization;

/// <summary>
/// Erfüllt eine <see cref="PermissionRequirement"/>, wenn der Nutzer entweder die Superuser-Rolle
/// „Admin" trägt (erfüllt IMMER alles — so bleiben umgestellte Endpoints für Admins unverändert
/// erreichbar, auch bevor die Permission-Claims im JWT stehen) ODER einen <c>perm</c>-Claim mit dem
/// geforderten Schlüssel besitzt. Rein claim-basiert (kein DB-Zugriff im Hot-Path).
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    /// <summary>Claim-Typ, unter dem aufgelöste Permissions im JWT landen (ab Phase 3).</summary>
    public const string PermissionClaimType = "perm";

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var user = context.User;
        if (user.IsInRole("Admin")
            || user.HasClaim(PermissionClaimType, requirement.Permission))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
