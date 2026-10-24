using Microsoft.AspNetCore.Authorization;

namespace RookHub.Api.Authorization;

/// <summary>Autorisierungs-Anforderung „Nutzer besitzt Permission <see cref="Permission"/>" — oder, mit
/// <see cref="Separator"/> verbunden (<c>a|b</c>), „eines von <see cref="AnyOf"/>" (F5-005-Nachlauf).</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    /// <summary>Trennt die Alternativen im Policy-Namen; Permission-Schlüssel enthalten ihn nie.</summary>
    public const char Separator = '|';

    /// <summary>Wie im Policy-Namen (bei mehreren Rechten die verbundene Form).</summary>
    public string Permission { get; }
    /// <summary>Die Rechte, von denen EINES genügt (bei einem einzelnen Recht genau dieses).</summary>
    public IReadOnlyList<string> AnyOf { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
        AnyOf = permission.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
