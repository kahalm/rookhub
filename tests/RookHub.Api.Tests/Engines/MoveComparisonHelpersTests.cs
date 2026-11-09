using Chess;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Tests.Engines;

/// <summary>Charakterisierung der reinen Helfer des Zugvergleichs (Brett, Bewertung lesen, Prompt, Antwort lesen) vor dem
/// Herausziehen aus <see cref="MoveComparisonService"/> (Zerlegung 2026-11-09).</summary>
public class MoveComparisonHelpersTests
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string Castle = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";
    private const string Mated = "7k/6Q1/6K1/8/8/8/8/8 b - - 0 1";
    private const string Stalemate = "7k/5Q2/6K1/8/8/8/8/8 b - - 0 1";

    private static string Pvs(params (int? cp, int? mate, string moves)[] pvs)
        => "{\"depth\":22,\"time\":1000,\"nodes\":5000000,\"pvs\":["
           + string.Join(',', pvs.Select(p => "{\"depth\":22," + (p.mate is { } m ? $"\"mate\":{m}" : $"\"cp\":{p.cp}")
               + $",\"moves\":[{string.Join(',', p.moves.Split(' ').Select(x => $"\"{x}\""))}]}}"))
           + "]}";

    [Fact]
    public void PieceAt_ReadsTheFen_NullForEmptyOrNonsense()
    {
        Assert.Equal('P', MoveComparisonBoard.PieceAt(Start, "e2"));
        Assert.Equal('k', MoveComparisonBoard.PieceAt(Start, "e8"));
        Assert.Null(MoveComparisonBoard.PieceAt(Start, "e4"));
        Assert.Null(MoveComparisonBoard.PieceAt(Start, "i1"));
        Assert.Null(MoveComparisonBoard.PieceAt(Start, "e"));
        Assert.Null(MoveComparisonBoard.PieceAt("8/8 w - - 0 1", "a1"));
        Assert.Null(MoveComparisonBoard.PieceAt("", "a1"));
    }

    [Fact]
    public void FindMove_UciAndKingTakesRookCastling_CaseAndBlanksIgnored()
    {
        var legal = ChessBoard.LoadFromFen(Castle).Moves();
        Assert.Equal("e1g1", RookHub.Api.Services.Games.GamePlies.ToUci(MoveComparisonBoard.FindMove(legal, "e1h1")!));
        Assert.Equal("e1c1", RookHub.Api.Services.Games.GamePlies.ToUci(MoveComparisonBoard.FindMove(legal, " E1A1 ")!));
        Assert.NotNull(MoveComparisonBoard.FindMove(legal, "a1a8"));
        Assert.Null(MoveComparisonBoard.FindMove(legal, "e1e3"));
        Assert.Null(MoveComparisonBoard.FindMove(legal, "e1"));
        Assert.Null(MoveComparisonBoard.FindMove(legal, null));
    }

    [Fact]
    public void PlayUci_AndSanOf_NullWhenNotPlayable()
    {
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", MoveComparisonBoard.PlayUci(Start, "e2e4"));
        Assert.Null(MoveComparisonBoard.PlayUci(Start, "e2e5"));
        Assert.Null(MoveComparisonBoard.PlayUci("kaputt", "e2e4"));
        Assert.Equal("Nf3", MoveComparisonBoard.SanOf(Start, "g1f3"));
        Assert.Equal("O-O", MoveComparisonBoard.SanOf(Castle, "e1h1"));
        Assert.Null(MoveComparisonBoard.SanOf(Start, "g1g3"));
        Assert.Null(MoveComparisonBoard.SanOf("kaputt", "g1f3"));
    }

    [Fact]
    public void Terminal_MateStalemateOrNothing()
    {
        Assert.Equal(new MoveComparisonEval.Ev(100_000, null, null, true), MoveComparisonEval.Terminal(Mated));
        Assert.Equal(new MoveComparisonEval.Ev(0, 0, null, false), MoveComparisonEval.Terminal(Stalemate));
        Assert.Null(MoveComparisonEval.Terminal(Start));
        Assert.Null(MoveComparisonEval.Terminal(""));
    }

    [Fact]
    public void EvalOf_And_Pvs_WhiteView_MateRanksByDistance()
    {
        var afterE4 = MoveComparisonBoard.PlayUci(Start, "e2e4")!;   // Schwarz am Zug — die Werte des Auftrags sind schon Weiß-Sicht
        var pvs = MoveComparisonEval.Pvs(afterE4, Pvs((-30, null, "e7e5 g1f3"), (-50, null, "c7c5")));
        Assert.Equal(2, pvs.Count);
        Assert.Equal(("e7e5", "e5", -30, (int?)null), (pvs[0].Uci, pvs[0].San, pvs[0].CpWhite, pvs[0].MateWhite));
        Assert.Equal(new[] { "e5", "Nf3" }, pvs[0].Line);
        Assert.Equal(new MoveComparisonEval.Ev(-30, -30, null, false), MoveComparisonEval.EvalOf(afterE4, Pvs((-30, null, "e7e5"))));
        Assert.Equal(new MoveComparisonEval.Ev(-100_000 + 2, null, -2, false), MoveComparisonEval.EvalOf(afterE4, Pvs((null, -2, "e7e5"))));
        Assert.Equal(new MoveComparisonEval.Ev(100_000 - 3, null, 3, false), MoveComparisonEval.EvalOf(Start, Pvs((null, 3, "e2e4"))));
        Assert.Null(MoveComparisonEval.EvalOf(Start, null));
        Assert.Empty(MoveComparisonEval.Pvs("", Pvs((10, null, "e2e4"))));
    }

    [Fact]
    public void SystemPrompt_NamesTheLanguage()
    {
        var p = MoveComparisonPrompts.SystemPrompt("de");
        Assert.StartsWith("You are a friendly, precise chess coach.", p);
        Assert.Contains("Write in German", p);
        Assert.EndsWith("Return the JSON object {\"explanation\": \"...\"}.", p);
    }

    [Fact]
    public void UserPrompt_ListsEvaluationsAnswersAndTests()
    {
        var fen = Start;
        var best = MoveComparisonBoard.PlayUci(fen, "e2e4")!;
        var weak = MoveComparisonBoard.PlayUci(fen, "g1f3")!;
        var bestReplies = MoveComparisonEval.Pvs(best, Pvs((-30, null, "e7e5 g1f3")));
        var weakReplies = MoveComparisonEval.Pvs(weak, Pvs((-20, null, "d7d5 d2d4")));
        var tests = new List<MoveComparisonPrompts.ReplyTest>
        {
            new("d5", MoveComparisonLineState.Done, "+0.40", new[] { "exd5" }),
            new("Nf6", MoveComparisonLineState.Illegal, null, Array.Empty<string>()),
            new("c5", MoveComparisonLineState.Failed, null, Array.Empty<string>()),
        };
        var f = new MoveComparisonPrompts.PairFacts(fen, true, 22, "e4", best, "+0.30", bestReplies, "Nf3", weak, "+0.20", weakReplies, tests);
        var text = MoveComparisonPrompts.UserPrompt(f);
        var lines = text.Split('\n');
        Assert.Equal($"Position (FEN): {fen}", lines[0]);
        Assert.Equal("The reader plays White and is to move. The engine compared the reader's candidate moves at depth 22.", lines[1]);
        Assert.StartsWith("Better move: 1.e4 — evaluation from the reader's view afterwards: +0.30", lines[2]);
        Assert.StartsWith("Weaker move: 1.Nf3 — evaluation from the reader's view afterwards: +0.20", lines[3]);
        Assert.Contains("The two moves are almost equal (difference 0.1 pawns).", lines);
        Assert.Contains("After 1.Nf3, the opponent's best answers (evaluation from the reader's view, engine line):", lines);
        Assert.Contains("The same answers against 1.e4:", lines);
        Assert.Contains(lines, l => l.StartsWith("- 1...d5: evaluation from the reader's view +0.40") && l.Contains("The reader's best reply, with the engine line: 2.exd5."));
        Assert.Contains(lines, l => l.EndsWith(": not possible after 1.e4."));
        Assert.Contains(lines, l => l.EndsWith(": could not be calculated."));
        Assert.Contains(lines, l => l.StartsWith("The opponent's best answer to 1.e4 itself: 1...e5"));
        Assert.Equal("Reader: plays White — \"you\" is the reader, Black is \"your opponent\".", lines[^1]);
        Assert.Contains("e4", f.AllowedMoves());
        Assert.Contains("exd5", f.AllowedMoves());
    }
}
