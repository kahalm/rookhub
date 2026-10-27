using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>Spiegel von <c>move-tactics.util.spec.ts</c> — dieselben Stellungen, dieselben Ergebnisse (0.854.0).</summary>
public class MoveTacticsTests
{
    [Fact]
    public void Game228_22Nxe6_IsASacrifice_KingRecapturesLast()
    {
        Assert.Equal(new MoveTactics.Sacrifice("e6", 'n'), MoveTactics.SacrificedPiece(
            "r1b4r/3kb1pp/p2qp3/3p2NB/8/2P5/P3Q1PP/R3K2R w KQ - 7 22",
            "r1b4r/3kb1pp/p2qN3/3p3B/8/2P5/P3Q1PP/R3K2R b KQ - 0 22", "g5e6"));
    }

    [Fact]
    public void KingCountsAsLastCapturer_QxF7_Hangs()
    {
        Assert.True(MoveTactics.IsPieceHanging(
            "r1b1kbnr/ppppqppp/2n5/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4",
            "r1b1kbnr/ppppqQpp/2n5/4p3/2B1P3/8/PPPP1PPP/RNB1K1NR b KQkq - 0 4", "f7"));
    }

    [Fact]
    public void DiscoveredCheck_PawnMayNotCapture_NotHanging()
    {
        const string before = "3k4/8/4p3/8/3N4/8/8/3R2K1 w - - 0 1";
        const string after = "3k4/8/4p3/5N2/8/8/8/3R2K1 b - - 1 1";
        Assert.False(MoveTactics.IsPieceHanging(before, after, "f5"));
        Assert.Null(MoveTactics.SacrificedPiece(before, after, "d4f5"));
    }

    [Fact]
    public void Game228_25Qf3_TheKnightE6StillHangs()
    {
        Assert.Equal(new MoveTactics.Sacrifice("e6", 'n'), MoveTactics.SacrificedPiece(
            "r1b4r/3k2pp/p3Nb2/3pq2B/8/2P3P1/P3Q2P/R4RK1 w - - 3 25",
            "r1b4r/3k2pp/p3Nb2/3pq2B/8/2P2QP1/P6P/R4RK1 b - - 4 25", "e2f3"));
    }

    [Fact]
    public void Promotion_NewQueenHangs_IsStillFoundAsSacrifice()
    {
        Assert.Equal(new MoveTactics.Sacrifice("e8", 'q'),
            MoveTactics.SacrificedPiece("r7/4P2k/8/8/8/8/8/6K1 w - - 0 1", "r3Q3/7k/8/8/8/8/8/6K1 b - - 0 1", "e7e8q"));
        Assert.True(MoveTactics.IsPromotion("e7e8q"));
        Assert.False(MoveTactics.IsPromotion("e7e8"));
    }

    [Fact]
    public void CapturesPiece_PieceYes_PawnNo_OwnRookNo()
    {
        Assert.True(MoveTactics.CapturesPiece("4k3/8/8/3n4/8/2N5/8/4K3 w - - 0 1", "c3d5"));
        Assert.False(MoveTactics.CapturesPiece("4k3/8/8/3p4/8/2N5/8/4K3 w - - 0 1", "c3d5"));
        Assert.False(MoveTactics.CapturesPiece("4k3/8/8/8/8/8/8/4K2R w K - 0 1", "e1h1"));
        Assert.False(MoveTactics.CapturesPiece("4k2r/8/8/8/8/8/8/4K2R b k - 0 1", "e8h8"));
    }

    [Fact]
    public void ExchangeGain_LikeTheClient()
    {
        Assert.Equal(2, MoveTactics.ExchangeGain(5, [3], [9]));     // gedeckter Turm gegen Läufer: Qualität
        Assert.Equal(0, MoveTactics.ExchangeGain(3, [3], [3]));     // Läufer gegen Läufer
        Assert.Equal(3, MoveTactics.ExchangeGain(3, [9, double.PositiveInfinity], [9]));   // …Dxe6 Dxe6+ Kxe6
    }

    [Fact]
    public void InCheck_UnreadableIsFalse()
    {
        Assert.True(MoveTactics.InCheck("4k3/8/8/8/1b6/8/8/4K3 w - - 0 1"));
        Assert.False(MoveTactics.InCheck("kaputt"));
    }
}
