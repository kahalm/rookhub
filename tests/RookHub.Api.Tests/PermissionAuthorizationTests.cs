using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using RookHub.Api.Authorization;

namespace RookHub.Api.Tests;

/// <summary>Kern der RBAC-Durchsetzung: der Handler erfüllt die Anforderung bei Admin-Rolle ODER
/// passendem perm-Claim, sonst nicht.</summary>
public class PermissionAuthorizationTests
{
    private static async Task<bool> Evaluate(ClaimsPrincipal user, string permission)
    {
        var requirement = new PermissionRequirement(permission);
        var ctx = new AuthorizationHandlerContext(new[] { requirement }, user, null);
        await new PermissionAuthorizationHandler().HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    private static ClaimsPrincipal User(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "test"));

    [Fact]
    public async Task AdminRole_SatisfiesEveryPermission()
    {
        var admin = User(new Claim(ClaimTypes.Role, "Admin"));
        Assert.True(await Evaluate(admin, "users.manage"));
        Assert.True(await Evaluate(admin, "anything.at.all"));
    }

    [Fact]
    public async Task MatchingPermClaim_Grants_OtherwiseDenies()
    {
        var user = User(new Claim(PermissionAuthorizationHandler.PermissionClaimType, "books.manage"));
        Assert.True(await Evaluate(user, "books.manage"));
        Assert.False(await Evaluate(user, "users.manage"));   // andere Permission → verweigert
    }

    [Fact]
    public async Task NoRoleNoPerm_Denies()
        => Assert.False(await Evaluate(User(new Claim(ClaimTypes.Name, "bob")), "ci.view"));

    [Fact]
    public async Task AnyOf_OnePermClaimSuffices_NoneDenies()
    {
        // F5-005-Nachlauf: [HasPermission(a, b, …)] = „eines von".
        var user = User(new Claim(PermissionAuthorizationHandler.PermissionClaimType, "menu.manage"));
        Assert.True(await Evaluate(user, "groups.manage|menu.manage"));
        Assert.False(await Evaluate(user, "groups.manage|catalog.manage"));
        Assert.Equal(["groups.manage", "menu.manage"], new PermissionRequirement("groups.manage|menu.manage").AnyOf);
    }

    [Fact]
    public void HasPermission_SeveralRights_JoinsIntoOnePolicy()
        => Assert.Equal("perm:groups.manage|menu.manage|roles.manage",
            new HasPermissionAttribute("groups.manage", "menu.manage", "roles.manage").Policy);

    [Fact]
    public async Task PolicyProvider_BuildsPermPolicy_AndDelegatesOthers()
    {
        var opts = Microsoft.Extensions.Options.Options.Create(new AuthorizationOptions());
        var provider = new PermissionPolicyProvider(opts);
        var policy = await provider.GetPolicyAsync("perm:groups.manage");
        Assert.NotNull(policy);
        Assert.Contains(policy!.Requirements, r => r is PermissionRequirement pr && pr.Permission == "groups.manage");
        // Nicht-perm-Policy → an den Default delegiert (existiert nicht → null, kein perm-Requirement)
        Assert.Null(await provider.GetPolicyAsync("some-other-policy"));
    }
}
