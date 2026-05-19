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

    private async Task<int> CreateUserAsync(string username)
    {
        var user = new AppUser
        {
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = "hash",
            Profile = new UserProfile()
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
}
