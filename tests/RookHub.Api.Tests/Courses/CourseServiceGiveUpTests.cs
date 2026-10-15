using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Courses;
using RookHub.Api.DTOs.Courses;

namespace RookHub.Api.Tests.Courses;

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
        _svc = TestServices.Course(_db);
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
        var book = new Book { FileName = "b.pgn", DisplayName = "B", OwnerUserId = UserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource() };
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

    [Theory]
    [InlineData("sequential")]
    [InlineData("random")]
    public async Task RetryFailed_FailedLineReturns_SolvedStaysSolved(string mode)
    {
        var (book, p1, p2) = await SeedAsync();
        await _svc.RecordResultAsync(UserId, book.Id, new RecordCourseResultDto { BookPuzzleId = p1.Id, Solved = true, Mode = mode }, isAdmin: false);
        await GiveUpAsync(book.Id, p2.Id);
        var done = await _svc.GetNextAsync(UserId, book.Id, mode, after: null, exclude: null, isAdmin: false, chapterIndex: null);
        Assert.Null(done.Puzzle);   // Runde durch, p2 gesperrt

        // „Mit den restlichen weiter" (Kurs 18: 946 gelöst, 55 gescheitert) — darf NICHT zurücksetzen.
        await _svc.RetryFailedAsync(UserId, book.Id, isAdmin: false);

        var next = await _svc.GetNextAsync(UserId, book.Id, mode, after: null, exclude: null, isAdmin: false, chapterIndex: null);
        Assert.Equal(p2.LineId, next.Puzzle?.LineId);
        Assert.Equal(1, next.SolvedCount);   // p1 bleibt gelöst
    }

    [Fact]
    public async Task SkipInfoLines_SequentialShowsOnlyQuizLines()
    {
        var (book, p1, _) = await SeedAsync();
        var info = await AddPuzzleAsync(book, "info", "000");
        info.IsInfoOnly = true;
        await _db.SaveChangesAsync();

        var withInfo = await _svc.GetNextAsync(UserId, book.Id, "sequential", after: null, exclude: null, isAdmin: false, chapterIndex: null);
        Assert.Equal("info", withInfo.Puzzle?.LineId);

        Assert.True(await _svc.SetSkipInfoLinesAsync(UserId, book.Id, true, isAdmin: false));
        var skipped = await _svc.GetNextAsync(UserId, book.Id, "sequential", after: null, exclude: null, isAdmin: false, chapterIndex: null);
        Assert.Equal(p1.LineId, skipped.Puzzle?.LineId);

        Assert.False(await _svc.SetSkipInfoLinesAsync(UserId, book.Id, false, isAdmin: false));
        var again = await _svc.GetNextAsync(UserId, book.Id, "sequential", after: null, exclude: null, isAdmin: false, chapterIndex: null);
        Assert.Equal("info", again.Puzzle?.LineId);
    }

    [Fact]
    public async Task Stats_SplitCleanHelpedGaveUp()
    {
        var (book, p1, p2) = await SeedAsync();
        var p3 = await AddPuzzleAsync(book, "p3", "003");
        var p4 = await AddPuzzleAsync(book, "p4", "004");
        Task Rec(BookPuzzle p, bool solved, int hints = 0) =>
            _svc.RecordResultAsync(UserId, book.Id, new RecordCourseResultDto { BookPuzzleId = p.Id, Solved = solved, Mode = "sequential", HintsUsed = hints }, isAdmin: false);

        await Rec(p1, true);                 // ohne Hilfe
        await Rec(p2, true, hints: 1);       // mit Tipp
        await Rec(p3, false);                // Fehler …
        await Rec(p3, true);                 // … dann gelöst (Neustart) → mit Hilfe
        await Rec(p4, false);                // aufgegeben

        var next = await _svc.GetNextAsync(UserId, book.Id, "sequential", after: null, exclude: null, isAdmin: false, chapterIndex: null);
        var bk = next.Book!;
        Assert.Equal(3, bk.SolvedCount);
        Assert.Equal(1, bk.CleanCount);
        Assert.Equal(2, bk.HelpedCount);
        Assert.Equal(1, bk.GaveUpCount);
    }
}
