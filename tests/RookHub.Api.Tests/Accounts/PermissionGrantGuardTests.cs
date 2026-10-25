using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers.Accounts;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Accounts;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Tests.Accounts;

/// <summary>
/// Codereview N9-004 (entschieden 25.10.2026): „nur vergeben, was man selbst hat“. Ein Nicht-Admin mit
/// <c>roles.manage</c> bzw. <c>groups.manage</c> darf über Rollen und Gruppen-Mitgliedschaften keine Rechte weitergeben,
/// die er selbst nicht hat — auch nicht an sich selbst. Admins bleiben frei; Wegnehmen bleibt frei.
/// </summary>
public class PermissionGrantGuardTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RoleAdminService _roles;

    public PermissionGrantGuardTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _roles = new RoleAdminService(_db);
        PermissionResolver.InvalidateAll();
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> UserAsync(string name, bool admin = false)
    {
        var u = new AppUser { Username = name, Email = $"{name}@t.com", PasswordHash = "h", IsAdmin = admin };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    /// <summary>Rolle direkt in der DB (ohne Prüfung) und dem Konto geben.</summary>
    private async Task<int> GiveRoleAsync(int userId, string key, params string[] perms)
    {
        var role = new Role { Key = key, Name = key, Permissions = perms.Select(p => new RolePermission { Permission = p }).ToList() };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();
        _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });
        await _db.SaveChangesAsync();
        return role.Id;
    }

    private async Task<int> RoleAsync(string key, params string[] perms)
    {
        var role = new Role { Key = key, Name = key, Permissions = perms.Select(p => new RolePermission { Permission = p }).ToList() };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();
        return role.Id;
    }

    private GroupController GroupControllerAs(int userId) => new(_db, new TrainingGoalService(_db))
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test")),
            },
        },
    };

    private async Task<int> GroupAsync(string name, params int[] roleIds)
    {
        var g = new Group { Name = name, CreatedAt = DateTime.UtcNow };
        _db.Groups.Add(g);
        await _db.SaveChangesAsync();
        foreach (var r in roleIds) _db.GroupRoles.Add(new GroupRole { GroupId = g.Id, RoleId = r });
        await _db.SaveChangesAsync();
        return g.Id;
    }

    // ── Rollen (roles.manage) ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRole_WithPermissionNotHeld_Forbidden_OwnPermission_Ok()
    {
        var mgr = await UserAsync("mgr");
        await GiveRoleAsync(mgr, "rolemgr", Permissions.RolesManage, Permissions.GroupsManage);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _roles.CreateAsync(
            new CreateRoleDto { Key = "evil", Name = "Evil", Permissions = [Permissions.UsersManage, Permissions.GroupsManage] }, mgr));
        Assert.Equal(ApiErrorCodes.PermissionNotHeld, ex.Code);
        Assert.Contains(Permissions.UsersManage, ex.Message);
        Assert.False(await _db.Roles.AnyAsync(r => r.Key == "evil"));

        var ok = await _roles.CreateAsync(new CreateRoleDto { Key = "trainer", Name = "Trainer", Permissions = [Permissions.GroupsManage] }, mgr);
        Assert.Equal([Permissions.GroupsManage], ok.Permissions);
    }

    [Fact]
    public async Task UpdateRole_AddingPermissionNotHeld_Forbidden_RenameKeepsForeignPermission_Ok()
    {
        var mgr = await UserAsync("mgr");
        await GiveRoleAsync(mgr, "rolemgr", Permissions.RolesManage);
        var foreign = await RoleAsync("books", Permissions.BooksManage);

        // Umbenennen mit unveränderten Rechten vergibt nichts Neues.
        var renamed = await _roles.UpdateAsync(foreign, new UpdateRoleDto { Name = "Bücher", Permissions = [Permissions.BooksManage] }, mgr);
        Assert.Equal("Bücher", renamed.Name);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _roles.UpdateAsync(foreign,
            new UpdateRoleDto { Name = "Bücher", Permissions = [Permissions.BooksManage, Permissions.UsersManage] }, mgr));
        Assert.Equal(ApiErrorCodes.PermissionNotHeld, ex.Code);
        Assert.DoesNotContain(await _db.RolePermissions.Where(p => p.RoleId == foreign).Select(p => p.Permission).ToListAsync(),
            p => p == Permissions.UsersManage);

        // Streichen ist frei.
        var shrunk = await _roles.UpdateAsync(foreign, new UpdateRoleDto { Name = "Bücher", Permissions = [] }, mgr);
        Assert.Empty(shrunk.Permissions);
    }

    [Fact]
    public async Task SetUserRoles_SelfEscalation_Forbidden_AdminAllowed_RemovalFree()
    {
        var mgr = await UserAsync("mgr");
        var own = await GiveRoleAsync(mgr, "rolemgr", Permissions.RolesManage);
        var strong = await RoleAsync("strong", Permissions.UsersManage);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _roles.SetUserRolesAsync(mgr, new SetUserRolesDto { RoleIds = [own, strong] }, mgr));
        Assert.Equal(ApiErrorCodes.PermissionNotHeld, ex.Code);
        Assert.False(await _db.UserRoles.AnyAsync(ur => ur.UserId == mgr && ur.RoleId == strong));

        var boss = await UserAsync("boss", admin: true);
        await _roles.SetUserRolesAsync(mgr, new SetUserRolesDto { RoleIds = [own, strong] }, boss);
        Assert.True(await _db.UserRoles.AnyAsync(ur => ur.UserId == mgr && ur.RoleId == strong));

        // Eine schon vorhandene Rolle, deren Rechte der Verwalter nicht hat, bleibt beim Speichern stehen …
        var other = await UserAsync("other");
        await _roles.SetUserRolesAsync(other, new SetUserRolesDto { RoleIds = [strong] }, boss);
        var lesser = await UserAsync("lesser");
        await GiveRoleAsync(lesser, "lessermgr", Permissions.RolesManage, Permissions.GroupsManage);
        var trainer = await RoleAsync("trainer", Permissions.GroupsManage);
        await _roles.SetUserRolesAsync(other, new SetUserRolesDto { RoleIds = [strong, trainer] }, lesser);
        // … und Wegnehmen ist frei.
        await _roles.SetUserRolesAsync(other, new SetUserRolesDto { RoleIds = [] }, lesser);
        Assert.False(await _db.UserRoles.AnyAsync(ur => ur.UserId == other));
    }

    [Fact]
    public async Task SetGroupRoles_RoleWithPermissionNotHeld_Forbidden()
    {
        var mgr = await UserAsync("mgr");
        await GiveRoleAsync(mgr, "rolemgr", Permissions.RolesManage, Permissions.LeagueView);
        var strong = await RoleAsync("strong", Permissions.UsersManage);
        var view = await RoleAsync("view", Permissions.LeagueView);
        var group = await GroupAsync("Verein");

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _roles.SetGroupRolesAsync(group, new SetUserRolesDto { RoleIds = [strong] }, mgr));
        Assert.Equal(ApiErrorCodes.PermissionNotHeld, ex.Code);

        await _roles.SetGroupRolesAsync(group, new SetUserRolesDto { RoleIds = [view] }, mgr);
        Assert.True(await _db.GroupRoles.AnyAsync(gr => gr.GroupId == group && gr.RoleId == view));
    }

    // ── Gruppen (groups.manage) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddMember_GroupWithRoleNotHeld_Forbidden_AdminAndRolelessGroup_Ok()
    {
        var trainer = await UserAsync("trainer");
        await GiveRoleAsync(trainer, "trainer", Permissions.GroupsManage);
        var strong = await RoleAsync("strong", Permissions.UsersManage);
        var admins = await GroupAsync("Verwaltung", strong);
        var kids = await GroupAsync("Kinder");

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => GroupControllerAs(trainer).AddMember(admins, trainer));
        Assert.Equal(ApiErrorCodes.PermissionNotHeld, ex.Code);
        Assert.False(await _db.UserGroups.AnyAsync(ug => ug.GroupId == admins));

        Assert.IsType<NoContentResult>(await GroupControllerAs(trainer).AddMember(kids, trainer));

        var boss = await UserAsync("boss", admin: true);
        Assert.IsType<NoContentResult>(await GroupControllerAs(boss).AddMember(admins, trainer));
        // Austragen ist frei.
        Assert.IsType<NoContentResult>(await GroupControllerAs(trainer).RemoveMember(admins, trainer));
    }

    /// <summary>Wer in „Jugend“ steht, steht auch in „Verein“ (Jugend ⊂ Verein) und erbt dessen Rolle — auch das zählt.</summary>
    [Fact]
    public async Task AddMember_InheritedRoleOfEnclosingGroup_Forbidden()
    {
        var trainer = await UserAsync("trainer");
        await GiveRoleAsync(trainer, "trainer", Permissions.GroupsManage);
        var strong = await RoleAsync("strong", Permissions.BooksManage);
        var verein = await GroupAsync("Verein", strong);
        var jugend = await GroupAsync("Jugend");
        var boss = await UserAsync("boss", admin: true);
        Assert.IsType<NoContentResult>(await GroupControllerAs(boss).AddInclusion(verein, jugend));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => GroupControllerAs(trainer).AddMember(jugend, trainer));
        Assert.Equal(ApiErrorCodes.PermissionNotHeld, ex.Code);
    }

    [Fact]
    public async Task AddInclusion_IntoGroupWithRoleNotHeld_Forbidden()
    {
        var trainer = await UserAsync("trainer");
        await GiveRoleAsync(trainer, "trainer", Permissions.GroupsManage);
        var strong = await RoleAsync("strong", Permissions.UsersManage);
        var verwaltung = await GroupAsync("Verwaltung", strong);
        var meine = await GroupAsync("Meine");

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => GroupControllerAs(trainer).AddInclusion(verwaltung, meine));
        Assert.Equal(ApiErrorCodes.PermissionNotHeld, ex.Code);
        Assert.False(await _db.GroupInclusions.AnyAsync());
    }
}
