using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class AdminSeedTests : IDisposable
{
    private readonly AppDbContext _db;

    public AdminSeedTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private IConfiguration BuildConfig(string username, string password)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ADMIN_USERNAME"] = username,
                ["ADMIN_PASSWORD"] = password
            })
            .Build();
    }

    [Fact]
    public async Task SeedAsync_CreatesNewAdmin()
    {
        var config = BuildConfig("admin", "secret");

        await AdminSeeder.SeedAsync(_db, config);

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == "admin");
        Assert.NotNull(user);
        Assert.True(user.IsAdmin);
        Assert.True(BCrypt.Net.BCrypt.Verify("secret", user.PasswordHash));
    }

    [Fact]
    public async Task SeedAsync_UpdatesExistingPassword()
    {
        _db.AppUsers.Add(new AppUser
        {
            Username = "admin",
            Email = "admin@rookhub.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("old"),
            IsAdmin = true
        });
        await _db.SaveChangesAsync();

        var config = BuildConfig("admin", "newpass");
        await AdminSeeder.SeedAsync(_db, config);

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == "admin");
        Assert.True(BCrypt.Net.BCrypt.Verify("newpass", user!.PasswordHash));
    }

    [Fact]
    public async Task SeedAsync_SetsIsAdmin_WhenExistingUserNotAdmin()
    {
        _db.AppUsers.Add(new AppUser
        {
            Username = "admin",
            Email = "admin@rookhub.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass"),
            IsAdmin = false
        });
        await _db.SaveChangesAsync();

        var config = BuildConfig("admin", "pass");
        await AdminSeeder.SeedAsync(_db, config);

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == "admin");
        Assert.True(user!.IsAdmin);
    }
}
