using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class RoleSeederTests : IDisposable
{
    private readonly AppDbContext _db;
    public RoleSeederTests()
    {
        var o = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(o);
    }
    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Seed_CreatesSystemRoles_AdminHasAllPermissions_AndMirrorsIsAdmin()
    {
        _db.AppUsers.Add(new AppUser { Id = 1, Username = "boss", PasswordHash = "x", IsAdmin = true });
        _db.AppUsers.Add(new AppUser { Id = 2, Username = "normal", PasswordHash = "x", IsAdmin = false });
        await _db.SaveChangesAsync();

        await RoleSeeder.SeedAsync(_db);

        var admin = await _db.Roles.Include(r => r.Permissions).FirstAsync(r => r.Key == "admin");
        var member = await _db.Roles.FirstAsync(r => r.Key == "member");
        Assert.True(admin.IsSystem);
        Assert.True(member.IsSystem);
        // admin trägt ALLE Permissions
        Assert.Equal(Permissions.All.OrderBy(x => x), admin.Permissions.Select(p => p.Permission).OrderBy(x => x));
        // IsAdmin-User in admin-Rolle, normaler User NICHT
        Assert.True(await _db.UserRoles.AnyAsync(ur => ur.UserId == 1 && ur.RoleId == admin.Id));
        Assert.False(await _db.UserRoles.AnyAsync(ur => ur.UserId == 2));
    }

    [Fact]
    public async Task Seed_IsIdempotent()
    {
        _db.AppUsers.Add(new AppUser { Id = 1, Username = "boss", PasswordHash = "x", IsAdmin = true });
        await _db.SaveChangesAsync();

        await RoleSeeder.SeedAsync(_db);
        await RoleSeeder.SeedAsync(_db);

        Assert.Equal(2, await _db.Roles.CountAsync());                                   // admin+member, nicht verdoppelt
        var admin = await _db.Roles.FirstAsync(r => r.Key == "admin");
        Assert.Equal(Permissions.All.Count, await _db.RolePermissions.CountAsync(rp => rp.RoleId == admin.Id));
        Assert.Equal(1, await _db.UserRoles.CountAsync());                               // eine Mitgliedschaft, nicht doppelt
    }
}
