using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Admin-Entzug über <see cref="AdminService.ToggleAdminAsync"/>: er muss SOFORT wirken. Die Admin-Rolle steht
/// im Token (bis 30/90 Tage) und erfüllt jedes <c>[HasPermission]</c>; die vom <see cref="RoleSeeder"/> gespiegelte
/// System-Rolle „admin" gibt über den Live-Resolver alle Rechte — beides überlebte den Entzug vorher.</summary>
public class AdminServiceTests : IDisposable
{
    private const int ActorId = 990001;

    private readonly AppDbContext _db;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly CapturingLogger<AdminServiceTests> _log = new();

    public AdminServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    private async Task<AppUser> AddUserAsync(string name, bool isAdmin, string stamp)
    {
        var user = new AppUser { Username = name, Email = $"{name}@t.com", PasswordHash = "h", IsAdmin = isAdmin, SecurityStamp = stamp };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private Task<string?> RejectionAsync(int userId, string tokenStamp)
        => JwtTokenGate.RejectionReasonAsync(_db, _cache, userId, tokenStamp, _log, CancellationToken.None);

    [Fact]
    public async Task ToggleAdmin_Demote_RotatesStamp_AndTheOldTokenIsRejectedAtOnce()
    {
        var admin = await AddUserAsync("admin-b", isAdmin: true, stamp: "stamp-before");
        Assert.Null(await RejectionAsync(admin.Id, "stamp-before"));      // Token gilt — und der Zustand liegt jetzt im Cache

        var dto = await new AdminService(_db, _cache).ToggleAdminAsync(admin.Id, ActorId);

        Assert.False(dto.IsAdmin);
        var reloaded = await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == admin.Id);
        Assert.NotEqual("stamp-before", reloaded.SecurityStamp);
        // Sofort, nicht erst nach Ablauf des 60-s-Caches: sonst ruft der Herabgestufte in dieser Minute toggle-admin
        // mit seinem alten Token auf und stuft ein Zweitkonto hoch.
        Assert.Equal("stamp-mismatch", await RejectionAsync(admin.Id, "stamp-before"));
    }

    [Fact]
    public async Task ToggleAdmin_Demote_RemovesTheMirroredAdminRole_SoTheLiveResolverGrantsNothing()
    {
        var admin = await AddUserAsync("admin-b", isAdmin: true, stamp: "s1");
        await RoleSeeder.SeedAsync(_db);                                   // Start: IsAdmin → UserRole „admin"
        var club = await new RoleAdminService(_db).CreateAsync(new CreateRoleDto
        {
            Key = "verein", Name = "Verein", Permissions = [Permissions.LeagueView],
        });
        _db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = club.Id });
        await _db.SaveChangesAsync();
        Assert.True((await PermissionResolver.LoadAsync(_db, admin.Id)).Permissions.Contains(Permissions.UsersManage));

        await new AdminService(_db, _cache).ToggleAdminAsync(admin.Id, ActorId);

        var live = await PermissionResolver.LoadAsync(_db, admin.Id);
        Assert.False(live.IsAdmin);
        Assert.False(live.Has(Permissions.UsersManage));                  // auch mit neuem Token (ohne Admin-Rolle) nichts mehr
        Assert.True(live.Has(Permissions.LeagueView));                     // andere Rollen bleiben
        var adminRoleId = await _db.Roles.Where(r => r.Key == RoleSeeder.AdminKey).Select(r => r.Id).SingleAsync();
        Assert.False(await _db.UserRoles.AnyAsync(ur => ur.UserId == admin.Id && ur.RoleId == adminRoleId));
    }

    [Fact]
    public async Task ToggleAdmin_Promote_LinksTheAdminRole_AndKeepsTheSessions()
    {
        var user = await AddUserAsync("user-c", isAdmin: false, stamp: "s1");
        await RoleSeeder.SeedAsync(_db);

        var dto = await new AdminService(_db, _cache).ToggleAdminAsync(user.Id, ActorId);

        Assert.True(dto.IsAdmin);
        var adminRoleId = await _db.Roles.Where(r => r.Key == RoleSeeder.AdminKey).Select(r => r.Id).SingleAsync();
        Assert.True(await _db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == adminRoleId));
        Assert.Null(await RejectionAsync(user.Id, "s1"));                  // Hochstufen meldet niemanden ab
    }
}
