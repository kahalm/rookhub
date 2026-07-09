using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Öffentlicher Kurz-Alias (<see cref="Book.PublicSlug"/>): der Admin vergibt je öffentlichem Buch
/// einen Alias, über den der Kurs per Kurz-URL /{slug} erreichbar ist. Normalisierung, Validierung
/// (Format/reserviert/eindeutig) und die anonyme Auflösung auf die BookId.
/// </summary>
public class BookPublicSlugTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly BookAdminService _admin;
    private readonly CourseService _course;

    public BookPublicSlugTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _admin = new BookAdminService(_db);
        var notifications = new NotificationService(_db);
        _course = new CourseService(_db, NullLogger<CourseService>.Instance, new PgnImportService(_db),
            _admin,
            new RepertoireService(_db, new RepertoireAnalyzeService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()))),
            new FriendService(_db, notifications), notifications);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Book> SeedBookAsync(bool isPublic = true, string? slug = null)
    {
        var book = new Book
        {
            FileName = $"book-{Guid.NewGuid():N}.pgn",
            DisplayName = "Course",
            IsPublic = isPublic,
            PublicSlug = slug,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    [Fact]
    public async Task Update_SetsAndNormalizesSlug()
    {
        var book = await SeedBookAsync();
        var dto = await _admin.UpdateBookAsync(book.Id, new UpdateBookDto { PublicSlug = "  Mate1  " });
        Assert.Equal("mate1", dto.PublicSlug);
        Assert.Equal("mate1", (await _db.Books.FindAsync(book.Id))!.PublicSlug);
    }

    [Fact]
    public async Task Update_EmptySlug_ClearsIt()
    {
        var book = await SeedBookAsync(slug: "mate1");
        var dto = await _admin.UpdateBookAsync(book.Id, new UpdateBookDto { PublicSlug = "" });
        Assert.Null(dto.PublicSlug);
    }

    [Fact]
    public async Task Update_NullSlug_LeavesUnchanged()
    {
        var book = await SeedBookAsync(slug: "keep");
        var dto = await _admin.UpdateBookAsync(book.Id, new UpdateBookDto { DisplayName = "Renamed" });
        Assert.Equal("keep", dto.PublicSlug);
    }

    [Theory]
    [InlineData("courses")]   // reserviert (Top-Level-Route)
    [InlineData("admin")]     // reserviert
    public async Task Update_ReservedSlug_Throws(string slug)
    {
        var book = await SeedBookAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _admin.UpdateBookAsync(book.Id, new UpdateBookDto { PublicSlug = slug }));
    }

    [Theory]
    [InlineData("1abc")]       // beginnt mit Ziffer
    [InlineData("a")]          // zu kurz
    [InlineData("bad_slug")]   // Unterstrich
    [InlineData("Bad Slug")]   // Leerzeichen
    [InlineData("trailing-")]  // endet auf Bindestrich
    [InlineData("double--x")]  // doppelter Bindestrich
    public async Task Update_InvalidSlug_Throws(string slug)
    {
        var book = await SeedBookAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _admin.UpdateBookAsync(book.Id, new UpdateBookDto { PublicSlug = slug }));
    }

    [Fact]
    public async Task Update_DuplicateSlug_Throws()
    {
        await SeedBookAsync(slug: "taken");
        var other = await SeedBookAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _admin.UpdateBookAsync(other.Id, new UpdateBookDto { PublicSlug = "TAKEN" }));
    }

    [Fact]
    public async Task Update_SameSlugOnSameBook_Ok()
    {
        var book = await SeedBookAsync(slug: "mate1");
        // Erneutes Speichern desselben Buchs mit demselben Alias darf NICHT als Duplikat scheitern.
        var dto = await _admin.UpdateBookAsync(book.Id, new UpdateBookDto { PublicSlug = "mate1", ForDaily = true });
        Assert.Equal("mate1", dto.PublicSlug);
    }

    [Fact]
    public async Task ResolvePublicSlug_ReturnsBookId_ForPublicBook()
    {
        var book = await SeedBookAsync(isPublic: true, slug: "mate1");
        Assert.Equal(book.Id, await _course.ResolvePublicSlugAsync("MATE1"));
    }

    [Fact]
    public async Task ResolvePublicSlug_Null_ForNonPublicOrUnknown()
    {
        await SeedBookAsync(isPublic: false, slug: "hidden");
        Assert.Null(await _course.ResolvePublicSlugAsync("hidden"));
        Assert.Null(await _course.ResolvePublicSlugAsync("nope"));
        Assert.Null(await _course.ResolvePublicSlugAsync(""));
    }
}
