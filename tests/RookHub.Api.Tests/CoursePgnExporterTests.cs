using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class CoursePgnExporterTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    [Fact]
    public void ToPgn_Startpos_ConvertsUciToSanWithHeaders()
    {
        var p = new BookPuzzle
        {
            Fen = StartFen,
            Moves = "e2e4 e7e5 g1f3 b8c6",
            StartPly = -1,      // ab moves[0] loesen ⇒ kein [%tqu] im Export
            Round = "1.1",
            Title = "White",
            Chapter = "Black"
        };

        var pgn = CoursePgnExporter.ToPgn("My Book", new[] { p });

        Assert.Contains("[Event \"My Book\"]", pgn);
        Assert.Contains("[White \"White\"]", pgn);
        Assert.Contains($"[FEN \"{StartFen}\"]", pgn);
        Assert.Contains("1. e4 e5 2. Nf3 Nc6 *", pgn);
    }

    [Fact]
    public void ToPgn_BlackToMove_NumbersWithEllipsis()
    {
        var p = new BookPuzzle
        {
            // nach 1. e4 — Schwarz am Zug, Vollzug 1
            Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1",
            Moves = "e7e5"
        };

        var pgn = CoursePgnExporter.ToPgn("B", new[] { p });

        Assert.Contains("1... e5 *", pgn);
    }

    [Fact]
    public void ToPgn_InvalidMoves_AreSkippedNotCrash()
    {
        var p = new BookPuzzle { Fen = StartFen, Moves = "e2e4 z9z9" }; // 2. Zug illegal → Abbruch
        var pgn = CoursePgnExporter.ToPgn("X", new[] { p });
        Assert.Contains("1. e4 *", pgn); // erster Zug drin, dann sauber abgebrochen
    }

    [Fact]
    public void ToPgn_EmbedsMoveComments_AfterEachMoveAndIntro()
    {
        var p = new BookPuzzle
        {
            Fen = StartFen,
            Moves = "e2e4 e7e5 g1f3",
            StartPly = -1,      // ab moves[0] loesen ⇒ kein [%tqu] im Export
            // -1 = Einleitung, 0 = nach 1. e4, 2 = nach 2. Nf3
            MoveComments = "{\"-1\":\"Italienisch\",\"0\":\"Königsbauer\",\"2\":\"entwickelt\"}"
        };

        var pgn = CoursePgnExporter.ToPgn("Book", new[] { p });

        Assert.Contains("{Italienisch} 1. e4 {Königsbauer} e5 2. Nf3 {entwickelt} *", pgn);
    }

    [Fact]
    public void ToPgn_FallsBackToLineComment_WhenNoIntroPly()
    {
        var p = new BookPuzzle { Fen = StartFen, Moves = "e2e4", Comment = "Allgemeiner Hinweis" };
        var pgn = CoursePgnExporter.ToPgn("Book", new[] { p });
        Assert.Contains("{Allgemeiner Hinweis} 1. e4 *", pgn);
    }

    [Fact]
    public void ToPgn_ClosingBraceInComment_IsSanitized()
    {
        var p = new BookPuzzle { Fen = StartFen, Moves = "e2e4", MoveComments = "{\"0\":\"a } b\"}" };
        var pgn = CoursePgnExporter.ToPgn("Book", new[] { p });
        Assert.Contains("1. e4 {a ) b}", pgn); // '}' ersetzt, Kommentar bleibt valide
    }

    [Fact]
    public void ToPgn_KeepsChessableOid()
    {
        // Die oid ist die Verknüpfung zur Extension: sie muss die Umwandlung Kurs → Repertoire überleben.
        var p = new BookPuzzle { Fen = StartFen, Moves = "e2e4", ChessableOid = "73000253" };
        Assert.Contains("[ChessableOid \"73000253\"]", CoursePgnExporter.ToPgn("Book", new[] { p }));
    }

    [Fact]
    public void ToPgn_WritesTrainingMarker_SoStartPlySurvivesAReimport()
    {
        // StartPly 1 ⇒ zwei Halbzüge vorgespielt, gelöst ab moves[2] (Nf3). Der Marker gehört hinter
        // den letzten vorgespielten Zug; ein erneuter Import muss wieder StartPly 1 ergeben.
        var p = new BookPuzzle { Fen = StartFen, Moves = "e2e4 e7e5 g1f3 b8c6", Round = "1", StartPly = 1 };
        var pgn = CoursePgnExporter.ToPgn("Book", new[] { p });

        Assert.Contains("1. e4 e5 {[%tqu", pgn);
        var reimported = Assert.Single(PgnImportService.ParsePgn("b.pgn", pgn).Puzzles);
        Assert.Equal(1, reimported.StartPly);
        Assert.Equal("e2e4 e7e5 g1f3 b8c6", reimported.Moves);
    }

    [Fact]
    public void ToPgn_SolveFromFirstMove_WritesNoMarker()
    {
        var p = new BookPuzzle { Fen = StartFen, Moves = "e2e4 e7e5", StartPly = -1 };
        Assert.DoesNotContain("[%tqu", CoursePgnExporter.ToPgn("Book", new[] { p }));
    }
}
