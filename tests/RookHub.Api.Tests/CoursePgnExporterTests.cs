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
}
