using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class ProfileControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ProfileService _profileService;
    private readonly ProfileController _controller;

    public ProfileControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        _profileService = new ProfileService(_db, new NoOpTaskQueue(), NullLogger<ProfileService>.Instance);

        // PlayerSearchService is needed but we test SearchPlayers validation separately
        // For controller tests, we pass a null-ish PlayerSearchService only for non-search tests
        _controller = new ProfileController(_profileService, null!);
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    private async Task<AppUser> CreateUserAsync(string username = "testuser")
    {
        var user = new AppUser
        {
            Username = username,
            Email = $"{username}@test.com",
            PasswordHash = "hash",
            Profile = new UserProfile()
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    // ---- GetMyProfile ----

    [Fact]
    public async Task GetMyProfile_ReturnsOk()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);

        var result = await _controller.GetMyProfile();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var profile = okResult.Value as ProfileDto;
        Assert.NotNull(profile);
        Assert.Equal("testuser", profile.Username);
    }

    [Fact]
    public async Task GetMyProfile_ReturnsNotFound_WhenUserMissing()
    {
        SetUser(99999);

        var result = await _controller.GetMyProfile();

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ---- UpdateProfile ----

    [Fact]
    public async Task UpdateProfile_ReturnsOk_WithUpdatedData()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);

        var result = await _controller.UpdateProfile(new UpdateProfileDto
        {
            DisplayName = "TestDisplay",
            FideId = "12345"
        });

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var profile = okResult.Value as ProfileDto;
        Assert.NotNull(profile);
        Assert.Equal("TestDisplay", profile.DisplayName);
        Assert.Equal("12345", profile.FideId);
    }

    [Fact]
    public async Task UpdateProfile_ReturnsNotFound_WhenUserMissing()
    {
        SetUser(99999);

        var result = await _controller.UpdateProfile(new UpdateProfileDto { DisplayName = "x" });

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ---- GetPublicProfile ----

    [Fact]
    public async Task GetPublicProfile_ReturnsOk()
    {
        await CreateUserAsync("publicuser");

        var result = await _controller.GetPublicProfile("publicuser");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var profile = okResult.Value as ProfileDto;
        Assert.NotNull(profile);
        Assert.Equal("publicuser", profile.Username);
    }

    [Fact]
    public async Task GetPublicProfile_ReturnsNotFound_WhenUserMissing()
    {
        var result = await _controller.GetPublicProfile("nonexistent");

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ---- SearchPlayers ----

    [Fact]
    public async Task SearchPlayers_ReturnsBadRequest_WhenLastNameTooShort()
    {
        SetUser(1);

        var result = await _controller.SearchPlayers("a", null);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SearchPlayers_ReturnsBadRequest_WhenLastNameEmpty()
    {
        SetUser(1);

        var result = await _controller.SearchPlayers("", null);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SearchPlayers_ReturnsBadRequest_WhenLastNameWhitespace()
    {
        SetUser(1);

        var result = await _controller.SearchPlayers("  ", null);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
