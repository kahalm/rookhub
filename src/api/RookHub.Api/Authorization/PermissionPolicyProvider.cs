using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace RookHub.Api.Authorization;

/// <summary>
/// Erzeugt Autorisierungs-Policies für <c>perm:&lt;key&gt;</c>-Namen ON THE FLY (kein Vorab-Registrieren
/// jeder Permission nötig). <see cref="HasPermissionAttribute"/> setzt solche Policy-Namen; alles andere
/// (Default-/Fallback-Policy, benannte Nicht-perm-Policies) delegiert an den Standard-Provider.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    public const string Prefix = "perm:";
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        => _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            var permission = policyName[Prefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        return _fallback.GetPolicyAsync(policyName);
    }
}
