using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
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
        var (puzzles, invalid) = PgnImportService.ParsePgn("book.pgn", SamplePgn);

        var p = Assert.Single(puzzles);
        Assert.Equal(0, invalid);
        Assert.Equal("book.pgn:1.1", p.LineId);
        Assert.Equal("1.1", p.Round);
        Assert.Equal("g1f3 b8c6 f1b5 a7a6", p.Moves);   // variation (2... d6) entfernt
        Assert.Equal("Italian Idea", p.Title);
        Assert.Equal("Kapitel 5", p.Chapter);
        Assert.Equal("Die Pointe ist Entwicklung.", p.Comment); // [%tqu] entfernt
        // Einleitungskommentar (vor dem ersten Zug) landet unter Schlüssel -1, [%tqu] entfernt.
        Assert.NotNull(p.MoveComments);
        Assert.Equal("Die Pointe ist Entwicklung.", p.MoveComments![-1]);
    }

    [Fact]
    public void ParsePgn_CollectsPerMoveComments_KeyedByHalfMoveIndex_IgnoringVariations()
    {
        var pgn = @"
[Event ""X""]
[Round ""1""]
[FEN ""rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2""]

{Intro.} 2. Nf3 {Develops the knight.} Nc6 (2... d6 {Philidor — Nebenvariante.}) 3. Bb5 {The pin.} a6 *
";
        var p = Assert.Single(PgnImportService.ParsePgn("c.pgn", pgn).Puzzles);
        Assert.Equal("g1f3 b8c6 f1b5 a7a6", p.Moves);
        Assert.NotNull(p.MoveComments);
        var mc = p.MoveComments!;
        Assert.Equal("Intro.", mc[-1]);              // vor dem ersten Zug
        Assert.Equal("Develops the knight.", mc[0]);  // nach Nf3 (Halbzug 0)
        Assert.Equal("The pin.", mc[2]);              // nach Bb5 (Halbzug 2)
        Assert.False(mc.ContainsKey(1));              // Nc6 hat keinen Kommentar
        Assert.False(mc.ContainsKey(3));              // a6 hat keinen Kommentar
        // Varianten-Kommentar (Philidor) wird NICHT mitgezählt.
        Assert.DoesNotContain(mc.Values, v => v.Contains("Philidor"));
    }

    [Fact]
    public async Task ImportFileAsync_PersistsMoveComments_RoundtripsThroughMapToDto()
    {
        var pgn = @"
[Event ""X""]
[Round ""1""]
[FEN ""rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2""]

{Intro.} 2. Nf3 {Develops the knight.} Nc6 3. Bb5 {The pin.} a6 *
";
        await _service.ImportFileAsync("rt.pgn", pgn, CancellationToken.None);

        var bp = await _db.BookPuzzles.SingleAsync();
        Assert.NotNull(bp.MoveComments);                       // als JSON-String in der DB

        var dto = BookPuzzleService.MapToDto(bp);              // deserialisiert wieder zur Map
        Assert.NotNull(dto.MoveComments);
        Assert.Equal("Intro.", dto.MoveComments![-1]);
        Assert.Equal("Develops the knight.", dto.MoveComments![0]);
        Assert.Equal("The pin.", dto.MoveComments![2]);
    }

    [Fact]
    public async Task ImportFileAsync_StoresSourcePgnAndCurrentVersion()
    {
        var pgn = @"
[Event ""X""]
[Round ""1""]
[FEN ""rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2""]

2. Nf3 Nc6 3. Bb5 a6 *
";
        var res = await _service.ImportFileAsync("v.pgn", pgn, CancellationToken.None);
        var book = await _db.Books.SingleAsync();
        Assert.Equal(pgn, book.SourcePgn);
        Assert.Equal(ImportPipeline.CurrentVersion, book.ImportVersion);
        Assert.True(res.Imported > 0);
        Assert.Equal(0, res.Updated);
    }

    [Fact]
    public async Task ImportFileAsync_StaleBook_UpdatesExistingLinesInPlace_PreservingId()
    {
        const string lineId = "stale.pgn:1";
        // Altbestand simulieren: Buch + Linie OHNE MoveComments, Version 0 (veraltet).
        var book = new RookHub.Api.Models.Book { FileName = "stale.pgn", DisplayName = "Stale", ImportVersion = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var old = new RookHub.Api.Models.BookPuzzle
        {
            LineId = lineId, BookFileName = "stale.pgn", BookId = book.Id, Round = "1",
            Fen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
            Moves = "g1f3 b8c6 f1b5 a7a6", StartPly = -1, MoveComments = null,
        };
        _db.BookPuzzles.Add(old);
        await _db.SaveChangesAsync();
        var originalId = old.Id;

        // Re-Import derselben Datei MIT Pro-Zug-Kommentaren → muss die bestehende Linie aktualisieren.
        var pgn = @"
[Event ""X""]
[Round ""1""]
[FEN ""rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2""]

2. Nf3 {Develops.} Nc6 3. Bb5 {The pin.} a6 *
";
        var res = await _service.ImportFileAsync("stale.pgn", pgn, CancellationToken.None);

        Assert.Equal(1, res.Updated);
        Assert.Equal(0, res.Imported);
        var refreshed = await _db.BookPuzzles.SingleAsync(bp => bp.LineId == lineId);
        Assert.Equal(originalId, refreshed.Id);            // Id (und damit Fortschritt-FKs) erhalten
        Assert.NotNull(refreshed.MoveComments);            // Kommentare nachgezogen
        Assert.Contains("Develops.", refreshed.MoveComments!);
        Assert.Equal(ImportPipeline.CurrentVersion, (await _db.Books.SingleAsync(b => b.Id == book.Id)).ImportVersion);
    }

    [Fact]
    public async Task ImportFileAsync_CurrentVersionBook_SkipsExistingInsteadOfUpdating()
    {
        var pgn = @"
[Event ""X""]
[Round ""1""]
[FEN ""rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2""]

2. Nf3 Nc6 3. Bb5 a6 *
";
        await _service.ImportFileAsync("idem.pgn", pgn, CancellationToken.None);   // legt an, Version = current
        var res2 = await _service.ImportFileAsync("idem.pgn", pgn, CancellationToken.None); // erneut → skip

        Assert.Equal(0, res2.Imported);
        Assert.Equal(0, res2.Updated);
        Assert.True(res2.Skipped > 0);
    }

    [Fact]
    public void ParsePgn_NoComments_LeavesMoveCommentsNull()
    {
        var pgn = @"
[Event ""X""]
[Round ""1""]
[FEN ""8/P7/8/8/8/8/8/k6K w - - 0 1""]

1. a8=Q+ Kb2 *
";
        var p = Assert.Single(PgnImportService.ParsePgn("n.pgn", pgn).Puzzles);
        Assert.Null(p.MoveComments);
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
        var p = Assert.Single(PgnImportService.ParsePgn("p.pgn", pgn).Puzzles);
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
        var p = Assert.Single(PgnImportService.ParsePgn("c.pgn", pgn).Puzzles);
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
        var skipResult = PgnImportService.ParsePgn("skip.pgn", pgn);
        Assert.Empty(skipResult.Puzzles);
        Assert.Equal(3, skipResult.Invalid); // alle drei Eintraege zaehlen als "Invalid"
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
        var bad = PgnImportService.ParsePgn("bad.pgn", pgn);
        Assert.Empty(bad.Puzzles);
        Assert.Equal(1, bad.Invalid);
    }

    [Fact]
    public void ParsePgn_MultipleGames()
    {
        // Nicht-Grundstellung (FEN = Puzzle-Stellung) → beide werden behalten (StartPly=-1).
        var pgn = @"
[Event ""A""]
[Round ""1""]
[FEN ""7k/8/8/8/8/8/6PP/7K w - - 0 1""]

1. g4 *

[Event ""A""]
[Round ""2""]
[FEN ""k7/8/8/8/8/8/PP6/K7 w - - 0 1""]

1. a4 *
";
        var multi = PgnImportService.ParsePgn("multi.pgn", pgn);
        Assert.Equal(2, multi.Puzzles.Count);
        Assert.Equal(0, multi.Invalid);
        Assert.Equal("g2g4", multi.Puzzles[0].Moves);
        Assert.Equal("a2a4", multi.Puzzles[1].Moves);
        Assert.Equal(-1, multi.Puzzles[0].StartPly);
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
        var p = Assert.Single(PgnImportService.ParsePgn("t.pgn", pgn).Puzzles);
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
        var p = Assert.Single(PgnImportService.ParsePgn("t.pgn", pgn).Puzzles);
        Assert.Equal("f3g5 d7d5", p.Moves);
        Assert.Equal(-1, p.StartPly);
    }

    [Fact]
    public void ParsePgn_NoTqu_NonStartFen_SolvesFromMove0()
    {
        // FEN ist die Puzzle-Stellung (kein Marker) → StartPly=-1, lösen ab moves[0].
        var pgn = @"
[Event ""T""]
[Round ""1""]
[FEN ""1q5r/4kpp1/Q2p1n1p/1r2p3/8/4B3/1P3PPP/R2R2K1 w - - 0 1""]

1. Bxa7 *
";
        var p = Assert.Single(PgnImportService.ParsePgn("t.pgn", pgn).Puzzles);
        Assert.Equal(-1, p.StartPly);
        Assert.Equal("e3a7", p.Moves);
    }

    [Fact]
    public void ParsePgn_StartFenWithoutMarker_Skipped()
    {
        // Grundstellung ohne Trainingsmarker = ganze Partie ohne Puzzle → übersprungen.
        var pgn = @"
[Event ""T""]
[Round ""1""]
[FEN ""rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1""]

1. e4 e5 2. Nf3 *
";
        var startSkip = PgnImportService.ParsePgn("t.pgn", pgn);
        Assert.Empty(startSkip.Puzzles);
        Assert.Equal(1, startSkip.Invalid);
    }

    [Fact]
    public void ParsePgn_InfoMarker_SetsIsInfoOnly_AndKeepsComment()
    {
        // piratechess markiert Chessable-Info-/Erklärlinien mit [%info] (im Kommentar vor dem 1. Zug).
        var pgn = @"
[Event ""T""]
[Round ""1""]
[FEN ""1q5r/4kpp1/Q2p1n1p/1r2p3/8/4B3/1P3PPP/R2R2K1 w - - 0 1""]

{[%info] Nur zur Erklärung.} 1. Bxa7 *
";
        var p = Assert.Single(PgnImportService.ParsePgn("t.pgn", pgn).Puzzles);
        Assert.True(p.IsInfoOnly);
        Assert.Equal("Nur zur Erklärung.", p.Comment);   // [%info]-Annotation rausgefiltert
    }

    [Fact]
    public void ParsePgn_NoInfoMarker_IsInfoOnlyFalse()
    {
        var p = Assert.Single(PgnImportService.ParsePgn("book.pgn", SamplePgn).Puzzles);
        Assert.False(p.IsInfoOnly);
    }

    [Fact]
    public void ParsePgn_InfoMarker_StartFen_NotSkipped()
    {
        // Anders als marker-lose Grundstellungen bleiben Info-Linien erhalten (zum Durchklicken).
        var pgn = @"
[Event ""T""]
[Round ""1""]
[FEN ""rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1""]

{[%info]} 1. e4 e5 2. Nf3 *
";
        var result = PgnImportService.ParsePgn("t.pgn", pgn);
        var p = Assert.Single(result.Puzzles);
        Assert.True(p.IsInfoOnly);
        Assert.Equal(0, result.Invalid);
    }

    [Fact]
    public async Task ImportFileAsync_InfoLine_RoundtripsIsInfoOnly()
    {
        var pgn = @"
[Event ""X""]
[Round ""1""]
[FEN ""1q5r/4kpp1/Q2p1n1p/1r2p3/8/4B3/1P3PPP/R2R2K1 w - - 0 1""]

{[%info] Erklärlinie.} 1. Bxa7 *
";
        await _service.ImportFileAsync("info.pgn", pgn, CancellationToken.None);

        var bp = await _db.BookPuzzles.SingleAsync();
        Assert.True(bp.IsInfoOnly);
        Assert.True(BookPuzzleService.MapToDto(bp).IsInfoOnly);
    }

    // ---- Zug-lose Erklär-/Intro-Seiten (Kommentar, keine Züge) -------------
    private const string CommentOnlyPgn = @"
[Event ""Course""]
[Round ""002.002""]
[White ""Introduction #1""]
[Black ""Introduction""]
[FEN ""rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1""]

{Welcome to the course. This page only explains things — there are no moves.}
";

    [Fact]
    public void ParsePgn_CommentOnlyLine_KeptAsInfoLine_WhenFlagSet()
    {
        var result = PgnImportService.ParsePgn("c.pgn", CommentOnlyPgn, keepCommentOnlyAsInfo: true);
        var p = Assert.Single(result.Puzzles);
        Assert.Equal(0, result.Invalid);
        Assert.True(p.IsInfoOnly);
        Assert.Equal("e2e4", p.Moves);                 // synthetischer Fake-Zug
        Assert.Equal("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", p.Fen);
        Assert.Contains("only explains things", p.Comment);   // Erklärtext bleibt erhalten
        Assert.Equal("Introduction #1", p.Title);
    }

    [Fact]
    public void ParsePgn_CommentOnlyLine_DroppedByDefault()
    {
        // Default (z. B. Wochenpost) unverändert: zug-lose Linie wird verworfen.
        var result = PgnImportService.ParsePgn("c.pgn", CommentOnlyPgn);
        Assert.Empty(result.Puzzles);
        Assert.Equal(1, result.Invalid);
    }

    [Fact]
    public void ParsePgn_NoMoveNoComment_DroppedEvenWithFlag()
    {
        var pgn = @"
[Event ""C""]
[Round ""1""]
[FEN ""rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1""]

*
";
        var result = PgnImportService.ParsePgn("c.pgn", pgn, keepCommentOnlyAsInfo: true);
        Assert.Empty(result.Puzzles);   // ohne Text nichts anzuzeigen → Skip
        Assert.Equal(1, result.Invalid);
    }

    [Fact]
    public async Task ImportFileAsync_KeepsCommentOnlyLine_AsInfoLine()
    {
        await _service.ImportFileAsync("intro.pgn", CommentOnlyPgn, CancellationToken.None);

        var bp = await _db.BookPuzzles.SingleAsync();
        Assert.True(bp.IsInfoOnly);
        Assert.Equal("e2e4", bp.Moves);
        Assert.Contains("only explains things", bp.Comment);
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
        Assert.Equal(0, item.Invalid);

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
        Assert.Equal(0, second.Invalid);
        Assert.Equal(1, await _db.BookPuzzles.CountAsync());
        Assert.Equal(1, await _db.Books.CountAsync()); // kein zweites Buch
    }

    [Fact]
    public async Task ImportFileAsync_CountsParseSkipsAsInvalid()
    {
        // Mix: 1 gueltiges Puzzle + 2 parse-skipped (kein Round + Grundstellung-ohne-Marker)
        // + 1 Duplikat des ersten Puzzles → Imported=1, Skipped=1, Invalid=2.
        var pgn = @"
[Event ""Mix""]
[Round ""1""]
[FEN ""8/P7/8/8/8/8/8/k6K w - - 0 1""]

1. a8=Q+ *

[Event ""NoRound""]
[FEN ""8/8/8/8/8/8/PP6/K7 w - - 0 1""]

1. a4 *

[Event ""StartFenNoMarker""]
[Round ""2""]
[FEN ""rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1""]

1. e4 *

[Event ""DupOfFirst""]
[Round ""1""]
[FEN ""8/P7/8/8/8/8/8/k6K w - - 0 1""]

1. a8=Q+ *
";
        var item = await _service.ImportFileAsync("mix.pgn", pgn, default);
        Assert.Equal(1, item.Imported);
        Assert.Equal(1, item.Skipped);  // Duplikat innerhalb des Batches
        Assert.Equal(2, item.Invalid);  // NoRound + StartFenNoMarker
    }

    [Theory]
    [InlineData("Chapter 2: Back-Rank Mates", "Chapter 2")]
    [InlineData("Kapitel 3: Abzugsschach", "Kapitel 3")]
    [InlineData("  chapter 10:   Smothered Mate  ", "chapter 10")]
    [InlineData("Chapter 5", "Chapter 5")]                 // kein Spoiler-Doppelpunkt → unverändert
    [InlineData("Tactics: Pins", "Tactics: Pins")]         // kein Chapter/Kapitel-Präfix → unverändert
    [InlineData("Introduction", "Introduction")]
    [InlineData("", "")]
    public void StripChapterSpoiler_StripsOnlyChapterTitlePattern(string input, string expected)
    {
        Assert.Equal(expected, PgnImportService.StripChapterSpoiler(input));
    }

    [Fact]
    public void StripChapterSpoiler_Null_ReturnsNull()
    {
        Assert.Null(PgnImportService.StripChapterSpoiler(null));
    }

    [Fact]
    public async Task ImportFileAsync_PuzzleBook_StripsChapterSpoiler()
    {
        // [Black] wird zum Chapter; Default-Buchart ist Puzzle → Spoiler raus.
        var pgn = @"
[Event ""S""]
[Round ""1""]
[Black ""Chapter 2: Back-Rank Mates""]
[FEN ""8/P7/8/8/8/8/8/k6K w - - 0 1""]

1. a8=Q+ Kb2 *
";
        await _service.ImportFileAsync("puzzles.pgn", pgn, default);
        var bp = await _db.BookPuzzles.SingleAsync();
        Assert.Equal("Chapter 2", bp.Chapter);
    }

    [Fact]
    public async Task ImportFileAsync_StudyBook_KeepsChapterName()
    {
        // Buch vorab als Study anlegen → Kapitelname bleibt erhalten.
        _db.Books.Add(new Book { FileName = "study.pgn", DisplayName = "Study", Kind = BookKind.Study });
        await _db.SaveChangesAsync();

        var pgn = @"
[Event ""S""]
[Round ""1""]
[Black ""Chapter 2: Back-Rank Mates""]
[FEN ""8/P7/8/8/8/8/8/k6K w - - 0 1""]

1. a8=Q+ Kb2 *
";
        await _service.ImportFileAsync("study.pgn", pgn, default);
        var bp = await _db.BookPuzzles.SingleAsync();
        Assert.Equal("Chapter 2: Back-Rank Mates", bp.Chapter);
    }
}
