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
    public async Task SeedAsync_CreatesNewAdmin_WhenAbsent()
    {
        var config = BuildConfig("admin", "secret");

        await AdminSeeder.SeedAsync(_db, config);

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == "admin");
        Assert.NotNull(user);
        Assert.True(user.IsAdmin);
        Assert.True(BCrypt.Net.BCrypt.Verify("secret", user.PasswordHash));
    }

    [Fact]
    public async Task SeedAsync_DoesNotResetExistingPassword()
    {
        // Vom User selbst gewähltes Passwort
        var originalHash = BCrypt.Net.BCrypt.HashPassword("userchosen");
        _db.AppUsers.Add(new AppUser
        {
            Username = "admin",
            Email = "admin@rookhub.local",
            PasswordHash = originalHash,
            IsAdmin = true
        });
        await _db.SaveChangesAsync();

        // Re-Seed mit abweichendem Env-Passwort darf NICHT überschreiben.
        var config = BuildConfig("admin", "envpass");
        await AdminSeeder.SeedAsync(_db, config);

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == "admin");
        Assert.Equal(originalHash, user!.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("userchosen", user.PasswordHash));
        Assert.False(BCrypt.Net.BCrypt.Verify("envpass", user.PasswordHash));
    }

    [Fact]
    public async Task SeedAsync_DoesNotPromoteOrModifyExistingUser()
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

        // Seeder fasst bestehende Accounts nicht an → kein Re-Promote über Env-Config.
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == "admin");
        Assert.False(user!.IsAdmin);
    }

    [Fact]
    public async Task SeedAsync_RefusesWellKnownPlaceholder()
    {
        var config = BuildConfig("admin", "change_me");

        await AdminSeeder.SeedAsync(_db, config);

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == "admin");
        Assert.Null(user);
    }

    [Fact]
    public async Task SeedAsync_NoConfig_NoSeed()
    {
        var config = BuildConfig("", "");

        await AdminSeeder.SeedAsync(_db, config);

        Assert.Empty(_db.AppUsers);
    }
}
