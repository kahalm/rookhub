using Microsoft.AspNetCore.Authorization;

namespace RookHub.Api.Authorization;

/// <summary>Autorisierungs-Anforderung „Nutzer besitzt Permission <see cref="Permission"/>".</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = permission;
}
