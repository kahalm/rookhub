using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class RoleAdminServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RoleAdminService _svc;

    public RoleAdminServiceTests()
    {
        var o = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(o);
        _svc = new RoleAdminService(_db);
    }
    public void Dispose() => _db.Dispose();

    private async Task SeedAsync() => await RoleSeeder.SeedAsync(_db);

    [Fact]
    public async Task Create_ValidatesPermissions_AndPersists()
    {
        await SeedAsync();
        var role = await _svc.CreateAsync(new CreateRoleDto
        {
            Key = "trainer", Name = "Trainer", Permissions = new() { Permissions.GroupsManage, Permissions.WeeklyPostsManage },
        });
        Assert.Equal("trainer", role.Key);
        Assert.False(role.IsSystem);
        Assert.Equal(new[] { Permissions.GroupsManage, Permissions.WeeklyPostsManage }.OrderBy(x => x),
                     role.Permissions.OrderBy(x => x));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CreateAsync(new CreateRoleDto
        {
            Key = "bad", Name = "Bad", Permissions = new() { "nonsense.permission" },
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CreateAsync(new CreateRoleDto
        {
            Key = "trainer", Name = "Dup", Permissions = new(),   // Key existiert schon
        }));
    }

    [Fact]
    public async Task Update_AdminRolePermissions_AreLocked_ButNameEditable()
    {
        await SeedAsync();
        var admin = (await _svc.ListAsync()).First(r => r.Key == "admin");
        var before = admin.Permissions.Count;

        var updated = await _svc.UpdateAsync(admin.Id, new UpdateRoleDto { Name = "Chef", Permissions = new() });
        Assert.Equal("Chef", updated.Name);
        Assert.Equal(before, updated.Permissions.Count);   // Permissions NICHT geleert (admin = alle)
    }

    [Fact]
    public async Task Delete_SystemRole_Forbidden_CustomRole_Ok()
    {
        await SeedAsync();
        var member = (await _svc.ListAsync()).First(r => r.Key == "member");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.DeleteAsync(member.Id));

        var role = await _svc.CreateAsync(new CreateRoleDto { Key = "temp", Name = "Temp", Permissions = new() });
        await _svc.DeleteAsync(role.Id);
        Assert.DoesNotContain(await _svc.ListAsync(), r => r.Key == "temp");
    }

    [Fact]
    public async Task SetUserRoles_ReplacesNonAdmin_ButNeverTouchesAdminMembership()
    {
        _db.AppUsers.Add(new AppUser { Id = 1, Username = "boss", PasswordHash = "x", IsAdmin = true });
        await _db.SaveChangesAsync();
        await SeedAsync();   // legt admin an + spiegelt IsAdmin → boss in admin-Rolle
        var trainer = await _svc.CreateAsync(new CreateRoleDto { Key = "trainer", Name = "Trainer", Permissions = new() { Permissions.GroupsManage } });
        var adminRoleId = (await _svc.ListAsync()).First(r => r.Key == "admin").Id;

        // Setze auf {trainer, admin} — admin muss ignoriert (aber vorhanden) bleiben, trainer wird gesetzt.
        await _svc.SetUserRolesAsync(1, new SetUserRolesDto { RoleIds = new() { trainer.Id, adminRoleId } });
        var roles = (await _svc.GetUserRolesAsync(1)).RoleIds;
        Assert.Contains(trainer.Id, roles);
        Assert.Contains(adminRoleId, roles);   // admin-Mitgliedschaft (aus IsAdmin) unberührt

        // Leeren → trainer weg, admin bleibt.
        await _svc.SetUserRolesAsync(1, new SetUserRolesDto { RoleIds = new() });
        roles = (await _svc.GetUserRolesAsync(1)).RoleIds;
        Assert.DoesNotContain(trainer.Id, roles);
        Assert.Contains(adminRoleId, roles);
    }
}
