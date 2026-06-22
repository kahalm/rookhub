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

    private async Task<int> CreateUserWithPasswordAsync(string username, string password)
    {
        var user = new Models.AppUser
        {
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsAdmin = true,
            Profile = new Models.UserProfile { DisplayName = "Real Name", FideId = "12345", DiscordId = "d1", DiscordUsername = "real#1" }
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task DeleteAccount_WrongPassword_Throws_AndKeepsData()
    {
        var id = await CreateUserWithPasswordAsync("delme1", "secret123");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _profileService.DeleteAccountAsync(id, "wrong-password"));
        var user = await _db.AppUsers.Include(u => u.Profile).FirstAsync(u => u.Id == id);
        Assert.Null(user.DeletedAt);
        Assert.Equal("Real Name", user.Profile!.DisplayName);
    }

    [Fact]
    public async Task DeleteAccount_CorrectPassword_Anonymizes_RemovesPersonal_KeepsStats()
    {
        var id = await CreateUserWithPasswordAsync("delme2", "secret123");
        var other = await CreateUserWithPasswordAsync("frienduser", "x");
        _db.Friendships.Add(new Models.Friendship { RequesterId = id, AddresseeId = other, Status = Models.FriendshipStatus.Accepted });
        _db.EndlessSessions.Add(new Models.EndlessSession { UserId = id, Timestamp = 1, TotalSolved = 7 });
        _db.UserApiTokens.Add(new Models.UserApiToken { UserId = id, Name = "ext", TokenHash = "h", Prefix = "rkh_abc", Scope = "extension" });
        await _db.SaveChangesAsync();

        await _profileService.DeleteAccountAsync(id, "secret123");

        var user = await _db.AppUsers.Include(u => u.Profile).FirstAsync(u => u.Id == id);
        // Identität anonymisiert + Login gesperrt
        Assert.NotNull(user.DeletedAt);
        Assert.Equal($"deleted_{id}", user.Username);
        Assert.Contains("@deleted.invalid", user.Email);
        Assert.False(user.IsAdmin);
        Assert.False(BCrypt.Net.BCrypt.Verify("secret123", user.PasswordHash));
        // PII entfernt
        Assert.Null(user.Profile!.DisplayName);
        Assert.Null(user.Profile.FideId);
        Assert.Null(user.Profile.DiscordId);
        // persönliche Verknüpfung weg
        Assert.False(await _db.Friendships.AnyAsync(f => f.RequesterId == id || f.AddresseeId == id));
        // Statistik bleibt (anonym, unter der UserId)
        Assert.True(await _db.EndlessSessions.AnyAsync(s => s.UserId == id && s.TotalSolved == 7));
        // API-Tokens widerrufen (kein Zugang nach Löschung)
        Assert.False(await _db.UserApiTokens.AnyAsync(t => t.UserId == id));
    }

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

    [Fact]
    public async Task UpdateProfile_SetsEmail_NormalizedLowercaseTrimmed()
    {
        var userId = await CreateUserAsync();
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto { Email = "  New.Mail@Example.COM  " });
        Assert.Equal("new.mail@example.com", result.Email);
        Assert.Equal("new.mail@example.com", (await _db.AppUsers.FindAsync(userId))!.Email);
    }

    [Fact]
    public async Task UpdateProfile_EmptyEmail_ClearsEmail()
    {
        var userId = await CreateUserAsync(); // startet mit testuser@example.com
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto { Email = "" });
        Assert.Null(result.Email);
        Assert.Null((await _db.AppUsers.FindAsync(userId))!.Email);
    }

    [Fact]
    public async Task UpdateProfile_NullEmail_LeavesEmailUnchanged()
    {
        var userId = await CreateUserAsync();
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto { DisplayName = "X" });
        Assert.Equal("testuser@example.com", result.Email);
    }

    [Fact]
    public async Task UpdateProfile_InvalidEmail_Throws()
    {
        var userId = await CreateUserAsync();
        await Assert.ThrowsAsync<ArgumentException>(
            () => _profileService.UpdateProfileAsync(userId, new UpdateProfileDto { Email = "not-an-email" }));
    }

    [Fact]
    public async Task UpdateProfile_DuplicateEmail_Throws()
    {
        await CreateUserAsync("alice");          // alice@example.com
        var bobId = await CreateUserAsync("bob"); // bob@example.com
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _profileService.UpdateProfileAsync(bobId, new UpdateProfileDto { Email = "ALICE@example.com" }));
    }

    [Fact]
    public async Task UpdateProfile_SameEmailAsOwn_Succeeds()
    {
        var userId = await CreateUserAsync(); // testuser@example.com
        var result = await _profileService.UpdateProfileAsync(userId, new UpdateProfileDto { Email = "testuser@example.com" });
        Assert.Equal("testuser@example.com", result.Email);
    }
}
