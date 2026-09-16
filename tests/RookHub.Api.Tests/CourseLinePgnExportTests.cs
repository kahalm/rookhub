using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// PGN-Export einzelner Kapitel und Linien: bevorzugt unverändert aus dem Roh-PGN (Varianten und
/// Kommentare bleiben), nur Linien ohne Gegenstück dort werden rekonstruiert.
/// </summary>
public class CourseLinePgnExportTests : IDisposable
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private const string GameA1 = "[Event \"Kurs X\"]\n[Round \"001.001\"]\n[Black \"Kapitel A\"]\n[FEN \"" + StartFen + "\"]\n\n"
        + "1. e4 (1. d4 {Damengambit}) e5\n{zweite Zeile} *";
    private const string GameA2 = "[Event \"Kurs X\"]\n[Round \"001.002\"]\n[Black \"Kapitel A\"]\n[FEN \"" + StartFen + "\"]\n\n"
        + "1. d4 d5 *";
    private const string GameB1 = "[Event \"Kurs X\"]\n[Round \"002.001\"]\n[Black \"Kapitel B\"]\n[FEN \"" + StartFen + "\"]\n\n"
        + "1. c4 (1. Nf3 Nf6) e5 *";

    private readonly AppDbContext _db;
    private readonly CourseService _svc;

    public CourseLinePgnExportTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = TestServices.Course(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Book> SeedAsync(bool withSource = true)
    {
        var book = new Book
        {
            FileName = "kurs-x.pgn",
            DisplayName = "Kurs X",
            OwnerUserId = 1,
            SourcePgn = withSource ? GameA1 + "\n\n" + GameA2 + "\n\n" + GameB1 + "\n" : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        void Line(string round, string? chapter, string moves) => _db.BookPuzzles.Add(new BookPuzzle
        {
            LineId = $"kurs-x.pgn:{round}", BookId = book.Id, BookFileName = book.FileName,
            Round = round, Chapter = chapter, Title = $"Linie {round}", Fen = StartFen, Moves = moves,
        });
        // bewusst nicht in Lesereihenfolge angelegt
        Line("001.002", "Kapitel A", "d2d4 d7d5");
        Line("002.001", "Kapitel B", "c2c4 e7e5");
        Line("001.001", "Kapitel A", "e2e4 e7e5");
        Line("001.003", "Kapitel A", "g1f3");          // nur in der DB (z. B. von Hand ergänzt)
        Line("003.001", null, "e2e4");                 // ohne Kapitel
        await _db.SaveChangesAsync();
        return book;
    }

    private int LineId(string round) => _db.BookPuzzles.Single(p => p.Round == round).Id;

    [Fact]
    public async Task ChapterPgn_ReturnsOnlyThatChapter_VerbatimFromSource_InReadingOrder()
    {
        var book = await SeedAsync();

        var (pgn, fileName) = await _svc.GetChapterPgnAsync(1, book.Id, "Kapitel A", isAdmin: false);

        Assert.StartsWith(GameA1 + "\n\n" + GameA2 + "\n\n", pgn);   // Roh-Text inkl. Variante und Zeilenumbruch
        Assert.DoesNotContain("002.001", pgn);                         // anderes Kapitel
        Assert.Contains("[Round \"001.003\"]", pgn);                   // DB-only-Linie rekonstruiert …
        Assert.Contains("[Site \"RookHub\"]", pgn);                    // … über den Rekonstruktions-Export
        Assert.True(pgn.IndexOf("001.002") < pgn.IndexOf("001.003"));
        Assert.EndsWith("\n", pgn);
        Assert.Equal("Kurs_X_Kapitel_A.pgn", fileName);
    }

    [Fact]
    public async Task ChapterPgn_EmptyName_IsTheGroupWithoutChapter()
    {
        var book = await SeedAsync();

        var (pgn, fileName) = await _svc.GetChapterPgnAsync(1, book.Id, "  ", isAdmin: false);

        Assert.Contains("[Round \"003.001\"]", pgn);
        Assert.DoesNotContain("Kapitel A", pgn);
        Assert.Equal("Kurs_X_no_chapter.pgn", fileName);
    }

    [Fact]
    public async Task LinePgn_ReturnsExactlyTheRawGame()
    {
        var book = await SeedAsync();

        var (pgn, fileName) = await _svc.GetLinePgnAsync(1, book.Id, LineId("002.001"), isAdmin: false);

        Assert.Equal(GameB1 + "\n", pgn);
        Assert.Equal("Kurs_X_002_001_Linie_002_001.pgn", fileName);
    }

    [Fact]
    public async Task LinePgn_WithoutSourcePgn_IsRebuiltFromTheStoredLine()
    {
        var book = await SeedAsync(withSource: false);

        var (pgn, _) = await _svc.GetLinePgnAsync(1, book.Id, LineId("001.001"), isAdmin: false);

        Assert.Contains("[Round \"001.001\"]", pgn);
        Assert.Contains("1. e4 e5", pgn);
    }

    [Fact]
    public async Task CalculationBook_IsNotExported_TheMovesWouldBeTheSolution()
    {
        var book = await SeedAsync();
        book.IsCalculation = true;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.GetChapterPgnAsync(1, book.Id, "Kapitel A", isAdmin: true));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.GetLinePgnAsync(1, book.Id, LineId("001.001"), isAdmin: true));
    }

    [Fact]
    public async Task UnknownChapter_LineOfOtherBook_OrNoAccess_AreNotFound()
    {
        var book = await SeedAsync();
        var other = new Book { FileName = "other.pgn", DisplayName = "Other", OwnerUserId = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(other);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.GetChapterPgnAsync(1, book.Id, "Gibt es nicht", false));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.GetLinePgnAsync(1, other.Id, LineId("001.001"), false));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.GetLinePgnAsync(2, book.Id, LineId("001.001"), false));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.GetChapterPgnAsync(2, book.Id, "Kapitel A", false));
    }
}
