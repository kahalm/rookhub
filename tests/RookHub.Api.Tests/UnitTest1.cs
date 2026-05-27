using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class AuthServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "TestSecretKeyThatIsAtLeast32Characters!",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience"
            })
            .Build();

        _authService = new AuthService(_db, config);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Register_CreatesUserAndReturnsToken()
    {
        var dto = new RegisterDto { Username = "testuser", Email = "test@example.com", Password = "password123" };

        var result = await _authService.RegisterAsync(dto);

        Assert.Equal("testuser", result.Username);
        Assert.NotEmpty(result.Token);
        Assert.True(result.UserId > 0);
        Assert.Single(_db.AppUsers);
    }

    [Fact]
    public async Task Register_DuplicateUsername_Throws()
    {
        var dto = new RegisterDto { Username = "testuser", Email = "test@example.com", Password = "password123" };
        await _authService.RegisterAsync(dto);

        var dto2 = new RegisterDto { Username = "testuser", Email = "test2@example.com", Password = "password123" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _authService.RegisterAsync(dto2));
    }

    [Fact]
    public async Task Register_DuplicateEmail_Throws()
    {
        var dto = new RegisterDto { Username = "user1", Email = "test@example.com", Password = "password123" };
        await _authService.RegisterAsync(dto);

        var dto2 = new RegisterDto { Username = "user2", Email = "test@example.com", Password = "password123" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _authService.RegisterAsync(dto2));
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        await _authService.RegisterAsync(new RegisterDto { Username = "testuser", Email = "test@example.com", Password = "password123" });

        var result = await _authService.LoginAsync(new LoginDto { Username = "testuser", Password = "password123" });

        Assert.Equal("testuser", result.Username);
        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task Login_InvalidPassword_Throws()
    {
        await _authService.RegisterAsync(new RegisterDto { Username = "testuser", Email = "test@example.com", Password = "password123" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginDto { Username = "testuser", Password = "wrongpassword" }));
    }

    [Fact]
    public async Task Login_NonexistentUser_Throws()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.LoginAsync(new LoginDto { Username = "nobody", Password = "password123" }));
    }
}

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
    public async Task GetProfileByUsername_ReturnsProfile()
    {
        await CreateUserAsync("alice");
        var profile = await _profileService.GetProfileByUsernameAsync("alice");
        Assert.Equal("alice", profile.Username);
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
}

public class FriendServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FriendService _friendService;

    public FriendServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _friendService = new FriendService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserAsync(string username)
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
    public async Task SendRequest_CreatesNewFriendship()
    {
        var user1 = await CreateUserAsync("alice");
        var user2 = await CreateUserAsync("bob");

        var friendship = await _friendService.SendRequestAsync(user1, user2);

        Assert.Equal(Models.FriendshipStatus.Pending, friendship.Status);
    }

    [Fact]
    public async Task SendRequest_ToSelf_Throws()
    {
        var user1 = await CreateUserAsync("alice");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _friendService.SendRequestAsync(user1, user1));
    }

    [Fact]
    public async Task AcceptRequest_ChangeStatusToAccepted()
    {
        var user1 = await CreateUserAsync("alice");
        var user2 = await CreateUserAsync("bob");

        var friendship = await _friendService.SendRequestAsync(user1, user2);
        await _friendService.AcceptRequestAsync(friendship.Id, user2);

        var friends = await _friendService.GetFriendsAsync(user1);
        Assert.Single(friends);
        Assert.Equal("bob", friends[0].Username);
    }

    [Fact]
    public async Task DeclineRequest_ChangeStatusToDeclined()
    {
        var user1 = await CreateUserAsync("alice");
        var user2 = await CreateUserAsync("bob");

        var friendship = await _friendService.SendRequestAsync(user1, user2);
        await _friendService.DeclineRequestAsync(friendship.Id, user2);

        var friends = await _friendService.GetFriendsAsync(user1);
        Assert.Empty(friends);
    }

    [Fact]
    public async Task SearchUsers_FindsByUsername()
    {
        var user1 = await CreateUserAsync("alice");
        await CreateUserAsync("bob");
        await CreateUserAsync("charlie");

        var results = await _friendService.SearchUsersAsync("bob", user1);
        Assert.Single(results);
        Assert.Equal("bob", results[0].Username);
    }
}

public class RepertoireServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RepertoireService _repertoireService;

    public RepertoireServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repertoireService = new RepertoireService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserAsync()
    {
        var user = new Models.AppUser
        {
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = "hash"
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task CreateRepertoire_ReturnsNewRepertoire()
    {
        var userId = await CreateUserAsync();
        var result = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto
        {
            Name = "My Opening Book",
            Description = "Sicilian lines",
            IsPublic = false
        });

        Assert.Equal("My Opening Book", result.Name);
        Assert.Equal(0, result.FileCount);
    }

    [Fact]
    public async Task UploadFile_AddsFileToRepertoire()
    {
        var userId = await CreateUserAsync();
        var rep = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto { Name = "Test" });

        var pgnContent = "1. e4 e5 2. Nf3 Nc6 *";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(pgnContent));
        var file = await _repertoireService.UploadFileAsync(rep.Id, userId, "game1.pgn", stream);

        Assert.Equal("game1.pgn", file.FileName);
        Assert.True(file.FileSize > 0);
    }

    [Fact]
    public async Task GetCombinedPgn_CombinesAllFiles()
    {
        var userId = await CreateUserAsync();
        var rep = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto { Name = "Test" });

        var pgn1 = "1. e4 e5 *";
        var pgn2 = "1. d4 d5 *";
        using var s1 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(pgn1));
        using var s2 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(pgn2));
        await _repertoireService.UploadFileAsync(rep.Id, userId, "g1.pgn", s1);
        await _repertoireService.UploadFileAsync(rep.Id, userId, "g2.pgn", s2);

        var combined = await _repertoireService.GetCombinedPgnAsync(rep.Id, userId);
        Assert.Contains("1. e4 e5", combined);
        Assert.Contains("1. d4 d5", combined);
    }

    [Fact]
    public async Task DeleteRepertoire_RemovesFromDb()
    {
        var userId = await CreateUserAsync();
        var rep = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto { Name = "Test" });

        await _repertoireService.DeleteAsync(rep.Id, userId);

        var all = await _repertoireService.GetAllAsync(userId);
        Assert.Empty(all);
    }
}
