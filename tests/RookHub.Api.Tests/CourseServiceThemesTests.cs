using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Buch-Themen-Tags (Book.Themes): setzen durch Admin/Besitzer, Validierung, Default „tactics".</summary>
public class CourseServiceThemesTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseService _svc;

    public CourseServiceThemesTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new CourseService(_db, NullLogger<CourseService>.Instance, new PgnImportService(_db), new BookAdminService(_db),
            new RepertoireService(_db, new RepertoireAnalyzeService(_db, new MemoryCache(new MemoryCacheOptions()))));
    }

    public void Dispose() => _db.Dispose();

    private async Task<Book> SeedBookAsync(int? ownerUserId)
    {
        var book = new Book
        {
            FileName = $"b-{Guid.NewGuid():N}.pgn", DisplayName = "Course", OwnerUserId = ownerUserId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    [Fact]
    public async Task Owner_SetsThemes_StoresCsv()
    {
        var book = await SeedBookAsync(ownerUserId: 1);
        var eff = await _svc.SetBookThemesAsync(userId: 1, book.Id, new[] { "tactics", "endgame" }, isAdmin: false);
        Assert.Equal(new[] { "tactics", "endgame" }, eff);
        Assert.Equal("tactics,endgame", (await _db.Books.FindAsync(book.Id))!.Themes);
    }

    [Fact]
    public async Task Admin_SetsThemes_OnForeignBook()
    {
        var book = await SeedBookAsync(ownerUserId: 99);
        var eff = await _svc.SetBookThemesAsync(userId: 1, book.Id, new[] { "endgame" }, isAdmin: true);
        Assert.Equal(new[] { "endgame" }, eff);
    }

    [Fact]
    public async Task NonOwnerNonAdmin_Throws403()
    {
        // Buch von User 1, an User 2 geteilt → User 2 hat ZUGRIFF, ist aber weder Besitzer noch Admin → 403.
        var book = await SeedBookAsync(ownerUserId: 1);
        _db.CourseShares.Add(new CourseShare { BookId = book.Id, OwnerId = 1, RecipientId = 2, SharedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _svc.SetBookThemesAsync(userId: 2, book.Id, new[] { "endgame" }, isAdmin: false));
    }

    [Fact]
    public async Task InvalidKey_Throws400()
    {
        var book = await SeedBookAsync(ownerUserId: 1);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.SetBookThemesAsync(userId: 1, book.Id, new[] { "bogus" }, isAdmin: false));
    }

    [Fact]
    public async Task DefaultTactics_StoredAsNull_ReturnsTactics()
    {
        var book = await SeedBookAsync(ownerUserId: 1);
        // Nur „tactics" bzw. leer → als null gespeichert (Default greift), Rückgabe = ["tactics"].
        var eff = await _svc.SetBookThemesAsync(userId: 1, book.Id, new[] { "tactics" }, isAdmin: false);
        Assert.Equal(new[] { "tactics" }, eff);
        Assert.Null((await _db.Books.FindAsync(book.Id))!.Themes);

        var effEmpty = await _svc.SetBookThemesAsync(userId: 1, book.Id, Array.Empty<string>(), isAdmin: false);
        Assert.Equal(new[] { "tactics" }, effEmpty);
    }

    [Fact]
    public async Task GetCourses_ExposesThemes_DefaultTacticsWhenUnset()
    {
        var unset = await SeedBookAsync(ownerUserId: 1);
        var tagged = await SeedBookAsync(ownerUserId: 1);
        await _svc.SetBookThemesAsync(userId: 1, tagged.Id, new[] { "endgame", "tactics" }, isAdmin: false);

        var courses = await _svc.GetCoursesAsync(userId: 1, isAdmin: false);
        Assert.Equal(new[] { "tactics" }, courses.First(c => c.BookId == unset.Id).Themes);
        Assert.Equal(new[] { "endgame", "tactics" }, courses.First(c => c.BookId == tagged.Id).Themes);
    }
}
