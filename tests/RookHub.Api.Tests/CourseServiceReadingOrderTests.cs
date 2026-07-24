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

    /// <summary>Regression: ein Kapitel, das NUR aus Info-/Erklärlinien besteht (Chessable-Intro),
    /// erscheint nicht in <c>GetChaptersAsync</c> — die Kapitel-Index-Auflösung von
    /// <c>?chapterIndex</c> muss denselben (gefilterten) Index-Raum verwenden, sonst verschiebt
    /// sich ab dem Info-Kapitel jedes Kapitel um eins und der Solver liefert Puzzles des
    /// FALSCHEN Kapitels (bzw. „completed" fürs leere Info-Kapitel).</summary>
    [Fact]
    public async Task ChapterIndex_SkipsInfoOnlyChapters_MatchesGetChapters()
    {
        var book = new Book { FileName = "ix.pgn", DisplayName = "IX", OwnerUserId = UserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        await AddPuzzleAsync(book, "intro1", "001.001", "Introduction", info: true); // reines Info-Kapitel
        var a = await AddPuzzleAsync(book, "a1", "002.001", "Tactics A");
        var b = await AddPuzzleAsync(book, "b1", "003.001", "Tactics B");

        var chapters = await _svc.GetChaptersAsync(UserId, book.Id, isAdmin: false);
        Assert.Equal(new[] { "Tactics A", "Tactics B" }, chapters.Select(c => c.Name).ToArray());

        // Index 0 (laut Frontend-Liste „Tactics A") muss auch Tactics A liefern — nicht die Intro.
        var next0 = await _svc.GetNextAsync(UserId, book.Id, "sequential", after: null, exclude: null, isAdmin: false, chapterIndex: 0);
        Assert.NotNull(next0.Puzzle);
        Assert.Equal(a.LineId, next0.Puzzle!.LineId);

        var next1 = await _svc.GetNextAsync(UserId, book.Id, "sequential", after: null, exclude: null, isAdmin: false, chapterIndex: 1);
        Assert.NotNull(next1.Puzzle);
        Assert.Equal(b.LineId, next1.Puzzle!.LineId);
    }

    /// <summary>Info-/Erklärlinien zählen NICHT in <c>PuzzleCount</c> (nur Quiz-Linien), werden aber
    /// je Kapitel in <c>InfoCount</c> ausgewiesen (Klammer-Anzeige in der Übersicht, entspricht der
    /// Chessable-Linienzahl). Ein reines Info-Kapitel bekommt weiterhin KEINE eigene Gruppe/Index.</summary>
    [Fact]
    public async Task GetChapters_ReportsInfoCount_PerChapter_WithoutInflatingPuzzleCount()
    {
        var book = new Book { FileName = "ic.pgn", DisplayName = "IC", OwnerUserId = UserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        // Kapitel „7. Promotion": 2 Quiz-Linien + 3 Info-/Erklärseiten.
        await AddPuzzleAsync(book, "p-intro", "007.001", "7. Promotion", info: true);
        await AddPuzzleAsync(book, "p-q1", "007.002", "7. Promotion");
        await AddPuzzleAsync(book, "p-info2", "007.003", "7. Promotion", info: true);
        await AddPuzzleAsync(book, "p-q2", "007.004", "7. Promotion");
        await AddPuzzleAsync(book, "p-info3", "007.005", "7. Promotion", info: true);
        // Ein reines Info-Kapitel darf keine Gruppe bilden (und sein InfoCount taucht nirgends auf).
        await AddPuzzleAsync(book, "only-info", "008.001", "8. Nur Info", info: true);

        var chapters = await _svc.GetChaptersAsync(UserId, book.Id, isAdmin: false);

        var promo = Assert.Single(chapters);
        Assert.Equal("7. Promotion", promo.Name);
        Assert.Equal(2, promo.PuzzleCount);   // nur Quiz-Linien
        Assert.Equal(3, promo.InfoCount);     // Info-Seiten separat
    }
}
