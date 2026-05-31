using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class PgnImportServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PgnImportService _service;

    public PgnImportServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new PgnImportService(_db);
    }

    public void Dispose() => _db.Dispose();

    private const string SamplePgn = @"
[Event ""Test Book""]
[Round ""1.1""]
[White ""Italian Idea""]
[Black ""Kapitel 5""]
[Result ""*""]
[SetUp ""1""]
[FEN ""rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2""]

{ [%tqu ""En"",""Finde den Zug""] Die Pointe ist Entwicklung. } 2.Nf3 Nc6 (2... d6 3. d4) 3. Bb5 $1 a6 *
";

    [Fact]
    public void ParsePgn_ExtractsMainlineAsUci_StrippingVariationsNagsNumbers()
    {
        var puzzles = PgnImportService.ParsePgn("book.pgn", SamplePgn);

        var p = Assert.Single(puzzles);
        Assert.Equal("book.pgn:1.1", p.LineId);
        Assert.Equal("1.1", p.Round);
        Assert.Equal("g1f3 b8c6 f1b5 a7a6", p.Moves);   // variation (2... d6) entfernt
        Assert.Equal("Italian Idea", p.Title);
        Assert.Equal("Kapitel 5", p.Chapter);
        Assert.Equal("Die Pointe ist Entwicklung.", p.Comment); // [%tqu] entfernt
    }

    [Fact]
    public void ParsePgn_HandlesPromotion()
    {
        var pgn = @"
[Event ""P""]
[Round ""1""]
[FEN ""8/P7/8/8/8/8/8/k6K w - - 0 1""]

1. a8=Q+ Kb2 *
";
        var p = Assert.Single(PgnImportService.ParsePgn("p.pgn", pgn));
        Assert.Equal("a7a8q a1b2", p.Moves);
    }

    [Fact]
    public void ParsePgn_HandlesCastling()
    {
        var pgn = @"
[Event ""C""]
[Round ""1""]
[FEN ""r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1""]

1. O-O O-O-O *
";
        var p = Assert.Single(PgnImportService.ParsePgn("c.pgn", pgn));
        Assert.Equal("e1g1 e8c8", p.Moves);
    }

    [Fact]
    public void ParsePgn_SkipsEntriesWithoutFenOrRound()
    {
        var pgn = @"
[Event ""NoFen""]
[Round ""1""]

1. e4 e5 *

[Event ""QuestionFen""]
[Round ""2""]
[FEN ""?""]

1. e4 *

[Event ""NoRound""]
[FEN ""rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1""]

1. e4 *
";
        Assert.Empty(PgnImportService.ParsePgn("skip.pgn", pgn));
    }

    [Fact]
    public void ParsePgn_SkipsGameWithIllegalSan()
    {
        var pgn = @"
[Event ""Bad""]
[Round ""1""]
[FEN ""rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1""]

1. Qh5 *
";
        // Qh5 ist aus der Grundstellung illegal → Eintrag wird übersprungen, nicht geworfen.
        Assert.Empty(PgnImportService.ParsePgn("bad.pgn", pgn));
    }

    [Fact]
    public void ParsePgn_MultipleGames()
    {
        var pgn = @"
[Event ""A""]
[Round ""1""]
[FEN ""rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1""]

1. e4 e5 *

[Event ""A""]
[Round ""2""]
[FEN ""rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1""]

1. d4 d5 *
";
        var puzzles = PgnImportService.ParsePgn("multi.pgn", pgn);
        Assert.Equal(2, puzzles.Count);
        Assert.Equal("e2e4 e7e5", puzzles[0].Moves);
        Assert.Equal("d2d4 d7d5", puzzles[1].Moves);
    }

    [Fact]
    public void ParsePgn_MidlineTqu_SetsStartPly_AndKeepsFullGame()
    {
        // [%tqu] hängt an 2...Nc6 (Index 3) → Setup-Zug, gelöst wird ab moves[4] (Bb5).
        var pgn = @"
[Event ""T""]
[Round ""1""]
[FEN ""rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1""]

1. e4 e5 2. Nf3 Nc6 {[%tqu ""En"",""find"","""","""",""f1b5"","""",10]} 3. Bb5 a6 *
";
        var p = Assert.Single(PgnImportService.ParsePgn("t.pgn", pgn));
        // Ganze Partie bleibt erhalten:
        Assert.Equal("e2e4 e7e5 g1f3 b8c6 f1b5 a7a6", p.Moves);
        Assert.Equal("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", p.Fen);
        // Trainingsstart bei Index 3 (der [%tqu]-Zug Nc6); Lösung ab moves[4] = f1b5:
        Assert.Equal(3, p.StartPly);
    }

    [Fact]
    public void ParsePgn_RootTqu_StartPlyMinusOne()
    {
        // [%tqu] vor dem ersten Zug → FEN ist bereits die Trainingsstellung, lösen ab moves[0].
        var pgn = @"
[Event ""T""]
[Round ""1""]
[FEN ""r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 0 1""]

{[%tqu ""En"",""find"",""""]} 1. Ng5 d5 *
";
        var p = Assert.Single(PgnImportService.ParsePgn("t.pgn", pgn));
        Assert.Equal("f3g5 d7d5", p.Moves);
        Assert.Equal(-1, p.StartPly);
    }

    [Fact]
    public void ParsePgn_NoTqu_StartPlyZero()
    {
        var pgn = @"
[Event ""T""]
[Round ""1""]
[FEN ""rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1""]

1. e4 e5 2. Nf3 *
";
        var p = Assert.Single(PgnImportService.ParsePgn("t.pgn", pgn));
        Assert.Equal(0, p.StartPly);
    }

    [Fact]
    public void CleanDisplayName_StripsSuffixes()
    {
        Assert.Equal("My Book", PgnImportService.CleanDisplayName("My Book_firstkey.pgn"));
        Assert.Equal("Other", PgnImportService.CleanDisplayName("Other.pgn"));
        Assert.Equal("Plain", PgnImportService.CleanDisplayName("Plain"));
    }

    [Fact]
    public async Task ImportFileAsync_CreatesBookAndPuzzles()
    {
        var item = await _service.ImportFileAsync("Test Book_firstkey.pgn", SamplePgn, default);

        Assert.Equal(1, item.Imported);
        Assert.Equal(0, item.Skipped);

        var book = await _db.Books.SingleAsync();
        Assert.Equal("Test Book_firstkey.pgn", book.FileName);
        Assert.Equal("Test Book", book.DisplayName);

        var puzzle = await _db.BookPuzzles.SingleAsync();
        Assert.Equal(book.Id, puzzle.BookId);
        Assert.Equal("Test Book_firstkey.pgn:1.1", puzzle.LineId);
    }

    [Fact]
    public async Task ImportFileAsync_DedupesOnReimport()
    {
        await _service.ImportFileAsync("Test Book_firstkey.pgn", SamplePgn, default);
        var second = await _service.ImportFileAsync("Test Book_firstkey.pgn", SamplePgn, default);

        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Skipped);
        Assert.Equal(1, await _db.BookPuzzles.CountAsync());
        Assert.Equal(1, await _db.Books.CountAsync()); // kein zweites Buch
    }
}
