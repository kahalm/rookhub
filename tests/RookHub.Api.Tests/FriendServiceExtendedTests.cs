using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class FriendServiceExtendedTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FriendService _friendService;

    public FriendServiceExtendedTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _friendService = new FriendService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserAsync(string username, Action<UserProfile>? configureProfile = null)
    {
        var profile = new UserProfile();
        configureProfile?.Invoke(profile);
        var user = new AppUser
        {
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = "hash",
            Profile = profile
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task GetPendingRequests_ReturnsPendingForAddressee()
    {
        var alice = await CreateUserAsync("alice");
        var bob = await CreateUserAsync("bob");
        await _friendService.SendRequestAsync(alice, bob);

        var pending = await _friendService.GetPendingRequestsAsync(bob);

        Assert.Single(pending);
        Assert.Equal(alice, pending[0].RequesterId);
        Assert.Equal("alice", pending[0].RequesterUsername);
    }

    [Fact]
    public async Task GetPendingRequests_Empty_WhenNoRequests()
    {
        var alice = await CreateUserAsync("alice");

        var pending = await _friendService.GetPendingRequestsAsync(alice);

        Assert.Empty(pending);
    }

    [Fact]
    public async Task RemoveFriend_RemovesFriendship()
    {
        var alice = await CreateUserAsync("alice");
        var bob = await CreateUserAsync("bob");
        var friendship = await _friendService.SendRequestAsync(alice, bob);
        await _friendService.AcceptRequestAsync(friendship.Id, bob);

        await _friendService.RemoveFriendAsync(friendship.Id, alice);

        var friends = await _friendService.GetFriendsAsync(alice);
        Assert.Empty(friends);
    }

    [Fact]
    public async Task RemoveFriend_NotFound_Throws()
    {
        var alice = await CreateUserAsync("alice");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _friendService.RemoveFriendAsync(99999, alice));
    }

    [Fact]
    public async Task RemoveFriend_NotPartOfFriendship_Throws()
    {
        var alice = await CreateUserAsync("alice");
        var bob = await CreateUserAsync("bob");
        var charlie = await CreateUserAsync("charlie");
        var friendship = await _friendService.SendRequestAsync(alice, bob);
        await _friendService.AcceptRequestAsync(friendship.Id, bob);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _friendService.RemoveFriendAsync(friendship.Id, charlie));
    }

    [Fact]
    public async Task SendRequest_AfterDecline_AllowsReRequest()
    {
        var alice = await CreateUserAsync("alice");
        var bob = await CreateUserAsync("bob");

        var friendship1 = await _friendService.SendRequestAsync(alice, bob);
        await _friendService.DeclineRequestAsync(friendship1.Id, bob);

        // M-12: Re-request should succeed after decline
        var friendship2 = await _friendService.SendRequestAsync(alice, bob);

        Assert.Equal(FriendshipStatus.Pending, friendship2.Status);
        Assert.NotEqual(friendship1.Id, friendship2.Id);
    }

    [Fact]
    public async Task SearchUsers_FindsByChessComUsername()
    {
        var me = await CreateUserAsync("me");
        var magnus = await CreateUserAsync("magnus", p => p.ChessComUsername = "MagnusCarlsen");

        var results = await _friendService.SearchUsersAsync("MagnusCarlsen", me);

        Assert.Single(results);
        Assert.Equal("magnus", results[0].Username);
        Assert.Equal("MagnusCarlsen", results[0].ChessComUsername);
    }

    [Fact]
    public async Task SearchUsers_FindsByChessResultsId()
    {
        var me = await CreateUserAsync("me");
        var player = await CreateUserAsync("player1", p => p.ChessResultsId = "CR-12345");

        var results = await _friendService.SearchUsersAsync("CR-12345", me);

        Assert.Single(results);
        Assert.Equal("player1", results[0].Username);
        Assert.Equal("CR-12345", results[0].ChessResultsId);
    }

    [Fact]
    public async Task SearchUsers_FindsByLichessUsername()
    {
        var me = await CreateUserAsync("me");
        var player = await CreateUserAsync("lichessplayer", p => p.LichessUsername = "DrNykterstein");

        var results = await _friendService.SearchUsersAsync("DrNykterstein", me);

        Assert.Single(results);
        Assert.Equal("lichessplayer", results[0].Username);
        Assert.Equal("DrNykterstein", results[0].LichessUsername);
    }

    [Fact]
    public async Task SearchUsers_FindsByFideId()
    {
        var me = await CreateUserAsync("me");
        var player = await CreateUserAsync("fideplayer", p => p.FideId = "1503014");

        var results = await _friendService.SearchUsersAsync("1503014", me);

        Assert.Single(results);
        Assert.Equal("fideplayer", results[0].Username);
        Assert.Equal("1503014", results[0].FideId);
    }
}
