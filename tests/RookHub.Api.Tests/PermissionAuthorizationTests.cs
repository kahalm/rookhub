using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>R1-004: <c>AdminOnly</c> erfüllt nur das LIVE-Admin-Flag — ein frisch ernanntes Konto mit altem Token (ohne
    /// Admin-Rolle) kommt durch, eine Rolle mit allen vergebbaren Rechten nicht; vergeben lässt es sich nicht.</summary>
    [Fact]
    public async Task AdminOnly_NurDasLiveAdminFlag_keineRolle()
    {
        await using var db = new RookHub.Api.Data.AppDbContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<RookHub.Api.Data.AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var promoted = new RookHub.Api.Models.AppUser { Username = "neu-admin", PasswordHash = "x", IsAdmin = true };
        var plain = new RookHub.Api.Models.AppUser { Username = "mod", PasswordHash = "x" };
        db.AppUsers.AddRange(promoted, plain);
        var role = new RookHub.Api.Models.Role { Key = "alles", Name = "Alles" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        foreach (var perm in RookHub.Api.Models.Permissions.All)
            db.RolePermissions.Add(new RookHub.Api.Models.RolePermission { RoleId = role.Id, Permission = perm });
        db.UserRoles.Add(new RookHub.Api.Models.UserRole { UserId = plain.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
        RookHub.Api.Services.Accounts.PermissionResolver.InvalidateAll();
        var resolver = new RookHub.Api.Services.Accounts.PermissionResolver(db,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

        async Task<bool> Live(int id)
        {
            var ctx = new AuthorizationHandlerContext(new[] { new PermissionRequirement(RookHub.Api.Models.Permissions.AdminOnly) },
                User(new Claim(ClaimTypes.NameIdentifier, id.ToString())), null);
            await new PermissionAuthorizationHandler(resolver).HandleAsync(ctx);
            return ctx.HasSucceeded;
        }

        Assert.True(await Live(promoted.Id));   // Token ohne Admin-Rolle, Flag schon gesetzt
        Assert.False(await Live(plain.Id));     // alle vergebbaren Rechte, aber kein Admin
        Assert.DoesNotContain(RookHub.Api.Models.Permissions.AdminOnly, RookHub.Api.Models.Permissions.All);
    }

    /// <summary>R1-004: Controller prüfen Rechte über [HasPermission] (Live-Stand), nicht über die Rolle im Token.</summary>
    [Fact]
    public void Controllers_nutzenKeinAuthorizeRoles()
    {
        var offenders = typeof(RookHub.Api.Controllers.BaseApiController).Assembly.GetTypes()
            .Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => new System.Reflection.MemberInfo[] { t }.Concat(t.GetMethods(System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                .Where(m => m.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
                    .Any(a => !string.IsNullOrEmpty(a.Roles)))
                .Select(m => m is Type ? t.Name : $"{t.Name}.{m.Name}"))
            .ToList();
        Assert.True(offenders.Count == 0, "[Authorize(Roles=…)] statt [HasPermission]: " + string.Join(", ", offenders));
    }
}
