using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Authorization;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Rechte LIVE (0.589.0): eigene Rollen + Rollen der Gruppen, sofort nach jeder Änderung — nicht erst nach dem
/// nächsten Anmelden (Wunsch 2026-09-28: „das ist doch scheiße — sollte immer wieder neue Infos holen").</summary>
public class PermissionResolverTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public PermissionResolverTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    private async Task<(AppUser user, Group group, Role club)> SeedAsync()
    {
        await RoleSeeder.SeedAsync(_db);
        var user = new AppUser { Username = "fm2200", PasswordHash = "x" };
        var group = new Group { Name = "Schwaz" };
        _db.AppUsers.Add(user);
        _db.Groups.Add(group);
        await _db.SaveChangesAsync();
        var club = await new RoleAdminService(_db).CreateAsync(new CreateRoleDto
        {
            Key = "verein-schwaz", Name = "Verein Schwaz", Permissions = [Permissions.LeagueView, Permissions.LeagueContribute],
        });
        return (user, group, await _db.Roles.SingleAsync(r => r.Id == club.Id));
    }

    [Fact]
    public async Task GroupRole_GivesEveryMemberThePermissions_AtOnce_AndLeavingTakesThemAway()
    {
        var (user, group, club) = await SeedAsync();
        var resolver = new PermissionResolver(_db, _cache);
        var admin = new RoleAdminService(_db);
        await admin.SetGroupRolesAsync(group.Id, new SetUserRolesDto { RoleIds = [club.Id] });

        Assert.False((await resolver.GetAsync(user.Id)).Has(Permissions.LeagueView));    // noch kein Mitglied

        _db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
        await _db.SaveChangesAsync();
        PermissionResolver.InvalidateAll();                                             // wie GroupController.AddMember
        var live = await resolver.GetAsync(user.Id);
        Assert.True(live.Has(Permissions.LeagueView));
        Assert.True(live.Has(Permissions.LeagueContribute));
        Assert.False(live.Has(Permissions.LeagueManage));

        _db.UserGroups.RemoveRange(_db.UserGroups.Where(ug => ug.UserId == user.Id));
        await _db.SaveChangesAsync();
        PermissionResolver.InvalidateAll();
        Assert.False((await resolver.GetAsync(user.Id)).Has(Permissions.LeagueView));
    }

    [Fact]
    public async Task OwnRoles_CountToo_AndAnInvalidationBeatsTheCache()
    {
        var (user, _, club) = await SeedAsync();
        var resolver = new PermissionResolver(_db, _cache);
        Assert.Empty((await resolver.GetAsync(user.Id)).Permissions);
        await new RoleAdminService(_db).SetUserRolesAsync(user.Id, new SetUserRolesDto { RoleIds = [club.Id] });  // leert selbst
        Assert.True((await resolver.GetAsync(user.Id)).Has(Permissions.LeagueContribute));
    }

    [Fact]
    public async Task Handler_DecidesByTheLiveState_NotByAStaleClaim()
    {
        var (user, group, club) = await SeedAsync();
        var resolver = new PermissionResolver(_db, _cache);
        // Token von gestern: trägt league.view, die Rolle ist inzwischen weg.
        var stale = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(PermissionAuthorizationHandler.PermissionClaimType, Permissions.LeagueView)], "test"));
        Assert.False(await Evaluate(stale, Permissions.LeagueView, resolver));

        // Frisch in die Gruppe, die die Rolle trägt: gilt sofort, ohne neues Token.
        await new RoleAdminService(_db).SetGroupRolesAsync(group.Id, new SetUserRolesDto { RoleIds = [club.Id] });
        _db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
        await _db.SaveChangesAsync();
        PermissionResolver.InvalidateAll();
        var fresh = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "test"));
        Assert.True(await Evaluate(fresh, Permissions.LeagueContribute, resolver));
    }

    private static async Task<bool> Evaluate(ClaimsPrincipal user, string permission, PermissionResolver resolver)
    {
        var requirement = new PermissionRequirement(permission);
        var ctx = new AuthorizationHandlerContext([requirement], user, null);
        await new PermissionAuthorizationHandler(resolver).HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    [Fact]
    public async Task GroupRoles_NeverTheAdminRole_NeverEveryone_AndTheRoleListNamesTheGroups()
    {
        var (_, group, club) = await SeedAsync();
        var admin = new RoleAdminService(_db);
        var adminRole = await _db.Roles.SingleAsync(r => r.Key == RoleSeeder.AdminKey);
        await admin.SetGroupRolesAsync(group.Id, new SetUserRolesDto { RoleIds = [club.Id, adminRole.Id] });
        Assert.Equal([club.Id], (await admin.GetGroupRolesAsync(group.Id)).RoleIds);

        var everyone = new Group { Name = "Everyone", IsEveryone = true };
        _db.Groups.Add(everyone);
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            admin.SetGroupRolesAsync(everyone.Id, new SetUserRolesDto { RoleIds = [club.Id] }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => admin.GetGroupRolesAsync(9999));

        Assert.Equal(["Schwaz"], (await admin.ListAsync()).Single(r => r.Id == club.Id).Groups);
        await admin.DeleteAsync(club.Id);                                                // räumt die Gruppenrolle mit ab
        Assert.Empty(_db.GroupRoles);
    }
}
