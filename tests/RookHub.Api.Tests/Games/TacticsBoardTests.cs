using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>Das kleine Taktik-Brett (0.854.0): Angreifer, legale Züge, en passant, Rochade aus der Partie, Matt in einem.</summary>
public class TacticsBoardTests
{
    [Fact]
    public void StartPosition_Has20LegalMoves_AndRoundTripsTheBoard()
    {
        var b = TacticsBoard.TryParse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")!;
        Assert.Equal(20, b.LegalMoves().Count);
        Assert.Equal("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w - - 0 1", b.ToFen());
    }

    [Fact]
    public void PinnedPiece_HasNoLegalMoves_ButStillAttacksPseudo()
    {
        // Lb5 fesselt den Sc6 an den Ke8.
        var b = TacticsBoard.TryParse("4k3/8/2n5/1B6/8/8/8/4K3 b - - 0 1")!;
        Assert.DoesNotContain(b.LegalMoves(), m => m.From == TacticsBoard.SquareIndex("c6"));
        Assert.Contains(TacticsBoard.SquareIndex("c6")!.Value, b.Attackers(TacticsBoard.SquareIndex("e5")!.Value, white: false));
    }

    [Fact]
    public void EnPassant_IsGenerated_AndRemovesThePawn()
    {
        var b = TacticsBoard.TryParse("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1")!;
        var ep = Assert.Single(b.LegalMoves(), m => m.To == TacticsBoard.SquareIndex("d6"));
        Assert.Equal('p', ep.Captured);
        Assert.Equal("4k3/8/3P4/8/8/8/8/4K3 b - - 0 1", b.Apply(ep).ToFen());
    }

    [Fact]
    public void Castling_FromTheGame_BothNotations()
    {
        var b = TacticsBoard.TryParse("4k3/8/8/8/8/8/8/4K2R w K - 0 1")!;
        Assert.Equal("4k3/8/8/8/8/8/8/5RK1 b - - 0 1", b.Apply("e1g1")!.ToFen());
        Assert.Equal("4k3/8/8/8/8/8/8/5RK1 b - - 0 1", b.Apply("e1h1")!.ToFen());
    }

    [Fact]
    public void MateInOne_BackRank()
    {
        Assert.True(TacticsBoard.TryParse("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1")!.HasMateInOne());
        Assert.False(TacticsBoard.TryParse("6k1/5pp1/7p/8/8/8/8/R5K1 w - - 0 1")!.HasMateInOne());
    }

    [Fact]
    public void Unreadable_IsNull()
    {
        Assert.Null(TacticsBoard.TryParse("kaputt"));
        Assert.Null(TacticsBoard.TryParse("8/8/8/8/8/8/8/8 w - - 0 1"));   // ohne Könige
    }
}
