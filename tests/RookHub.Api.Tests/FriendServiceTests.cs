using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

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
        _friendService = new FriendService(_db, new NotificationService(_db));
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

    [Fact]
    public async Task SearchUsers_MatchesUsernameByPrefix_NotMidSubstring()
    {
        var me = await CreateUserAsync("me");
        await CreateUserAsync("bobby");   // Präfix-Treffer für "bob"
        await CreateUserAsync("alibob");  // Mid-Substring → KEIN Treffer mehr (präfix-anker)

        var results = await _friendService.SearchUsersAsync("bob", me);
        Assert.Single(results);
        Assert.Equal("bobby", results[0].Username);
    }

    [Fact]
    public async Task SearchUsers_EmptyOrWildcardOnlyQuery_ReturnsEmpty()
    {
        var me = await CreateUserAsync("me");
        await CreateUserAsync("someone");

        Assert.Empty(await _friendService.SearchUsersAsync("   ", me));
        Assert.Empty(await _friendService.SearchUsersAsync("%%", me)); // Wildcards gestrippt → leer
    }

    [Fact]
    public async Task SearchUsers_LongQuery_IsTruncated_NoThrow()
    {
        var me = await CreateUserAsync("me");
        await CreateUserAsync("zzz");
        // 200-Zeichen-Query darf nicht werfen (service-seitig auf 50 gekürzt) und nichts finden.
        var results = await _friendService.SearchUsersAsync(new string('a', 200), me);
        Assert.Empty(results);
    }
}
