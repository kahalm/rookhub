using RookHub.Api.Services.Puzzles;
using Xunit;

namespace RookHub.Api.Tests.Puzzles;

/// <summary>Matt als Drohung hinter einem Zug (<see cref="MateThreats"/>, 0.976.0) — an Stellungen echter Vereinspartien.</summary>
public class MateThreatsTests
{
    [Fact]
    public void QuietMove_ThreatensMateInOne()
    {
        // Vereinspartie 250, 8...Dh4 droht Dxf2#.
        var t = MateThreats.After("r1bqk2r/ppp2ppp/2nb4/8/2BNn3/2P5/PP3PPP/RNBQK2R b KQkq - 0 8", "d8h4", "c1e3");
        Assert.NotNull(t);
        Assert.Null(t!.ReplyUci);
        Assert.Equal("h4f2", t.MateUci);
        Assert.True(MatePattern.IsMate(t.End));
    }

    [Fact]
    public void Capture_WithMateBehindIt_IsATrade_NotAThreat()
    {
        // Vereinspartie 225, 23...Txd3: schlägt Weiß nicht zurück, folgt Txd1# — das ist ein Abtausch, keine Mattdrohung.
        Assert.Null(MateThreats.After("3r3r/5kpp/p3bp2/1p2pN2/8/P1PR4/1P3PPP/3R2K1 b - - 5 23", "d8d3", "f5h6"));
    }

    [Fact]
    public void NaturalDefence_RunsIntoMate()
    {
        // Vereinspartie 251, 37.Sd6: der angegriffene Turm e8 zieht nach g8 und mauert den König ein — 38.Sf7#.
        var t = MateThreats.After("4r2k/6bp/p7/Bp2p1p1/2r1N1Pn/2P3K1/P3RP2/3R4 w - - 0 37", "e4d6", "c4g4");
        Assert.Equal(("e8g8", "d6f7"), (t!.ReplyUci, t.MateUci));
        Assert.Equal(MatePattern.SmotheredMate, MatePattern.ThemeOf(t.End));
    }

    [Fact]
    public void CheckingMove_AnyOtherLegalReply_Counts_ButNotTheLineReply()
    {
        // Vereinspartie 231, 19.Sg5+: die Linie spielt Kh8; wer den Springer nimmt (hxg5), wird mit Dh3# matt.
        const string fen = "r4r2/ppqn2pk/2pb2pp/8/2BPN1Q1/P5P1/1P3P1P/R4RK1 w - - 5 19";
        var t = MateThreats.After(fen, "e4g5", "h7h8");
        Assert.Equal(("h6g5", "g4h3"), (t!.ReplyUci, t.MateUci));
        // Wäre hxg5 die Antwort der Linie, stünde die Linie selbst auf Matt — dann zählt sie hier nicht.
        Assert.Null(MateThreats.After(fen, "e4g5", "h6g5"));
    }

    [Fact]
    public void NoThreat_OrBrokenInput_Null()
    {
        Assert.Null(MateThreats.After("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "e2e4", "e7e5"));
        Assert.Null(MateThreats.After("kaputt", "e2e4"));
        Assert.Null(MateThreats.After("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "e7e5"));   // fremder Stein
        // Der Zug setzt selbst matt → keine „Drohung“.
        Assert.Null(MateThreats.After("6k1/5ppp/8/8/8/8/8/3R2K1 w - - 0 1", "d1d8"));
    }

    [Fact]
    public void IsNaturalReply_OnlyDefencesAgainstTheMove()
    {
        // 251: nach 37.Sd6 ist 37...Tg8 (der angegriffene Turm zieht) naheliegend, ein Bauernzug am Rand nicht.
        const string fen = "4r2k/6bp/p7/Bp2p1p1/2r1N1Pn/2P3K1/P3RP2/3R4 w - - 0 37";
        Assert.True(MateThreats.IsNaturalReply(fen, "e4d6", "e8g8"));
        Assert.False(MateThreats.IsNaturalReply(fen, "e4d6", "h7h6"));
        Assert.False(MateThreats.IsNaturalReply(fen, "e4d6", null));
        // Vereinspartie 223: nach 51.d5 (greift die Dame an) ist ein Königszug, der die Dame stehen lässt, nicht naheliegend.
        Assert.False(MateThreats.IsNaturalReply("8/3k3p/2q3pP/4Pp2/1p1P1P2/2pQ2P1/5K2/8 w - - 0 51", "d4d5", "d7d8"));
        Assert.True(MateThreats.IsNaturalReply("8/3k3p/2q3pP/4Pp2/1p1P1P2/2pQ2P1/5K2/8 w - - 0 51", "d4d5", "c6d5"));
    }
}
