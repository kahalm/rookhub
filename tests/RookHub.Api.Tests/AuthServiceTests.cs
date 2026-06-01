using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    [Fact]
    public async Task Login_UsernameIsCaseInsensitive()
    {
        await _authService.RegisterAsync(new RegisterDto { Username = "TestUser", Email = "t@example.com", Password = "password123" });

        var result = await _authService.LoginAsync(new LoginDto { Username = "testuser", Password = "password123" });

        Assert.Equal("TestUser", result.Username);
        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task Register_UsernameCollisionIsCaseInsensitive_Throws()
    {
        await _authService.RegisterAsync(new RegisterDto { Username = "Admin", Email = "a@example.com", Password = "password123" });

        var dup = new RegisterDto { Username = "admin", Email = "other@example.com", Password = "password123" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _authService.RegisterAsync(dup));
    }
}
