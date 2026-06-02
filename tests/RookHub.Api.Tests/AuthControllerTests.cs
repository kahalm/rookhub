using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class AuthControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AuthService _authService;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "TestSecretKeyThatIsLongEnoughForHmacSha256!!",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience"
            })
            .Build();

        _authService = new AuthService(_db, config, NullLogger<AuthService>.Instance);
        _controller = new AuthController(_authService);
    }

    public void Dispose() => _db.Dispose();

    // ---- Register ----

    [Fact]
    public async Task Register_ReturnsOk_WithToken()
    {
        var dto = new RegisterDto { Username = "newuser", Email = "new@test.com", Password = "Password1!" };

        var result = await _controller.Register(dto);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = okResult.Value as AuthResponseDto;
        Assert.NotNull(response);
        Assert.Equal("newuser", response.Username);
        Assert.False(string.IsNullOrEmpty(response.Token));
    }

    [Fact]
    public async Task Register_ReturnsConflict_WhenUsernameExists()
    {
        _db.AppUsers.Add(new AppUser
        {
            Username = "existing",
            Email = "exist@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass")
        });
        await _db.SaveChangesAsync();

        var dto = new RegisterDto { Username = "existing", Email = "new@test.com", Password = "Password1!" };

        var result = await _controller.Register(dto);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Register_ReturnsConflict_WhenEmailExists()
    {
        _db.AppUsers.Add(new AppUser
        {
            Username = "user1",
            Email = "taken@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass")
        });
        await _db.SaveChangesAsync();

        var dto = new RegisterDto { Username = "user2", Email = "taken@test.com", Password = "Password1!" };

        var result = await _controller.Register(dto);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    // ---- Login ----

    [Fact]
    public async Task Login_ReturnsOk_WithValidCredentials()
    {
        // Register first
        await _controller.Register(new RegisterDto
        {
            Username = "loginuser",
            Email = "login@test.com",
            Password = "Password1!"
        });

        var result = await _controller.Login(new LoginDto
        {
            Username = "loginuser",
            Password = "Password1!"
        });

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = okResult.Value as AuthResponseDto;
        Assert.NotNull(response);
        Assert.Equal("loginuser", response.Username);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WithWrongPassword()
    {
        await _controller.Register(new RegisterDto
        {
            Username = "loginuser",
            Email = "login@test.com",
            Password = "Password1!"
        });

        var result = await _controller.Login(new LoginDto
        {
            Username = "loginuser",
            Password = "WrongPassword1!"
        });

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WithNonexistentUser()
    {
        var result = await _controller.Login(new LoginDto
        {
            Username = "nonexistent",
            Password = "Password1!"
        });

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task Register_SetsIsAdmin_False()
    {
        var result = await _controller.Register(new RegisterDto
        {
            Username = "newuser",
            Email = "new@test.com",
            Password = "Password1!"
        });

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = okResult.Value as AuthResponseDto;
        Assert.False(response!.IsAdmin);
    }
}
