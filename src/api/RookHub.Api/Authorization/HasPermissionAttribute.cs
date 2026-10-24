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

    /// <summary>„Eines von" (F5-005-Nachlauf): erfüllt, wenn der Nutzer mindestens eines der Rechte hat — für Endpunkte,
    /// die mehrere Oberflächen mit verschiedenen Rechten bedienen (z. B. die Gruppen-Auswahlliste).</summary>
    public HasPermissionAttribute(string permission, string orPermission, params string[] orMore)
        => Policy = PermissionPolicyProvider.Prefix
            + string.Join(PermissionRequirement.Separator, new[] { permission, orPermission }.Concat(orMore));
}
