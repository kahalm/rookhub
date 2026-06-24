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
        _controller = new ProfileController(_profileService, null!, DiscordTokenTestHelper.Service(),
            new ApiTokenService(_db, NullLogger<ApiTokenService>.Instance));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ListTokens_Empty_ForNewUser()
    {
        var u = await CreateUserAsync("u1");
        SetUser(u.Id);
        var result = (await _controller.ListTokens()).Result as OkObjectResult;
        Assert.NotNull(result);
        var list = Assert.IsType<List<ApiTokenDto>>(result.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task CreateToken_ReturnsRawTokenOnce_AndAppearsInList()
    {
        var u = await CreateUserAsync("u2");
        SetUser(u.Id);

        var created = (await _controller.CreateToken(new CreateApiTokenDto { Name = "ext" })).Result as OkObjectResult;
        Assert.NotNull(created);
        var dto = Assert.IsType<ApiTokenCreatedDto>(created.Value);
        Assert.StartsWith("rkh_", dto.RawToken);

        var listResult = (await _controller.ListTokens()).Result as OkObjectResult;
        var list = Assert.IsType<List<ApiTokenDto>>(listResult!.Value);
        Assert.Single(list);
        Assert.Equal("ext", list[0].Name);
        Assert.Equal(dto.Prefix, list[0].Prefix);
    }

    [Fact]
    public async Task CreateToken_InvalidScope_Returns400()
    {
        var u = await CreateUserAsync("u3");
        SetUser(u.Id);
        var bad = await _controller.CreateToken(new CreateApiTokenDto { Name = "x", Scope = "admin" });
        Assert.IsType<BadRequestObjectResult>(bad.Result);
    }

    [Fact]
    public async Task RevokeToken_RemovesAndReturns204_NotFoundForForeign()
    {
        var alice = await CreateUserAsync("alice");
        var bob = await CreateUserAsync("bob");
        SetUser(alice.Id);
        var c = (await _controller.CreateToken(new CreateApiTokenDto { Name = "x" })).Result as OkObjectResult;
        var dto = (ApiTokenCreatedDto)c!.Value!;

        // Foreign user kann nicht widerrufen.
        SetUser(bob.Id);
        Assert.IsType<NotFoundResult>(await _controller.RevokeToken(dto.Id));

        // Owner kann widerrufen.
        SetUser(alice.Id);
        Assert.IsType<NoContentResult>(await _controller.RevokeToken(dto.Id));

        // Erneuter Revoke → 404.
        Assert.IsType<NotFoundResult>(await _controller.RevokeToken(dto.Id));
    }

    private void SetUser(int userId, int? impersonatorAdminId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (impersonatorAdminId is int adminId)
            claims.Add(new Claim("imp", adminId.ToString()));
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    [Fact]
    public async Task CreateToken_WhileImpersonating_Returns403()
    {
        var u = await CreateUserAsync("imp-target");
        SetUser(u.Id, impersonatorAdminId: 999);

        var result = await _controller.CreateToken(new CreateApiTokenDto { Name = "ext" });
        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, status.StatusCode);
        // Es darf KEIN Token angelegt worden sein.
        Assert.Empty(_db.UserApiTokens);
    }

    [Fact]
    public async Task DeleteAccount_WhileImpersonating_Returns403()
    {
        var u = await CreateUserAsync("imp-target2");
        SetUser(u.Id, impersonatorAdminId: 999);

        var result = await _controller.DeleteAccount(new DeleteAccountDto { Password = "x" });
        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
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
        var profile = okResult.Value as PublicProfileDto;
        Assert.NotNull(profile);
        Assert.Equal("publicuser", profile.Username);
    }

    [Fact]
    public async Task GetPublicProfile_ReturnsNotFound_WhenUserMissing()
    {
        var result = await _controller.GetPublicProfile("nonexistent");

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ---- Discord link/unlink ----

    [Fact]
    public async Task LinkDiscord_ReturnsOk_WithValidToken()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);
        var token = DiscordTokenTestHelper.Make("555000111", "DiscoUser", DiscordTokenTestHelper.FarFuture);

        var result = await _controller.LinkDiscord(new LinkDiscordDto { Token = token });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var profile = Assert.IsType<ProfileDto>(ok.Value);
        Assert.Equal("555000111", profile.DiscordId);
        Assert.Equal("DiscoUser", profile.DiscordUsername);
    }

    [Fact]
    public async Task LinkDiscord_ReturnsBadRequest_WithInvalidToken()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);

        var result = await _controller.LinkDiscord(new LinkDiscordDto { Token = "garbage.token" });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task LinkDiscord_ReturnsConflict_WhenDiscordIdAlreadyLinkedToAnother()
    {
        var other = await CreateUserAsync("otheruser");
        other.Profile!.DiscordId = "999";
        await _db.SaveChangesAsync();

        var user = await CreateUserAsync("me");
        SetUser(user.Id);
        var token = DiscordTokenTestHelper.Make("999", "Dupe", DiscordTokenTestHelper.FarFuture);

        var result = await _controller.LinkDiscord(new LinkDiscordDto { Token = token });

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task LinkDiscord_Succeeds_WhenRelinkingSameUser()
    {
        var user = await CreateUserAsync();
        user.Profile!.DiscordId = "777";
        await _db.SaveChangesAsync();
        SetUser(user.Id);
        var token = DiscordTokenTestHelper.Make("777", "Same", DiscordTokenTestHelper.FarFuture);

        var result = await _controller.LinkDiscord(new LinkDiscordDto { Token = token });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("777", ((ProfileDto)ok.Value!).DiscordId);
    }

    [Fact]
    public async Task UnlinkDiscord_ClearsLink()
    {
        var user = await CreateUserAsync();
        user.Profile!.DiscordId = "123";
        user.Profile!.DiscordUsername = "Name";
        await _db.SaveChangesAsync();
        SetUser(user.Id);

        var result = await _controller.UnlinkDiscord();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var profile = Assert.IsType<ProfileDto>(ok.Value);
        Assert.Null(profile.DiscordId);
        Assert.Null(profile.DiscordUsername);
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
