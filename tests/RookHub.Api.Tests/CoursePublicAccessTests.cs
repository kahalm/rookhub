using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// „Öffentliche Kurse" (<see cref="Book.IsPublic"/>): ein als public markierter Kurs ist ohne
/// Registrierung über den Direkt-Link nutzbar. Server-seitig heißt das: jeder (auch ein
/// eingeloggter Nutzer ohne Gruppen-Freigabe) darf zugreifen, und die öffentlichen Puzzles
/// lassen sich ganz ohne User-Kontext abrufen. Nicht-öffentliche Kurse bleiben gesperrt.
/// </summary>
public class CoursePublicAccessTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseService _svc;

    public CoursePublicAccessTests()
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

    private async Task<Book> SeedBookAsync(bool isPublic, int puzzleCount = 2)
    {
        var book = new Book
        {
            FileName = $"book-{Guid.NewGuid():N}.pgn",
            DisplayName = "Public Course",
            IsPublic = isPublic,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        for (var i = 0; i < puzzleCount; i++)
            _db.BookPuzzles.Add(new BookPuzzle
            {
                BookId = book.Id,
                LineId = $"{book.FileName}#{i}",
                BookFileName = book.FileName,
                Round = (i + 1).ToString(),
                Fen = "8/8/8/8/8/8/8/K6k w - - 0 1",
                Moves = "a1a2",
                Title = $"Line {i}",
            });
        await _db.SaveChangesAsync();
        return book;
    }

    [Fact]
    public async Task PublicBook_IsAccessible_ToAnyLoggedInUser_WithoutGroupOrOwnership()
    {
        var book = await SeedBookAsync(isPublic: true);
        Assert.True(await _svc.CanAccessAsync(userId: 42, book.Id, isAdmin: false));
    }

    [Fact]
    public async Task NonPublicBook_IsNotAccessible_ToUnrelatedUser()
    {
        var book = await SeedBookAsync(isPublic: false);
        Assert.False(await _svc.CanAccessAsync(userId: 42, book.Id, isAdmin: false));
    }

    [Fact]
    public async Task GetPublicCoursePuzzles_ReturnsPuzzles_ForPublicBook()
    {
        var book = await SeedBookAsync(isPublic: true, puzzleCount: 3);
        var puzzles = await _svc.GetPublicCoursePuzzlesAsync(book.Id);
        Assert.Equal(3, puzzles.Count);
        Assert.All(puzzles, p => Assert.Equal(book.FileName, p.BookFileName));
    }

    [Fact]
    public async Task GetPublicCoursePuzzles_Throws_ForNonPublicBook()
    {
        var book = await SeedBookAsync(isPublic: false);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.GetPublicCoursePuzzlesAsync(book.Id));
    }

    [Fact]
    public async Task GetPublicCoursePuzzles_Throws_ForMissingBook()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.GetPublicCoursePuzzlesAsync(9999));
    }
}
