using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Persönliche Kurs-Verknüpfung (Buch ↔ Workbook) für den Schnellwechsel.
/// </summary>
public class CourseLinkTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseService _svc;

    public CourseLinkTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new CourseService(_db, NullLogger<CourseService>.Instance, new PgnImportService(_db),
            new BookAdminService(_db),
            new RepertoireService(_db, new RepertoireAnalyzeService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()))));
    }

    public void Dispose() => _db.Dispose();

    private async Task<Book> SeedBookAsync(int ownerUserId, string name)
    {
        var book = new Book { FileName = $"u{ownerUserId}-{Guid.NewGuid():N}.pgn", DisplayName = name, OwnerUserId = ownerUserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    [Fact]
    public async Task Link_IsSymmetric_AndShownBothWaysInList()
    {
        var book = await SeedBookAsync(1, "Book");
        var wb = await SeedBookAsync(1, "Workbook");

        await _svc.LinkCoursesAsync(userId: 1, book.Id, wb.Id, isAdmin: false);

        var link1 = await _svc.GetLinkAsync(1, book.Id, false);
        var link2 = await _svc.GetLinkAsync(1, wb.Id, false);
        Assert.Equal(wb.Id, link1.LinkedBookId);
        Assert.Equal("Workbook", link1.LinkedDisplayName);
        Assert.Equal(book.Id, link2.LinkedBookId);
        Assert.Equal("Book", link2.LinkedDisplayName);

        var courses = await _svc.GetCoursesAsync(1, false);
        Assert.Equal(wb.Id, courses.Single(c => c.BookId == book.Id).LinkedBookId);
        Assert.Equal(book.Id, courses.Single(c => c.BookId == wb.Id).LinkedBookId);
        Assert.Equal("Workbook", courses.Single(c => c.BookId == book.Id).LinkedDisplayName);
    }

    [Fact]
    public async Task Link_IsPerUser_NotVisibleToOthers()
    {
        var book = await SeedBookAsync(1, "Book");
        var wb = await SeedBookAsync(1, "Workbook");
        await _svc.LinkCoursesAsync(1, book.Id, wb.Id, false);

        // Admin (anderer User) sieht KEINE fremde persönliche Verknüpfung.
        var adminLink = await _svc.GetLinkAsync(99, book.Id, isAdmin: true);
        Assert.Null(adminLink.LinkedBookId);
    }

    [Fact]
    public async Task Link_ReplacesExistingLink_OnePartnerPerBook()
    {
        var a = await SeedBookAsync(1, "A");
        var b = await SeedBookAsync(1, "B");
        var c = await SeedBookAsync(1, "C");
        await _svc.LinkCoursesAsync(1, a.Id, b.Id, false);
        await _svc.LinkCoursesAsync(1, a.Id, c.Id, false);   // A jetzt mit C → A↔B aufgelöst

        Assert.Equal(c.Id, (await _svc.GetLinkAsync(1, a.Id, false)).LinkedBookId);
        Assert.Equal(a.Id, (await _svc.GetLinkAsync(1, c.Id, false)).LinkedBookId);
        Assert.Null((await _svc.GetLinkAsync(1, b.Id, false)).LinkedBookId);   // B nicht mehr verknüpft
        Assert.Equal(2, await _db.CourseLinks.CountAsync());                    // genau ein Paar
    }

    [Fact]
    public async Task Link_ToSelf_Throws()
    {
        var a = await SeedBookAsync(1, "A");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.LinkCoursesAsync(1, a.Id, a.Id, false));
    }

    [Fact]
    public async Task Link_InaccessibleBook_Throws()
    {
        var mine = await SeedBookAsync(1, "Mine");
        var other = await SeedBookAsync(2, "Other");   // gehört User 2
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.LinkCoursesAsync(1, mine.Id, other.Id, false));
        Assert.Empty(_db.CourseLinks);
    }

    [Fact]
    public async Task Unlink_RemovesBothDirections()
    {
        var a = await SeedBookAsync(1, "A");
        var b = await SeedBookAsync(1, "B");
        await _svc.LinkCoursesAsync(1, a.Id, b.Id, false);

        await _svc.UnlinkCourseAsync(1, a.Id);
        Assert.Empty(_db.CourseLinks);
        Assert.Null((await _svc.GetLinkAsync(1, b.Id, false)).LinkedBookId);
    }

    [Fact]
    public async Task DeletePersonalCourse_RemovesLinksBothDirections()
    {
        var a = await SeedBookAsync(1, "A");
        var b = await SeedBookAsync(1, "B");
        await _svc.LinkCoursesAsync(1, a.Id, b.Id, false);
        Assert.Equal(2, await _db.CourseLinks.CountAsync());

        await _svc.DeletePersonalCourseAsync(1, b.Id);   // Workbook gelöscht → beide Zeilen weg
        Assert.Empty(_db.CourseLinks);
    }
}
