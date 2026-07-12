using Microsoft.AspNetCore.Authorization;

namespace RookHub.Api.Authorization;

/// <summary>
/// Schützt einen Endpoint/Controller gegen eine feste <see cref="Models.Permissions"/>-Konstante.
/// Erzeugt eine <c>perm:&lt;key&gt;</c>-Policy (aufgelöst vom <see cref="PermissionPolicyProvider"/>),
/// die der <see cref="PermissionAuthorizationHandler"/> gegen Admin-Rolle bzw. perm-Claim prüft.
/// Ersetzt <c>[Authorize(Roles="Admin")]</c> 1:1 (Admin erfüllt weiterhin alles).
/// </summary>
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission) => Policy = PermissionPolicyProvider.Prefix + permission;
}
