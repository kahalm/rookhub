using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly PuzzleService _puzzleService;
    private readonly FriendController _controller;

    public FriendControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _friendService = new FriendService(_db);
        _puzzleService = new PuzzleService(_db, new MemoryCache(new MemoryCacheOptions()), NullLogger<PuzzleService>.Instance);
        _controller = new FriendController(_friendService, _puzzleService);
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

    [Fact]
    public async Task Accept_Returns403_WhenNotAddressee()
    {
        // Regression: Controller gab Forbid(ex.Message) zurueck -> Message wurde als
        // Auth-Scheme interpretiert -> HTTP 500 statt 403.
        var requester = await CreateUserAsync("user1");
        var addressee = await CreateUserAsync("user2");
        var friendship = new Friendship
        {
            RequesterId = requester.Id,
            AddresseeId = addressee.Id,
            Status = FriendshipStatus.Pending
        };
        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();
        SetUser(requester.Id); // Requester (nicht Addressee) versucht zu akzeptieren

        var result = await _controller.Accept(friendship.Id);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, obj.StatusCode);
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

    // ---- GetFriendStats ----

    private async Task MakeFriendsAsync(int requesterId, int addresseeId)
    {
        _db.Friendships.Add(new Friendship
        {
            RequesterId = requesterId,
            AddresseeId = addresseeId,
            Status = FriendshipStatus.Accepted
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetFriendStats_ReturnsStats_WhenFriends()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        await MakeFriendsAsync(me.Id, friend.Id);

        var puzzle = new Puzzle { LichessId = "abc", Fen = "fen", Moves = "e2e4", Rating = 1500, Themes = "fork" };
        _db.Puzzles.Add(puzzle);
        await _db.SaveChangesAsync();
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = friend.Id, PuzzleId = puzzle.Id, Solved = true, TimeSpentSeconds = 5 });
        await _db.SaveChangesAsync();

        SetUser(me.Id);
        var result = await _controller.GetFriendStats(friend.Id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<FriendStatsDto>(ok.Value);
        Assert.Equal(friend.Id, dto.UserId);
        Assert.Equal("friend", dto.Username);
        Assert.Equal(1, dto.Stats.Solved);
        Assert.Equal(1, dto.Stats.TotalAttempts);
    }

    [Fact]
    public async Task GetFriendStats_Returns403_WhenNotFriends()
    {
        var me = await CreateUserAsync("me");
        var stranger = await CreateUserAsync("stranger");

        SetUser(me.Id);
        var result = await _controller.GetFriendStats(stranger.Id);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public async Task GetFriendStats_Returns403_WhenOnlyPendingRequest()
    {
        var me = await CreateUserAsync("me");
        var other = await CreateUserAsync("other");
        _db.Friendships.Add(new Friendship { RequesterId = me.Id, AddresseeId = other.Id, Status = FriendshipStatus.Pending });
        await _db.SaveChangesAsync();

        SetUser(me.Id);
        var result = await _controller.GetFriendStats(other.Id);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, status.StatusCode);
    }

    // ---- GetRevenge ----

    private async Task<Puzzle> CreatePuzzleAsync(string lichessId, int rating, string themes)
    {
        var p = new Puzzle { LichessId = lichessId, Fen = "fen", Moves = "e2e4", Rating = rating, Themes = themes };
        _db.Puzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    [Fact]
    public async Task GetRevenge_ReturnsOnlyUnsolvedFailures_WhenFriends()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        await MakeFriendsAsync(me.Id, friend.Id);

        var failedOnly = await CreatePuzzleAsync("p1", 1700, "fork");      // 2x gescheitert, nie gelöst → drin
        var failedThenSolved = await CreatePuzzleAsync("p2", 1500, "pin"); // gescheitert, dann gelöst → raus
        var solvedOnly = await CreatePuzzleAsync("p3", 1400, "skewer");    // nur gelöst → raus

        _db.PuzzleAttempts.AddRange(
            new PuzzleAttempt { UserId = friend.Id, PuzzleId = failedOnly.Id, Solved = false, TimeSpentSeconds = 12, AttemptedAt = new DateTime(2026, 6, 1) },
            new PuzzleAttempt { UserId = friend.Id, PuzzleId = failedOnly.Id, Solved = false, TimeSpentSeconds = 9, AttemptedAt = new DateTime(2026, 6, 10) },
            new PuzzleAttempt { UserId = friend.Id, PuzzleId = failedThenSolved.Id, Solved = false, TimeSpentSeconds = 8 },
            new PuzzleAttempt { UserId = friend.Id, PuzzleId = failedThenSolved.Id, Solved = true, TimeSpentSeconds = 6 },
            new PuzzleAttempt { UserId = friend.Id, PuzzleId = solvedOnly.Id, Solved = true, TimeSpentSeconds = 5 });
        await _db.SaveChangesAsync();

        SetUser(me.Id);
        var result = await _controller.GetRevenge(friend.Id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<RevengeListDto>(ok.Value);
        Assert.Single(dto.Puzzles);
        var rev = dto.Puzzles[0];
        Assert.Equal(failedOnly.Id, rev.PuzzleId);
        Assert.Equal(2, rev.FailCount);
        Assert.Equal(1700, rev.Rating);
    }

    [Fact]
    public async Task GetRevenge_Returns403_WhenNotFriends()
    {
        var me = await CreateUserAsync("me");
        var stranger = await CreateUserAsync("stranger");

        SetUser(me.Id);
        var result = await _controller.GetRevenge(stranger.Id);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, status.StatusCode);
    }
}
