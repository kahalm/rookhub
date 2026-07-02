using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// „Kurs mit ausgewählten Personen teilen": der Besitzer eines persönlichen Kurses gibt ihn
/// an befreundete Nutzer frei; diese sehen/lösen ihn dann (eigener Fortschritt), können ihn
/// aber nicht verwalten.
/// </summary>
public class CourseSharingTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseService _svc;

    public CourseSharingTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        var notifications = new NotificationService(_db);
        _svc = new CourseService(_db, NullLogger<CourseService>.Instance, new PgnImportService(_db),
            new BookAdminService(_db),
            new RepertoireService(_db, new RepertoireAnalyzeService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()))),
            new FriendService(_db, notifications), notifications);
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> SeedUserAsync(int id, string username)
    {
        var u = new AppUser { Id = id, Username = username, PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    private async Task MakeFriendsAsync(int a, int b)
    {
        _db.Friendships.Add(new Friendship { RequesterId = a, AddresseeId = b, Status = FriendshipStatus.Accepted });
        await _db.SaveChangesAsync();
    }

    private async Task<Book> SeedPersonalBookAsync(int ownerUserId)
    {
        var book = new Book
        {
            FileName = $"chessable-u{ownerUserId}-x.pgn",
            DisplayName = "My Course",
            OwnerUserId = ownerUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    [Fact]
    public async Task Share_WithFriend_GrantsAccess_AndCreatesNotification()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "friend");
        await MakeFriendsAsync(1, 2);
        var book = await SeedPersonalBookAsync(ownerUserId: 1);

        var res = await _svc.ShareCourseAsync(userId: 1, book.Id, new List<int> { 2 }, isAdmin: false);

        Assert.Equal(1, res.Shared);
        Assert.Empty(res.Skipped);
        // Empfänger sieht + darf den Kurs.
        Assert.True(await _svc.CanAccessAsync(userId: 2, book.Id, isAdmin: false));
        var courses = await _svc.GetCoursesAsync(userId: 2, isAdmin: false);
        var shared = courses.Single(c => c.BookId == book.Id);
        Assert.True(shared.IsShared);
        Assert.False(shared.IsOwned);
        Assert.Equal("owner", shared.SharedByUsername);
        // Benachrichtigung beim Empfänger.
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == 2 && n.Type == NotificationType.CourseShared));
    }

    [Fact]
    public async Task Share_WithNonFriend_IsSkipped_NotFriends()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "stranger");
        var book = await SeedPersonalBookAsync(ownerUserId: 1);

        var res = await _svc.ShareCourseAsync(userId: 1, book.Id, new List<int> { 2 }, isAdmin: false);

        Assert.Equal(0, res.Shared);
        Assert.Equal("not_friends", res.Skipped.Single().Reason);
        Assert.False(await _svc.CanAccessAsync(userId: 2, book.Id, isAdmin: false));
    }

    [Fact]
    public async Task Share_UnknownUser_IsSkipped_NotFound()
    {
        await SeedUserAsync(1, "owner");
        var book = await SeedPersonalBookAsync(ownerUserId: 1);

        var res = await _svc.ShareCourseAsync(userId: 1, book.Id, new List<int> { 999 }, isAdmin: false);

        Assert.Equal(0, res.Shared);
        Assert.Equal("not_found", res.Skipped.Single().Reason);
    }

    [Fact]
    public async Task Share_Self_IsSkipped()
    {
        await SeedUserAsync(1, "owner");
        var book = await SeedPersonalBookAsync(ownerUserId: 1);

        var res = await _svc.ShareCourseAsync(userId: 1, book.Id, new List<int> { 1 }, isAdmin: false);

        Assert.Equal(0, res.Shared);
        Assert.Equal("self", res.Skipped.Single().Reason);
    }

    [Fact]
    public async Task Share_Twice_IsIdempotent_SecondSkippedAsDuplicate()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "friend");
        await MakeFriendsAsync(1, 2);
        var book = await SeedPersonalBookAsync(ownerUserId: 1);

        await _svc.ShareCourseAsync(userId: 1, book.Id, new List<int> { 2 }, isAdmin: false);
        var second = await _svc.ShareCourseAsync(userId: 1, book.Id, new List<int> { 2 }, isAdmin: false);

        Assert.Equal(0, second.Shared);
        Assert.Equal("duplicate", second.Skipped.Single().Reason);
        Assert.Single(_db.CourseShares);
    }

    [Fact]
    public async Task Share_ByNonOwner_Throws()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "other");
        await SeedUserAsync(3, "friend");
        await MakeFriendsAsync(2, 3);
        var book = await SeedPersonalBookAsync(ownerUserId: 1);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _svc.ShareCourseAsync(userId: 2, book.Id, new List<int> { 3 }, isAdmin: false));
        Assert.Empty(_db.CourseShares);
    }

    [Fact]
    public async Task Share_Admin_CanShareWithNonFriend()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "stranger");
        var book = await SeedPersonalBookAsync(ownerUserId: 1);

        var res = await _svc.ShareCourseAsync(userId: 1, book.Id, new List<int> { 2 }, isAdmin: true);

        Assert.Equal(1, res.Shared);
        Assert.True(await _svc.CanAccessAsync(userId: 2, book.Id, isAdmin: false));
    }

    [Fact]
    public async Task GetShareRecipients_ReturnsSharedUsers_OwnerOnly()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "friend");
        await MakeFriendsAsync(1, 2);
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        await _svc.ShareCourseAsync(userId: 1, book.Id, new List<int> { 2 }, isAdmin: false);

        var recipients = await _svc.GetShareRecipientsAsync(userId: 1, book.Id);
        Assert.Equal(2, recipients.Single().UserId);
        Assert.Equal("friend", recipients.Single().Username);

        // Nicht-Besitzer darf die Freigabeliste nicht lesen.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.GetShareRecipientsAsync(userId: 2, book.Id));
    }

    [Fact]
    public async Task Unshare_RevokesAccess()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "friend");
        await MakeFriendsAsync(1, 2);
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        await _svc.ShareCourseAsync(userId: 1, book.Id, new List<int> { 2 }, isAdmin: false);
        Assert.True(await _svc.CanAccessAsync(userId: 2, book.Id, isAdmin: false));

        await _svc.UnshareCourseAsync(userId: 1, book.Id, recipientId: 2);

        Assert.Empty(_db.CourseShares);
        Assert.False(await _svc.CanAccessAsync(userId: 2, book.Id, isAdmin: false));
    }

    [Fact]
    public async Task Unshare_ByNonOwner_Throws()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "friend");
        await MakeFriendsAsync(1, 2);
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        await _svc.ShareCourseAsync(userId: 1, book.Id, new List<int> { 2 }, isAdmin: false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _svc.UnshareCourseAsync(userId: 2, book.Id, recipientId: 2));
        Assert.Single(_db.CourseShares);
    }

    [Fact]
    public async Task DeletePersonalCourse_AlsoRemovesShares()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "friend");
        await MakeFriendsAsync(1, 2);
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        await _svc.ShareCourseAsync(userId: 1, book.Id, new List<int> { 2 }, isAdmin: false);
        Assert.Single(_db.CourseShares);

        await _svc.DeletePersonalCourseAsync(userId: 1, book.Id);
        Assert.Empty(_db.CourseShares);
    }

    [Fact]
    public async Task HasAnyAccess_TrueForRecipient()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "friend");
        await MakeFriendsAsync(1, 2);
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        await _svc.ShareCourseAsync(userId: 1, book.Id, new List<int> { 2 }, isAdmin: false);

        Assert.True(await _svc.HasAnyAccessAsync(userId: 2, isAdmin: false));
    }
}
