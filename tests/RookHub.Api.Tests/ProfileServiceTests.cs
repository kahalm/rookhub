using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class ProfileServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ProfileService _profileService;

    public ProfileServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        var logger = NullLogger<ProfileService>.Instance;
        _profileService = new ProfileService(_db, new NoOpTaskQueue(), logger);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserAsync(string username = "testuser")
    {
        var user = new Models.AppUser
        {
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = "hash",
            Profile = new Models.UserProfile()
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task GetProfile_ReturnsProfile()
    {
        var userId = await CreateUserAsync();
        var profile = await _profileService.GetProfileAsync(userId);
        Assert.Equal("testuser", profile.Username);
    }

    [Fact]
    public async Task UpdateProfile_UpdatesFields()
    {
        var userId = await CreateUserAsync();
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            DisplayName = "Test User",
            FideId = "12345",
            ChessComUsername = "testplayer"
        });

        Assert.Equal("Test User", result.DisplayName);
        Assert.Equal("12345", result.FideId);
        Assert.Equal("testplayer", result.ChessComUsername);
    }

    [Fact]
    public async Task GetPublicProfileByUsername_ReturnsReducedProfile_WithoutPii()
    {
        var userId = await CreateUserAsync("alice");
        // Sensible Felder setzen, die NICHT öffentlich erscheinen dürfen.
        await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            DisplayName = "Alice A.",
            FirstName = "Alice",
            LastName = "Anderson",
            FideId = "999",
            ChessResultsId = "777",
        });
        await _profileService.LinkDiscordAsync(userId, "discord-123", "alice#1");

        var profile = await _profileService.GetPublicProfileByUsernameAsync("alice");

        Assert.Equal("alice", profile.Username);
        Assert.Equal("Alice A.", profile.DisplayName);
        Assert.Equal("999", profile.FideId);
        // PublicProfileDto besitzt KEINE DiscordId/ChessResultsId/Klarnamen-Felder (Compile-Garantie),
        // d.h. diese Daten können gar nicht anonym geleakt werden.
    }

    [Fact]
    public async Task UpdateProfile_SetsFirstNameLastName()
    {
        var userId = await CreateUserAsync();
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            FirstName = "Johann",
            LastName = "Huber"
        });

        Assert.Equal("Johann", result.FirstName);
        Assert.Equal("Huber", result.LastName);
    }

    [Fact]
    public async Task GetProfile_ReturnsFirstNameLastName()
    {
        var userId = await CreateUserAsync();
        await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            FirstName = "Maria",
            LastName = "Schmidt"
        });

        var profile = await _profileService.GetProfileAsync(userId);
        Assert.Equal("Maria", profile.FirstName);
        Assert.Equal("Schmidt", profile.LastName);
    }

    [Fact]
    public async Task UpdateProfile_WithPreferences_PersistsAll()
    {
        var userId = await CreateUserAsync();
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            BoardTheme = "green",
            PieceSet = "merida",
            StockfishDepth = 20,
            PuzzleDifficulty = "schwer",
            BookStockfishDepth = 12
        });

        Assert.Equal("green", result.BoardTheme);
        Assert.Equal("merida", result.PieceSet);
        Assert.Equal(20, result.StockfishDepth);
        Assert.Equal("schwer", result.PuzzleDifficulty);
        Assert.Equal(12, result.BookStockfishDepth);
    }

    [Fact]
    public async Task GetProfile_IncludesPreferences()
    {
        var userId = await CreateUserAsync();
        await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            BoardTheme = "blue",
            PieceSet = "fantasy",
            StockfishDepth = 8
        });

        var profile = await _profileService.GetProfileAsync(userId);
        Assert.Equal("blue", profile.BoardTheme);
        Assert.Equal("fantasy", profile.PieceSet);
        Assert.Equal(8, profile.StockfishDepth);
    }

    [Fact]
    public async Task UpdateProfile_NullPreferences_ExistingKept()
    {
        var userId = await CreateUserAsync();
        await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            BoardTheme = "wood",
            PieceSet = "spatial",
            StockfishDepth = 18
        });

        // Update only DisplayName, preferences should stay unchanged
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            DisplayName = "Unchanged Prefs"
        });

        Assert.Equal("wood", result.BoardTheme);
        Assert.Equal("spatial", result.PieceSet);
        Assert.Equal(18, result.StockfishDepth);
        Assert.Equal("Unchanged Prefs", result.DisplayName);
    }

    [Fact]
    public async Task UpdateProfile_StockfishDepthRange_Clamped()
    {
        var userId = await CreateUserAsync();

        // Too high
        var result1 = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            StockfishDepth = 50
        });
        Assert.Equal(24, result1.StockfishDepth);

        // Too low
        var result2 = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            StockfishDepth = 0
        });
        Assert.Equal(1, result2.StockfishDepth);

        // BookStockfishDepth too high
        var result3 = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            BookStockfishDepth = 99
        });
        Assert.Equal(24, result3.BookStockfishDepth);
    }

    [Fact]
    public async Task UpdateProfile_PreferenceOnly_DoesNotTriggerAutoSubscription()
    {
        var userId = await CreateUserAsync();
        var queue = new CountingTaskQueue();
        var service = new ProfileService(_db, queue, NullLogger<ProfileService>.Instance);

        // Identität setzen (ein Trigger erwartet).
        await service.UpdateProfileAsync(userId, new UpdateProfileDto { ChessResultsId = "T1", LastName = "Müller" });
        Assert.Equal(1, queue.EnqueuedCount);

        // Reine Einstellung (kein Identitäts-Feld) -> KEIN weiterer Trigger.
        await service.UpdateProfileAsync(userId, new UpdateProfileDto { BoardTheme = "blue" });
        await service.UpdateProfileAsync(userId, new UpdateProfileDto { StockfishDepth = 20 });
        Assert.Equal(1, queue.EnqueuedCount);
    }

    [Fact]
    public async Task UpdateProfile_IdentityChange_TriggersAutoSubscription()
    {
        var userId = await CreateUserAsync();
        var queue = new CountingTaskQueue();
        var service = new ProfileService(_db, queue, NullLogger<ProfileService>.Instance);

        await service.UpdateProfileAsync(userId, new UpdateProfileDto { ChessResultsId = "T1", LastName = "Müller" });
        Assert.Equal(1, queue.EnqueuedCount);

        // Nachname geändert -> erneuter Trigger.
        await service.UpdateProfileAsync(userId, new UpdateProfileDto { LastName = "Meier" });
        Assert.Equal(2, queue.EnqueuedCount);

        // Gleicher Nachname erneut gesetzt (keine echte Änderung) -> kein Trigger.
        await service.UpdateProfileAsync(userId, new UpdateProfileDto { LastName = "Meier" });
        Assert.Equal(2, queue.EnqueuedCount);

        // Auch eine FideId-Änderung zählt als Identitätsänderung -> Trigger.
        await service.UpdateProfileAsync(userId, new UpdateProfileDto { FideId = "999" });
        Assert.Equal(3, queue.EnqueuedCount);
    }
}
