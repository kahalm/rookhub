using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Lesereihenfolge eines Kurses richtet sich nach <see cref="BookPuzzle.Round"/> (Chessable-
/// Zeilennummer), NICHT nach der DB-Id: nachträglich re-gefetchte Linien (z. B. eine Intro-/
/// Info-Linie, die beim Erstimport fehlte) bekommen höhere Ids, gehören aber der Round nach
/// nach vorn. Regression zu „100 Tactical Patterns": die Intro (Round 002.002, hohe Id)
/// erschien fälschlich hinter 002.003/002.004.
/// </summary>
public class CourseServiceReadingOrderTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseService _svc;
    private const int UserId = 1;

    public CourseServiceReadingOrderTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new CourseService(_db, NullLogger<CourseService>.Instance, new PgnImportService(_db), new BookAdminService(_db), new RepertoireService(_db, new RepertoireAnalyzeService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()))));
    }

    public void Dispose() => _db.Dispose();

    private async Task<BookPuzzle> AddPuzzleAsync(Book book, string lineId, string round, string chapter, bool info = false)
    {
        var p = new BookPuzzle
        {
            LineId = lineId,
            BookFileName = book.FileName,
            BookId = book.Id,
            Round = round,
            Chapter = chapter,
            Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            Moves = "e2e4",
            IsInfoOnly = info,
        };
        _db.BookPuzzles.Add(p);
        await _db.SaveChangesAsync();  // Insert-Reihenfolge = aufsteigende Id (InMemory)
        return p;
    }

    /// <summary>Buch, dessen Id-Reihenfolge BEWUSST von der Round-Reihenfolge abweicht:
    /// die beiden Quiz-Linien werden zuerst angelegt (niedrige Ids), die Intro-Info-Linie
    /// (kleinstes Round) zuletzt (höchste Id) — genau die Re-Fetch-Situation.</summary>
    private async Task<(Book book, BookPuzzle intro, BookPuzzle q3, BookPuzzle q4)> SeedReorderedBookAsync()
    {
        var book = new Book { FileName = "tac.pgn", DisplayName = "Tactics", OwnerUserId = UserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        var q3 = await AddPuzzleAsync(book, "q3", "002.003", "Introduction");             // niedrige Id
        var q4 = await AddPuzzleAsync(book, "q4", "002.004", "Introduction");             // niedrige Id
        var intro = await AddPuzzleAsync(book, "intro", "002.002", "Introduction", info: true); // HÖCHSTE Id, kleinstes Round
        Assert.True(intro.Id > q3.Id && intro.Id > q4.Id);
        return (book, intro, q3, q4);
    }

    [Fact]
    public async Task SequentialNext_ReturnsLowestRoundFirst_EvenWithHigherId()
    {
        var (book, intro, _, _) = await SeedReorderedBookAsync();

        var next = await _svc.GetNextAsync(UserId, book.Id, "sequential", after: null, exclude: null, isAdmin: false, chapterIndex: null);

        Assert.NotNull(next.Puzzle);
        Assert.Equal("002.002", next.Puzzle!.Round);     // die Intro zuerst …
        Assert.Equal(intro.LineId, next.Puzzle.LineId);
        Assert.True(next.Puzzle.IsInfoOnly);
    }

    [Fact]
    public async Task SequentialNext_AfterIntro_AdvancesByRound()
    {
        var (book, intro, q3, _) = await SeedReorderedBookAsync();

        // … dann kommt Round 002.003, obwohl die Intro die HÖHERE Id hat.
        var next = await _svc.GetNextAsync(UserId, book.Id, "sequential", after: intro.Id, exclude: null, isAdmin: false, chapterIndex: null);

        Assert.NotNull(next.Puzzle);
        Assert.Equal("002.003", next.Puzzle!.Round);
        Assert.Equal(q3.LineId, next.Puzzle.LineId);
    }

    [Fact]
    public async Task GetAllPuzzles_AreInRoundOrder()
    {
        var (book, _, _, _) = await SeedReorderedBookAsync();

        var all = await _svc.GetAllPuzzlesAsync(UserId, book.Id, isAdmin: false);

        Assert.Equal(new[] { "002.002", "002.003", "002.004" }, all.Select(p => p.Round).ToArray());
    }

    [Fact]
    public async Task Chapters_OrderedByRound_AcrossChapters()
    {
        var book = new Book { FileName = "c.pgn", DisplayName = "C", OwnerUserId = UserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        // „Kapitel 1" zuerst eingefügt (niedrige Ids) — aber „Intro" hat das kleinere Round und
        // wird (mit höheren Ids) danach eingefügt. Round muss die Kapitelreihenfolge bestimmen.
        await AddPuzzleAsync(book, "c1a", "003.001", "1. Double Attack");
        await AddPuzzleAsync(book, "c1b", "003.002", "1. Double Attack");
        await AddPuzzleAsync(book, "i1", "002.003", "Introduction");

        var chapters = await _svc.GetChaptersAsync(UserId, book.Id, isAdmin: false);

        Assert.Equal(new[] { "Introduction", "1. Double Attack" }, chapters.Select(c => c.Name).ToArray());
    }
}
