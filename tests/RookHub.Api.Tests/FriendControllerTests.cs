using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class FriendControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FriendService _friendService;
    private readonly FriendController _controller;

    public FriendControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _friendService = new FriendService(_db);
        _controller = new FriendController(_friendService);
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

    private async Task<AppUser> CreateUserAsync(string username)
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

    // ---- GetFriends ----

    [Fact]
    public async Task GetFriends_ReturnsOk_Empty()
    {
        var user = await CreateUserAsync("user1");
        SetUser(user.Id);

        var result = await _controller.GetFriends();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var friends = okResult.Value as List<FriendDto>;
        Assert.NotNull(friends);
        Assert.Empty(friends);
    }

    [Fact]
    public async Task GetFriends_ReturnsAcceptedFriends()
    {
        var user1 = await CreateUserAsync("user1");
        var user2 = await CreateUserAsync("user2");
        _db.Friendships.Add(new Friendship
        {
            RequesterId = user1.Id,
            AddresseeId = user2.Id,
            Status = FriendshipStatus.Accepted
        });
        await _db.SaveChangesAsync();
        SetUser(user1.Id);

        var result = await _controller.GetFriends();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var friends = okResult.Value as List<FriendDto>;
        Assert.Single(friends!);
        Assert.Equal("user2", friends![0].Username);
    }

    // ---- GetRequests ----

    [Fact]
    public async Task GetRequests_ReturnsPending()
    {
        var requester = await CreateUserAsync("requester");
        var addressee = await CreateUserAsync("addressee");
        _db.Friendships.Add(new Friendship
        {
            RequesterId = requester.Id,
            AddresseeId = addressee.Id,
            Status = FriendshipStatus.Pending
        });
        await _db.SaveChangesAsync();
        SetUser(addressee.Id);

        var result = await _controller.GetRequests();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var requests = okResult.Value as List<FriendRequestDto>;
        Assert.Single(requests!);
    }

    // ---- SendRequest ----

    [Fact]
    public async Task SendRequest_ReturnsOk()
    {
        var user1 = await CreateUserAsync("user1");
        var user2 = await CreateUserAsync("user2");
        SetUser(user1.Id);

        var result = await _controller.SendRequest(user2.Id) as OkObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task SendRequest_ReturnsConflict_WhenDuplicate()
    {
        var user1 = await CreateUserAsync("user1");
        var user2 = await CreateUserAsync("user2");
        _db.Friendships.Add(new Friendship
        {
            RequesterId = user1.Id,
            AddresseeId = user2.Id,
            Status = FriendshipStatus.Pending
        });
        await _db.SaveChangesAsync();
        SetUser(user1.Id);

        var result = await _controller.SendRequest(user2.Id);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task SendRequest_ReturnsConflict_WhenSelfRequest()
    {
        var user1 = await CreateUserAsync("user1");
        SetUser(user1.Id);

        var result = await _controller.SendRequest(user1.Id);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task SendRequest_ReturnsNotFound_WhenUserMissing()
    {
        var user1 = await CreateUserAsync("user1");
        SetUser(user1.Id);

        var result = await _controller.SendRequest(99999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---- Accept ----

    [Fact]
    public async Task Accept_ReturnsOk()
    {
        var user1 = await CreateUserAsync("user1");
        var user2 = await CreateUserAsync("user2");
        var friendship = new Friendship
        {
            RequesterId = user1.Id,
            AddresseeId = user2.Id,
            Status = FriendshipStatus.Pending
        };
        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();
        SetUser(user2.Id);

        var result = await _controller.Accept(friendship.Id) as OkObjectResult;

        Assert.NotNull(result);
        var updated = await _db.Friendships.FindAsync(friendship.Id);
        Assert.Equal(FriendshipStatus.Accepted, updated!.Status);
    }

    [Fact]
    public async Task Accept_ReturnsNotFound_WhenMissing()
    {
        var user = await CreateUserAsync("user1");
        SetUser(user.Id);

        var result = await _controller.Accept(99999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---- Decline ----

    [Fact]
    public async Task Decline_ReturnsOk()
    {
        var user1 = await CreateUserAsync("user1");
        var user2 = await CreateUserAsync("user2");
        var friendship = new Friendship
        {
            RequesterId = user1.Id,
            AddresseeId = user2.Id,
            Status = FriendshipStatus.Pending
        };
        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();
        SetUser(user2.Id);

        var result = await _controller.Decline(friendship.Id) as OkObjectResult;

        Assert.NotNull(result);
        var updated = await _db.Friendships.FindAsync(friendship.Id);
        Assert.Equal(FriendshipStatus.Declined, updated!.Status);
    }

    // ---- Remove ----

    [Fact]
    public async Task Remove_ReturnsOk()
    {
        var user1 = await CreateUserAsync("user1");
        var user2 = await CreateUserAsync("user2");
        var friendship = new Friendship
        {
            RequesterId = user1.Id,
            AddresseeId = user2.Id,
            Status = FriendshipStatus.Accepted
        };
        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();
        SetUser(user1.Id);

        var result = await _controller.Remove(friendship.Id) as OkObjectResult;

        Assert.NotNull(result);
        Assert.Null(await _db.Friendships.FindAsync(friendship.Id));
    }

    [Fact]
    public async Task Remove_ReturnsNotFound_WhenMissing()
    {
        var user = await CreateUserAsync("user1");
        SetUser(user.Id);

        var result = await _controller.Remove(99999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---- Search ----

    [Fact]
    public async Task Search_ReturnsBadRequest_WhenQueryTooShort()
    {
        var user = await CreateUserAsync("user1");
        SetUser(user.Id);

        var result = await _controller.Search("a");

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Search_ReturnsBadRequest_WhenQueryEmpty()
    {
        var user = await CreateUserAsync("user1");
        SetUser(user.Id);

        var result = await _controller.Search("");

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Search_ReturnsOk_WithResults()
    {
        var user1 = await CreateUserAsync("user1");
        var user2 = await CreateUserAsync("searchable");
        SetUser(user1.Id);

        var result = await _controller.Search("searchable");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var results = okResult.Value as List<UserSearchResultDto>;
        Assert.NotNull(results);
        Assert.Single(results);
    }

    [Fact]
    public async Task Search_TruncatesLongQuery()
    {
        var user = await CreateUserAsync("user1");
        SetUser(user.Id);
        var longQuery = new string('a', 100);

        // Should not throw, query gets truncated to 50 chars
        var result = await _controller.Search(longQuery);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
    }
}
