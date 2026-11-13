using RookHub.Api.Services.Games;
using RookHub.Api.Services.Puzzles;
using Xunit;

namespace RookHub.Api.Tests.Puzzles;

/// <summary>Mattbild der Schlussstellung (<see cref="MatePattern"/>, 0.960.0): Spiegelung, Farbtausch, strenge Signatur gegen Familie.</summary>
public class MatePatternTests
{
    private static MatePattern.Signature Sig(string fen) =>
        MatePattern.Of(TacticsBoard.TryParse(fen)!) ?? throw new Xunit.Sdk.XunitException($"kein Matt: {fen}");

    // Dame g7 matt, gedeckt vom Bauern f6 bzw. vom Springer h5 bzw. vom Läufer d4 (Wunsch des Users: „kann dann natürlich auch ein
    // Springer sein, der das deckt“).
    private const string QueenPawn = "6k1/5pQp/5P2/8/8/8/8/6K1 b - - 0 1";
    private const string QueenKnight = "6k1/5pQp/8/7N/8/8/8/6K1 b - - 0 1";
    private const string QueenBishop = "6k1/5pQp/8/8/3B4/8/8/6K1 b - - 0 1";

    [Fact]
    public void QueenDefendedByPawnOrKnight_SameFamily_DifferentStrict()
    {
        var pawn = Sig(QueenPawn); var knight = Sig(QueenKnight); var bishop = Sig(QueenBishop);
        Assert.Equal(pawn.Family, knight.Family);
        Assert.Equal(pawn.Family, bishop.Family);
        Assert.NotEqual(pawn.Strict, knight.Strict);
        Assert.NotEqual(pawn.Strict, bishop.Strict);
        Assert.Contains("/P", pawn.Strict);
        Assert.Contains("/N", knight.Strict);
        Assert.Equal(MatePattern.Hash(pawn.Family), knight.FamilyHash);
    }

    [Fact]
    public void Mirrored_SameStrict()
    {
        Assert.Equal(Sig(QueenPawn).Strict, Sig("1k6/pQp5/2P5/8/8/8/8/6K1 b - - 0 1").Strict);
    }

    [Fact]
    public void ColorsSwapped_SameStrict()
    {
        // Schwarz setzt Weiß matt: Dame g2, gedeckt vom Bauern f3.
        Assert.Equal(Sig(QueenPawn).Strict, Sig("6k1/8/8/8/8/5p2/5PqP/6K1 w - - 0 1").Strict);
        // … und gespiegelt dazu.
        Assert.Equal(Sig(QueenPawn).Strict, Sig("1k6/8/8/8/8/2p5/PqP5/1K6 w - - 0 1").Strict);
    }

    [Fact]
    public void BackRankRook_DifferentFamilyThanQueenContact()
    {
        var rook = Sig("3R2k1/5ppp/8/8/8/8/8/6K1 b - - 0 1");
        Assert.NotEqual(Sig(QueenPawn).Family, rook.Family);
        Assert.StartsWith("MF1|back|Rlo|", rook.Family);
    }

    [Fact]
    public void UsersExample_QueenF7KingF6_PawnF4OrKnight_SameFamily()
    {
        // Prod-Partie 221 (kahalm – farnattie), Zug 15: verpasstes Matt in 4 (Wunsch-Beispiel „Df7 Kf6 und Bauer auf f4“). Die
        // Engine-Linie endet mit Df7#: Dame gedeckt vom Bauern e6, der Bauer f4 nimmt e5 und g5.
        var final = MatePattern.Play("r2qkb1r/5ppp/p2pP3/1p1Q4/2nN1P2/4B3/PPP3PP/R3K2R w KQkq - 1 15",
            ["d5c6", "e8e7", "c6b7", "d8d7", "b7d7", "e7f6", "d7f7"])!;
        var pawn = MatePattern.Of(final)!;
        Assert.StartsWith("MF1|open|Qco+|", pawn.Family);
        // Dasselbe mit einem Springer f3 statt des Bauern f4 („kann dann natürlich auch ein Springer sein“).
        var fen = final.ToFen();
        Assert.Contains("/2nN1P2/4B3/", fen);
        var knight = Sig(fen.Replace("/2nN1P2/4B3/", "/2nN4/4BN2/"));
        Assert.Equal(pawn.Family, knight.Family);
        Assert.NotEqual(pawn.Strict, knight.Strict);
    }

    [Fact]
    public void NotMate_Null()
    {
        Assert.Null(MatePattern.Of(TacticsBoard.TryParse("6k1/5pQp/8/8/8/8/8/6K1 b - - 0 1")!));   // Dame ungedeckt: Kxg7
        Assert.Null(MatePattern.Of(TacticsBoard.TryParse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")!));
    }

    [Fact]
    public void OfPuzzle_PlaysAllMoves()
    {
        // Lichess-Form: FEN vor dem Zug des Gegners, dann Gegnerzug + Lösung.
        var sig = MatePattern.OfPuzzle("6k1/p4p1p/5P1Q/8/8/8/8/K7 b - - 0 1", "a7a6 h6g7");
        Assert.NotNull(sig);
        Assert.Equal(Sig(QueenPawn).Strict, sig!.Strict);
        Assert.Null(MatePattern.OfPuzzle("6k1/p4p1p/5P1Q/8/8/8/8/K7 b - - 0 1", "a7a6 h6g6"));
    }
}
