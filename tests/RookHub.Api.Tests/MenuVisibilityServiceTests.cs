using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class MenuVisibilityServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly MenuVisibilityService _svc;

    public MenuVisibilityServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _svc = new MenuVisibilityService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserAsync(string name = "alice")
    {
        var u = new AppUser { Username = name, Email = name + "@x.com", PasswordHash = "h" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    private async Task<int> CreateGroupAsync(string name, params int[] memberUserIds)
    {
        var g = new Group { Name = name, CreatedAt = DateTime.UtcNow };
        _db.Groups.Add(g);
        await _db.SaveChangesAsync();
        foreach (var uid in memberUserIds)
            _db.UserGroups.Add(new UserGroup { UserId = uid, GroupId = g.Id });
        await _db.SaveChangesAsync();
        return g.Id;
    }

    [Fact]
    public async Task GetConfig_WithoutOverrides_ReturnsRegistryDefaults()
    {
        var config = await _svc.GetConfigAsync();

        Assert.Equal(MenuRegistry.Items.Count, config.Count);
        Assert.Equal(MenuVisibilityLevel.All, config.First(c => c.Key == "puzzles").Level);
        Assert.Equal(MenuVisibilityLevel.Registered, config.First(c => c.Key == "repertoires").Level);
        Assert.All(config, c => Assert.Empty(c.GroupIds));
    }

    [Fact]
    public async Task SaveConfig_PersistsLevelAndGroups_AndIgnoresUnknownKeys()
    {
        var gid = await CreateGroupAsync("Coaches");

        await _svc.SaveConfigAsync(new List<MenuItemConfigDto>
        {
            new() { Key = "repertoires", Level = MenuVisibilityLevel.Groups, GroupIds = new() { gid } },
            new() { Key = "analysis", Level = MenuVisibilityLevel.Admin },
            new() { Key = "does-not-exist", Level = MenuVisibilityLevel.All },
        });

        var config = await _svc.GetConfigAsync();
        var rep = config.First(c => c.Key == "repertoires");
        Assert.Equal(MenuVisibilityLevel.Groups, rep.Level);
        Assert.Equal(new[] { gid }, rep.GroupIds);
        Assert.Equal(MenuVisibilityLevel.Admin, config.First(c => c.Key == "analysis").Level);
        // Unbekannter Key wurde nicht angelegt.
        Assert.DoesNotContain(config, c => c.Key == "does-not-exist");
    }

    [Fact]
    public async Task SaveConfig_ChangingAwayFromGroups_ClearsGroupRows()
    {
        var gid = await CreateGroupAsync("Coaches");
        await _svc.SaveConfigAsync(new List<MenuItemConfigDto>
        {
            new() { Key = "repertoires", Level = MenuVisibilityLevel.Groups, GroupIds = new() { gid } },
        });
        await _svc.SaveConfigAsync(new List<MenuItemConfigDto>
        {
            new() { Key = "repertoires", Level = MenuVisibilityLevel.Registered, GroupIds = new() { gid } },
        });

        Assert.Empty(await _db.MenuItemGroupAccesses.ToListAsync());
        Assert.Empty((await _svc.GetConfigAsync()).First(c => c.Key == "repertoires").GroupIds);
    }

    [Fact]
    public async Task GetVisibleKeys_Anonymous_OnlyAllLevel()
    {
        var keys = await _svc.GetVisibleKeysAsync(userId: null, isAdmin: false);

        Assert.Contains("puzzles", keys);   // default All
        Assert.Contains("analysis", keys);  // default All
        Assert.Contains("help", keys);      // default All
        Assert.DoesNotContain("repertoires", keys); // default Registered
        Assert.DoesNotContain("dashboard", keys);
    }

    [Fact]
    public async Task GetVisibleKeys_RegisteredUser_SeesAllAndRegistered_NotGroupNorAdmin()
    {
        var uid = await CreateUserAsync();
        await _svc.SaveConfigAsync(new List<MenuItemConfigDto>
        {
            new() { Key = "weekly", Level = MenuVisibilityLevel.Admin },
            new() { Key = "courses", Level = MenuVisibilityLevel.Groups, GroupIds = new() },
        });

        var keys = await _svc.GetVisibleKeysAsync(uid, isAdmin: false);

        Assert.Contains("repertoires", keys); // Registered
        Assert.Contains("puzzles", keys);     // All
        Assert.DoesNotContain("weekly", keys); // Admin
        Assert.DoesNotContain("courses", keys); // Groups, kein Gruppen-Match
    }

    [Fact]
    public async Task GetVisibleKeys_GroupMember_SeesGroupGatedItem()
    {
        var uid = await CreateUserAsync();
        var gid = await CreateGroupAsync("Coaches", uid);
        await _svc.SaveConfigAsync(new List<MenuItemConfigDto>
        {
            new() { Key = "courses", Level = MenuVisibilityLevel.Groups, GroupIds = new() { gid } },
        });

        var member = await _svc.GetVisibleKeysAsync(uid, isAdmin: false);
        Assert.Contains("courses", member);

        var other = await CreateUserAsync("bob");
        var nonMember = await _svc.GetVisibleKeysAsync(other, isAdmin: false);
        Assert.DoesNotContain("courses", nonMember);
    }

    [Fact]
    public async Task GetVisibleKeys_Admin_SeesEverythingIncludingAdminAndEmptyGroups()
    {
        await _svc.SaveConfigAsync(new List<MenuItemConfigDto>
        {
            new() { Key = "weekly", Level = MenuVisibilityLevel.Admin },
            new() { Key = "courses", Level = MenuVisibilityLevel.Groups, GroupIds = new() },
        });

        var keys = await _svc.GetVisibleKeysAsync(userId: 999, isAdmin: true);

        Assert.Equal(MenuRegistry.Items.Count, keys.Count);
        Assert.Contains("weekly", keys);
        Assert.Contains("courses", keys);
    }
}
