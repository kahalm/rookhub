using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Sichtbarkeit persönlicher Bücher (Book.OwnerUserId, z. B. eigener Chessable-Import):
/// nur der Besitzer (und Admins) sehen das Buch als Kurs.
/// </summary>
public class CourseServiceOwnerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseService _svc;

    public CourseServiceOwnerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new CourseService(_db, NullLogger<CourseService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Book> SeedPersonalBookAsync(int ownerUserId)
    {
        var book = new Book
        {
            FileName = $"chessable-u{ownerUserId}-x.pgn",
            DisplayName = "My Chessable Course",
            OwnerUserId = ownerUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    [Fact]
    public async Task Owner_CanAccess_PersonalBook()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        Assert.True(await _svc.CanAccessAsync(userId: 1, book.Id, isAdmin: false));
    }

    [Fact]
    public async Task NonOwner_CannotAccess_PersonalBook()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        Assert.False(await _svc.CanAccessAsync(userId: 2, book.Id, isAdmin: false));
    }

    [Fact]
    public async Task Admin_CanAccess_AnyPersonalBook()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        Assert.True(await _svc.CanAccessAsync(userId: 99, book.Id, isAdmin: true));
    }

    [Fact]
    public async Task GetCourses_ListsOwnPersonalBook_NotForOthers()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);

        var ownerCourses = await _svc.GetCoursesAsync(userId: 1, isAdmin: false);
        Assert.Contains(ownerCourses, c => c.BookId == book.Id);

        var otherCourses = await _svc.GetCoursesAsync(userId: 2, isAdmin: false);
        Assert.DoesNotContain(otherCourses, c => c.BookId == book.Id);
    }

    [Fact]
    public async Task HasAnyAccess_TrueForOwner_FalseForOther()
    {
        await SeedPersonalBookAsync(ownerUserId: 1);
        Assert.True(await _svc.HasAnyAccessAsync(userId: 1, isAdmin: false));
        Assert.False(await _svc.HasAnyAccessAsync(userId: 2, isAdmin: false));
    }

    /// <summary>Buch über eine Gruppe freigegeben (kein OwnerUserId) — öffentlicher Kurs.</summary>
    private async Task<Book> SeedGroupBookAsync(int groupId, int memberUserId)
    {
        var book = new Book
        {
            FileName = $"group-{groupId}.pgn",
            DisplayName = "Group Course",
            OwnerUserId = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Books.Add(book);
        _db.UserGroups.Add(new UserGroup { UserId = memberUserId, GroupId = groupId });
        await _db.SaveChangesAsync();
        _db.BookGroupAccesses.Add(new BookGroupAccess { BookId = book.Id, GroupId = groupId });
        await _db.SaveChangesAsync();
        return book;
    }

    [Fact]
    public async Task GetCourses_MarksPersonalBook_AsOwned()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        var courses = await _svc.GetCoursesAsync(userId: 1, isAdmin: false);
        Assert.True(courses.Single(c => c.BookId == book.Id).IsOwned);
    }

    [Fact]
    public async Task GetCourses_MarksGroupBook_AsNotOwned()
    {
        var book = await SeedGroupBookAsync(groupId: 5, memberUserId: 1);
        var courses = await _svc.GetCoursesAsync(userId: 1, isAdmin: false);
        Assert.False(courses.Single(c => c.BookId == book.Id).IsOwned);
    }

    [Fact]
    public async Task GetCourses_OtherUsersPersonalBook_NotOwned_ForAdmin()
    {
        var book = await SeedPersonalBookAsync(ownerUserId: 1);
        var courses = await _svc.GetCoursesAsync(userId: 99, isAdmin: true);
        Assert.False(courses.Single(c => c.BookId == book.Id).IsOwned);
    }
}
