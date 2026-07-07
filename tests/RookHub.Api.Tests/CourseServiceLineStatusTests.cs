using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Pro-Linien-Bearbeitungsstatus für die „Linien durchsehen"-Ansicht: eine Linie ist gelöst (✓),
/// wenn sie in <see cref="CoursePuzzleResult"/> steht, „gescheitert" (✗), wenn ein
/// <see cref="CourseAttempt"/> existiert, aber (aktuell) keine Lösung — sonst offen.
/// </summary>
public class CourseServiceLineStatusTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseService _svc;
    private const int UserId = 1;

    public CourseServiceLineStatusTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new CourseService(_db, NullLogger<CourseService>.Instance, new PgnImportService(_db), new BookAdminService(_db),
            new RepertoireService(_db, new RepertoireAnalyzeService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()))));
    }

    public void Dispose() => _db.Dispose();

    private async Task<BookPuzzle> AddPuzzleAsync(Book book, string lineId)
    {
        var p = new BookPuzzle
        {
            LineId = lineId, BookFileName = book.FileName, BookId = book.Id, Round = lineId,
            Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", Moves = "e2e4",
        };
        _db.BookPuzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    [Fact]
    public async Task GetLineStatus_SeparatesSolvedFromAttemptedFailed()
    {
        var book = new Book { FileName = "b.pgn", DisplayName = "B", OwnerUserId = UserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        var solved = await AddPuzzleAsync(book, "s");
        var failed = await AddPuzzleAsync(book, "f");
        var untouched = await AddPuzzleAsync(book, "u");

        // Gelöste Linie: Result + (irrelevant) auch ein Versuch.
        _db.CoursePuzzleResults.Add(new CoursePuzzleResult { UserId = UserId, BookId = book.Id, BookPuzzleId = solved.Id, SolvedAt = DateTime.UtcNow });
        _db.CourseAttempts.Add(new CourseAttempt { UserId = UserId, BookId = book.Id, BookPuzzleId = solved.Id, Solved = true, AttemptedAt = DateTime.UtcNow });
        // Gescheiterte Linie: nur ein Versuch, kein Result.
        _db.CourseAttempts.Add(new CourseAttempt { UserId = UserId, BookId = book.Id, BookPuzzleId = failed.Id, Solved = false, AttemptedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var status = await _svc.GetLineStatusAsync(UserId, book.Id, isAdmin: false);

        Assert.Equal(new[] { solved.Id }, status.SolvedIds);
        Assert.Equal(new[] { failed.Id }, status.FailedIds);
        Assert.DoesNotContain(untouched.Id, status.SolvedIds);
        Assert.DoesNotContain(untouched.Id, status.FailedIds);
    }

    [Fact]
    public async Task GetLineStatus_ThrowsWhenNoAccess()
    {
        // Buch eines anderen Users, keine Freigabe → kein Zugriff.
        var book = new Book { FileName = "o.pgn", DisplayName = "O", OwnerUserId = 999, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.GetLineStatusAsync(UserId, book.Id, isAdmin: false));
    }
}
