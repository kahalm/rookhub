using RookHub.Api.Services.Puzzles;
using Xunit;

namespace RookHub.Api.Tests.Puzzles;

/// <summary>Motiv-Erkenner (<see cref="TacticMotifs"/>, 0.961.0): je Motiv ein klares Beispiel und ein Gegenbeispiel.</summary>
public class TacticMotifsTests
{
    private static List<TacticMotifs.Motif> D(string fen, params string[] line) => TacticMotifs.Detect(fen, line);
    private static TacticMotifs.Motif? Of(string theme, string fen, params string[] line) => D(fen, line).FirstOrDefault(m => m.Theme == theme);

    [Fact]
    public void Fork_KnightKingRook()
    {
        var m = Of("fork", "r3k3/8/8/3N4/8/8/8/4K3 w - - 0 1", "d5c7", "e8d7", "c7a8");
        Assert.Equal("fork|N|KR", m!.Strict);
        Assert.Equal("T1|fork", m.Family);
        // Schwarz genauso (Farbe egal): Sc2+ gabelt Ke1 und Ta1.
        Assert.Equal("fork|N|KR", Of("fork", "4k3/8/8/8/3n4/8/8/R3K3 b - - 0 1", "d4c2", "e1d2", "c2a1")!.Strict);
    }

    [Fact]
    public void Fork_TwoDefendedPawns_NoFork() => Assert.Null(Of("fork", "4k3/3p4/2p1p3/8/8/1N6/8/4K3 w - - 0 1", "b3d4", "e8f7", "d4c6"));

    [Fact]
    public void Pin_BishopPinsKnightToKing()
    {
        Assert.Equal("pin|B|N>K", Of("pin", "4k3/8/2n5/8/8/8/8/4KB2 w - - 0 1", "f1b5", "e8e7", "b5c6")!.Strict);
        Assert.Null(Of("pin", "4k3/3p4/2n5/8/8/8/8/4KB2 w - - 0 1", "f1b5", "e8e7", "b5c6"));   // dahinter nur ein Bauer
    }

    [Fact]
    public void Skewer_RookKingThenQueen_OnlyWhenLineTakesIt()
    {
        Assert.Equal("skewer|R|K>Q", Of("skewer", "q3k3/8/8/8/8/8/8/4K2R w - - 0 1", "h1h8", "e8d7", "h8a8")!.Strict);
        Assert.Null(Of("skewer", "q3k3/8/8/8/8/8/8/4K2R w - - 0 1", "h1h8"));
    }

    [Fact]
    public void DiscoveredAttack_BishopUnmasksRookOnQueen()
    {
        Assert.Equal("discoveredAttack|R|Q|B", Of("discoveredAttack", "4q1k1/8/8/8/4B3/8/8/4R1K1 w - - 0 1", "e4b7", "g8h8", "e1e8")!.Strict);
        Assert.Null(Of("discoveredAttack", "4q1k1/8/8/8/4B3/8/4P3/4R1K1 w - - 0 1", "e4b7", "g8h8", "e1e8"));   // Bauer e2 verstellt weiter
        Assert.Null(Of("discoveredAttack", "4q1k1/8/8/8/4B3/8/8/4R1K1 w - - 0 1", "e4b7", "g8h8", "b7a8"));    // Dame nicht geholt
    }

    [Fact]
    public void DoubleCheck_BishopAndRook()
    {
        Assert.Equal("doubleCheck|BR", Of("doubleCheck", "4k3/8/8/8/4B3/8/8/4R1K1 w - - 0 1", "e4g6")!.Strict);
        Assert.Null(Of("doubleCheck", "4q1k1/8/8/8/4B3/8/8/4R1K1 w - - 0 1", "e4b7"));
    }

    [Fact]
    public void HangingPiece_UndefendedKnight()
    {
        Assert.Equal("hangingPiece|Q|N", Of("hangingPiece", "4k3/8/8/3n4/8/8/8/3QK3 w - - 0 1", "d1d5")!.Strict);
        Assert.Null(Of("hangingPiece", "4k3/8/4p3/3n4/8/8/8/3QK3 w - - 0 1", "d1d5"));   // e6 deckt
    }

    [Fact]
    public void Promotion_NotPlainPawnMove()
    {
        Assert.Equal("promotion|Q", Of("promotion", "8/4P3/8/8/8/k7/8/K7 w - - 0 1", "e7e8q")!.Strict);
        Assert.Empty(D("8/8/4P3/8/8/k7/8/K7 w - - 0 1", "e6e7"));   // vorgerückter Bauer allein ist kein Motiv (unscharf)
        Assert.Empty(D("8/8/8/8/8/k7/4P3/K7 w - - 0 1", "e2e4"));
    }

    [Fact]
    public void Fork_WithoutFollowUpCapture_NoFork() => Assert.Null(Of("fork", "r3k3/8/8/3N4/8/8/8/4K3 w - - 0 1", "d5c7", "e8d7", "c7e6"));

    [Fact]
    public void Pin_WithoutCapturingThePinnedPiece_NoPin() => Assert.Null(Of("pin", "4k3/8/2n5/8/8/8/8/4KB2 w - - 0 1", "f1b5", "e8e7", "e1e2"));

    [Fact]
    public void QuietPositionalMove_NoMotif() =>
        Assert.Empty(D("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "g1f3"));

    [Fact]
    public void OfPuzzle_StartsAfterOpponentMove()
    {
        // Lichess-Form: Schwarz zieht a7-a6, dann gabelt Weiß mit Sc7+.
        var m = TacticMotifs.OfPuzzle("r3k3/p7/8/3N4/8/8/8/4K3 b - - 0 1", "a7a6 d5c7 e8d7 c7a8");
        Assert.Contains(m, x => x.Strict == "fork|N|KR");
        Assert.Equal(Models.PuzzleSignatureKind.Fork, TacticMotifs.KindOf("fork"));
    }

    [Fact]
    public void Broken_Input_Empty()
    {
        Assert.Empty(D("kaputt", "e2e4"));
        Assert.Empty(D("4k3/8/8/8/8/8/8/4K3 w - - 0 1", "e2e4"));   // leeres Feld
    }
}
