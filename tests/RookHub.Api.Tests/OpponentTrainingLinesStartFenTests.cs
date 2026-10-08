using Chess;
using RookHub.Api.Services;
using RookHub.Api.Services.Prep;

namespace RookHub.Api.Tests;

/// <summary>
/// Linien mit eigener Startstellung (<c>[FEN]</c>, z. B. Chessable-Übungen aus Modellpartien) in den Trainingslinien
/// (Screenshot 2026-10-08: „Prep: Stoettner" bestand aus 50 Übungen, die der Gegner nie erreicht): ohne erreichte
/// Startstellung bleibt so eine Linie „nie erreicht" — auch mit Schätzung, der Explorer kennt zwar die Züge ab der FEN,
/// aber nicht, ob man dort je hinkommt. Hat er die Startstellung erreicht, zählt sie wie jede andere.
/// </summary>
public class OpponentTrainingLinesStartFenTests
{
    // nach 1.d4 d5 2.c4 e6 — Weiß am Zug
    private const string Fen = "rnbqkbnr/ppp2ppp/4p3/3p4/2PP4/8/PP2PPPP/RNBQKBNR w KQkq - 0 3";
    private const string Exercise = "3. Nc3 Nf6 4. Bg5 Be7";

    private static RepertoireReach.Graph Graph(params string[] sections) =>
        RepertoireReach.Build(sections.SelectMany(PgnMoveTree.ParseSections), 'w');

    private static string Section(string moves, string? fen = null) =>
        $"[Event \"x\"]\n[Black \"K\"]\n{(fen is null ? "" : $"[FEN \"{fen}\"]\n")}\n{moves} *\n";

    private static IEnumerable<OpponentTrainingLines.Game> Times(int n, string moves) =>
        Enumerable.Range(0, n).Select(_ => new OpponentTrainingLines.Game(moves.Split(' '), false, 2025));

    private static ExplorerPositionStats Stats(params (string San, long Games)[] moves) =>
        new(moves.Sum(m => m.Games), moves.Select(m => new ExplorerMoveStat("", m.San, m.Games, null, null)).ToList());

    /// <summary>Ein Explorer, der JEDE Stellung kennt — so verführerisch wie der echte bei Übungsstellungen.</summary>
    private static OpponentTrainingLines.Estimate KnowsEverything() =>
        new(_ => Stats(("Nf6", 60), ("Be7", 40), ("c5", 10), ("d6", 10)), 1, new HashSet<string>());

    [Fact]
    public void ExerciseWhoseStartHeNeverReached_StaysNeverReached_EvenWithTheEstimate()
    {
        var g = Graph(Section(Exercise, Fen), Section("1. e4 c5 2. Nf3 d6"));
        var a = OpponentTrainingLines.Analyze(g, Times(1, "e4 c5 Nf3 d6"));

        var r = OpponentTrainingLines.Rank(g, ["K", "K"], a, KnowsEverything());

        Assert.Equal("e4", r.Lines[0].Sans[0]);                    // seine Partie zuerst
        Assert.Equal("own", r.Lines[0].Source);
        var ex = r.Lines[1];
        Assert.Equal("Nc3", ex.Sans[0]);
        Assert.NotNull(ex.StartFen);
        Assert.True(ex.NeverReached);                              // der Explorer kennt die Züge ab der FEN — hilft nichts
        Assert.Equal("none", ex.Source);
        Assert.Equal(0, ex.Probability);
        Assert.Equal(0, ex.LichessMoves);
    }

    [Fact]
    public void ExerciseWhoseStartHeNeverReached_AsksTheExplorerNothing()
    {
        var g = Graph(Section(Exercise, Fen), Section("1. e4 c5 2. Nf3 d6"));
        var a = OpponentTrainingLines.Analyze(g, Times(1, "e4 c5 Nf3 d6"));

        var need = OpponentTrainingLines.NeedsExplorer(g, a, 1);

        Assert.Empty(need);                                        // die e4-Linie trifft er voll, die Übung zählt nicht
    }

    [Fact]
    public void ExerciseWhoseStartHeReached_CountsLikeAnyLine()
    {
        var g = Graph(Section(Exercise, Fen), Section("1. e4 c5 2. Nf3 d6"));
        var a = OpponentTrainingLines.Analyze(g, Times(1, "d4 d5 c4 e6 Nc3 Nf6"));

        var r = OpponentTrainingLines.Rank(g, ["K", "K"], a, KnowsEverything());

        var ex = r.Lines[0];
        Assert.Equal("Nc3", ex.Sans[0]);
        Assert.False(ex.NeverReached);
        Assert.Equal("mixed", ex.Source);                          // 3…Nf6 aus seiner Partie, 4…Be7 vom Explorer
        Assert.Equal(1, ex.OwnMoves);
        Assert.Equal(1, ex.LichessMoves);
        Assert.Equal(1.0 * (40.0 / 120), ex.Probability, 6);
        Assert.Equal(0, ex.Reached);                               // seine Partie endet nach 3…Nf6, die Endstellung erreicht sie nicht

        var board = new ChessBoard();
        foreach (var san in "d4 d5 c4 e6 Nc3 Nf6 Bg5".Split(' ')) Assert.True(board.Move(san), san);
        Assert.Contains(OpponentTrainingLines.NeedsExplorer(g, a, 1), n => n.Key == RepertoireReach.Key(board.ToFen()));
    }

    [Fact]
    public void WithoutEstimate_TheExerciseIsNeverReachedAsBefore()
    {
        var g = Graph(Section(Exercise, Fen), Section("1. e4 c5 2. Nf3 d6"));

        var r = OpponentTrainingLines.Rank(g, ["K", "K"], Times(1, "e4 c5 Nf3 d6"));

        Assert.True(r.Lines[1].NeverReached);
        Assert.NotNull(r.Lines[1].StartFen);
    }
}
