using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Aufgeben (Versuch mit Solved=false) im Kurs zählt als GESCHEITERT und fällt bis zum nächsten
/// Reset aus dem Pool — auch im sequenziellen Modus. Regression: vorher schloss der seq-Pool nur
/// gelöste Puzzles aus, sodass ein aufgegebenes Puzzle beim Neustart sofort wieder erschien.
/// </summary>
public class CourseServiceGiveUpTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseService _svc;
    private const int UserId = 1;

    public CourseServiceGiveUpTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new CourseService(_db, NullLogger<CourseService>.Instance, new PgnImportService(_db), new BookAdminService(_db),
            new RepertoireService(_db, new RepertoireAnalyzeService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()))));
    }

    public void Dispose() => _db.Dispose();

    private async Task<BookPuzzle> AddPuzzleAsync(Book book, string lineId, string round)
    {
        var p = new BookPuzzle
        {
            LineId = lineId,
            BookFileName = book.FileName,
            BookId = book.Id,
            Round = round,
            Chapter = "Ch",
            Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            Moves = "e2e4",
        };
        _db.BookPuzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    private async Task<(Book book, BookPuzzle p1, BookPuzzle p2)> SeedAsync()
    {
        var book = new Book { FileName = "b.pgn", DisplayName = "B", OwnerUserId = UserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var p1 = await AddPuzzleAsync(book, "p1", "001");
        var p2 = await AddPuzzleAsync(book, "p2", "002");
        return (book, p1, p2);
    }

    private Task GiveUpAsync(int bookId, int puzzleId) =>
        _svc.RecordResultAsync(UserId, bookId, new RecordCourseResultDto { BookPuzzleId = puzzleId, Solved = false, Mode = "sequential" }, isAdmin: false);

    [Fact]
    public async Task SequentialNext_AfterGiveUp_SkipsFailedPuzzleOnRestart()
    {
        var (book, p1, p2) = await SeedAsync();

        await GiveUpAsync(book.Id, p1.Id);

        // Neustart (kein after-Cursor) → nicht mehr p1, sondern p2.
        var next = await _svc.GetNextAsync(UserId, book.Id, "sequential", after: null, exclude: null, isAdmin: false, chapterIndex: null);

        Assert.NotNull(next.Puzzle);
        Assert.Equal(p2.LineId, next.Puzzle!.LineId);
        Assert.False(next.Completed);
    }

    [Fact]
    public async Task SequentialNext_AllGivenUp_ReportsRoundDone()
    {
        var (book, p1, p2) = await SeedAsync();

        await GiveUpAsync(book.Id, p1.Id);
        await GiveUpAsync(book.Id, p2.Id);

        var next = await _svc.GetNextAsync(UserId, book.Id, "sequential", after: null, exclude: null, isAdmin: false, chapterIndex: null);

        Assert.Null(next.Puzzle);          // Pool leer (alle aufgegeben)
        Assert.True(next.Completed);       // Runde durch → UI zeigt „noch N übrig" + Von-vorn
        Assert.Equal(0, next.SolvedCount); // aber nichts wirklich gelöst
        Assert.Equal(2, next.Total);
    }

    [Fact]
    public async Task SequentialNext_AfterReset_FailedPuzzleReappears()
    {
        var (book, p1, _) = await SeedAsync();

        await GiveUpAsync(book.Id, p1.Id);
        await _svc.ResetAsync(UserId, book.Id, isAdmin: false);

        var next = await _svc.GetNextAsync(UserId, book.Id, "sequential", after: null, exclude: null, isAdmin: false, chapterIndex: null);

        Assert.NotNull(next.Puzzle);
        Assert.Equal(p1.LineId, next.Puzzle!.LineId);   // nach Reset wieder ganz vorne
    }
}
