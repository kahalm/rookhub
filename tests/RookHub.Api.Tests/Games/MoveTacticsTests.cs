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

    /// <summary>Bis Regelstand 1 hieß das hier „Opfer Se6" (der Springer hängt weiter). Seit Regelstand 2 (User-Entscheidung
    /// 2026-11-09, Partie 241): der Springer hing auf e6 schon VOR 25.Df3 — dieser Zug opfert ihn nicht.</summary>
    [Fact]
    public void Game228_25Qf3_TheKnightE6HungBefore_NoSacrifice()
    {
        Assert.Null(MoveTactics.SacrificedPiece(
            "r1b4r/3k2pp/p3Nb2/3pq2B/8/2P3P1/P3Q2P/R4RK1 w - - 3 25",
            "r1b4r/3k2pp/p3Nb2/3pq2B/8/2P2QP1/P6P/R4RK1 b - - 4 25", "e2f3"));
    }

    /// <summary>Vereinspartie 241 (Analyse 24754), 7…Sxe4: der Springer schlägt e4 und hängt dort — die gezogene Figur
    /// (Zielfeld) stand vorher woanders, also Opfer.</summary>
    [Fact]
    public void Game241_7NxE4_IsASacrifice()
    {
        Assert.Equal(new MoveTactics.Sacrifice("e4", 'n'), MoveTactics.SacrificedPiece(
            "rnbq1rk1/ppp1bppp/3p1n2/8/2BNP3/2N5/PPP2PPP/R1BQ1RK1 b - - 6 7",
            "rnbq1rk1/ppp1bppp/3p4/8/2BNn3/2N5/PPP2PPP/R1BQ1RK1 w - - 0 8", "f6e4"));
    }

    /// <summary>Vereinspartie 241, 9.Ld3: der Springer e4 hing schon vor dem Zug (Bauer d5) — der Läuferzug gibt nur das
    /// vorher geopferte Material zurück, kein Opfer (User-Entscheidung 2026-11-09, Regelstand 2).</summary>
    [Fact]
    public void Game241_9Bd3_KnightE4HungBefore_NoSacrifice()
    {
        const string before = "rnbq1rk1/ppp1bppp/8/3p4/2BNN3/8/PPP2PPP/R1BQ1RK1 w - - 0 9";
        const string after = "rnbq1rk1/ppp1bppp/8/3p4/3NN3/3B4/PPP2PPP/R1BQ1RK1 b - - 1 9";
        Assert.True(MoveTactics.IsPieceHanging(before, before, "e4"));
        Assert.Null(MoveTactics.SacrificedPiece(before, after, "c4d3"));
    }

    /// <summary>Verteidiger weggezogen: der Sd4 ist vom Td8 angegriffen und vom Le3 gedeckt (hängt nicht); nach Lc1 hängt er
    /// erst — er hing vorher nicht, bleibt also ein Opfer.</summary>
    [Fact]
    public void DefenderMovedAway_PieceHangsOnlyAfter_IsStillASacrifice()
    {
        const string before = "3r2k1/8/8/8/3N4/4B3/8/6K1 w - - 0 1";
        const string after = "3r2k1/8/8/8/3N4/8/8/2B3K1 b - - 1 1";
        Assert.False(MoveTactics.IsPieceHanging(before, before, "d4"));
        Assert.Equal(new MoveTactics.Sacrifice("d4", 'n'), MoveTactics.SacrificedPiece(before, after, "e3c1"));
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
