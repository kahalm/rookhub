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

    // ---- Partie-Erkennung (0.976.0): Engine-Linien echter Vereinspartien ----

    private static List<TacticMotifs.Motif> G(string fen, params string[] line) => TacticMotifs.DetectInGame(fen, line);

    [Fact]
    public void Game_ForkCashedByDesperado_OnlyInGameMode()
    {
        // Vereinspartie 251, 37.Sd6: gabelt Tc4 und Te8; der eine Turm verkauft sich (37...Txg4+ 38.Kxg4), der andere zieht weg.
        const string fen = "4r2k/6bp/p7/Bp2p1p1/2r1N1Pn/2P3K1/P3RP2/3R4 w - - 0 37";
        string[] line = ["e4d6", "c4g4", "g3g4", "e8f8", "d6e4", "f8f4", "g4h3", "h7h6"];
        Assert.Equal("fork|N|RR", Assert.Single(G(fen, line)).Strict);
        Assert.Null(Of("fork", fen, line));                                   // Aufgaben-Regel unverändert: dort muss ein ZIEL geschlagen werden
        Assert.Empty(G(fen, "e4d6", "c4a4", "g3h4"));                         // der Turm zieht nur weg, geschlagen wird etwas anderes
    }

    [Fact]
    public void Game_ForkAfterForcingIntroduction()
    {
        // Vereinspartie 245: 26.Txc8+ Kxc8 27.Tc1+ Kd8 28.Sf7+ (Gabel König + Turm) Kd7 29.Sxh6.
        const string fen = "1kb3r1/pp2n3/1q2pb1r/1P1pN1p1/3P2P1/P1RBQ3/4NPK1/R7 w - - 3 26";
        Assert.Equal("fork|N|KR", Assert.Single(G(fen, "c3c8", "b8c8", "a1c1", "c8d8", "e5f7", "d8d7", "f7h6", "g8f8")).Strict);
        Assert.Empty(D(fen, "c3c8", "b8c8", "a1c1", "c8d8", "e5f7", "d8d7", "f7h6", "g8f8"));   // Aufgaben: nur der erste Zug
        // Ohne forcierende Einleitung (ruhiger erster Zug) wird nicht weitergesucht.
        Assert.Empty(G("r3k3/8/8/3N4/8/8/8/4K3 w - - 0 1", "e1e2", "a8a7", "d5c7", "e8d7", "c7a8"));
    }

    [Fact]
    public void Game_BackRank_CheckOnHomeRank_KingWithoutMove_WinsMaterial()
    {
        // Vereinspartie 251, 45.Ta8+ Lf8 46.Lxc3+ Kg8 47.Txa4 bxa4: der Läufer muss dazwischen, der Springer c3 fällt mit Schach.
        const string fen = "7k/R5bp/8/Bp4p1/r3p1P1/2n5/P4PK1/8 w - - 0 45";
        string[] line = ["a7a8", "g7f8", "a5c3", "h8g8", "a8a4", "b5a4", "c3d2", "h7h6"];
        var m = Assert.Single(G(fen, line));
        Assert.Equal((TacticMotifs.BackRank, "backRank|R"), (m.Theme, m.Strict));
        Assert.Equal("backRankMate", TacticMotifs.BackRank);                  // Name des Lichess-Themas (Tag der Aufgaben)
        Assert.False(TacticMotifs.HasSignatures(m.Theme));
        Assert.Empty(D(fen, line));                                           // keine Aufgaben-Signatur
        // Mit Luftloch (Bauer h6 statt h7) weicht der König aus → keine Grundreihe.
        Assert.DoesNotContain(G("7k/R5b1/7p/Bp4p1/r3p1P1/2n5/P4PK1/8 w - - 0 45", line), x => x.Theme == TacticMotifs.BackRank);
        // Schach auf der Grundreihe ohne Materialgewinn → nichts.
        Assert.Empty(G(fen, "a7a8", "g7f8", "a8a7", "f8g7"));
    }

    [Fact]
    public void Game_HangingPiece_ByMaterialBalance_NotByLineLength()
    {
        // Vereinspartie 250, 8...Lf5?? 9.Sxf5: der Läufer steht ungedeckt ein, die Engine-Linie ist acht Halbzüge lang.
        const string fen = "r2qk2r/ppp2ppp/2nb4/5b2/2BNn3/2P5/PP3PPP/RNBQK2R w KQkq - 1 9";
        string[] line = ["d4f5", "d8f6", "d1f3", "f6e5", "c1e3", "e8a8", "f5d6", "e4d6"];
        Assert.Equal("hangingPiece|N|B", Assert.Single(G(fen, line)).Strict);
        Assert.Empty(D(fen, line));                                           // Aufgaben-Regel: nur kurze Lösungen
        var f5 = RookHub.Api.Services.Games.TacticsBoard.SquareIndex("f5")!.Value;
        // Zurückschlagen nach einem gleichwertigen Tausch auf f5 ist keine hängende Figur — nach „Läufer schlägt Bauer“ schon.
        Assert.Empty(TacticMotifs.DetectInGame(fen, line, (f5, 3)));
        Assert.Single(TacticMotifs.DetectInGame(fen, line, (f5, 1)));
    }

    [Fact]
    public void Game_RecaptureInsideTheLine_IsNoHangingPiece()
    {
        // Vereinspartie 251, 44...Lxc3 45.Lxc3+ Sxc3: der Springer schlägt nur zurück (Bilanz +1), keine hängende Figur.
        Assert.Empty(G("7k/R5bp/8/Bp4p1/r3p1P1/2P5/P3nPK1/8 b - - 3 44", "g7c3", "a5c3", "e2c3", "a7c7", "a4a3", "c7c8", "h8g7", "c8c5"));
    }

    [Fact]
    public void NetGain_CountsCapturesPromotionAndEnPassant()
    {
        var b = RookHub.Api.Services.Games.TacticsBoard.TryParse("7k/R5bp/8/Bp4p1/r3p1P1/2n5/P4PK1/8 w - - 0 45")!;
        string[] line = ["a7a8", "g7f8", "a5c3", "h8g8", "a8a4", "b5a4"];
        Assert.Equal(0, TacticMotifs.NetGain(b, line, 2));
        Assert.Equal(3, TacticMotifs.NetGain(b, line, 3));    // Lxc3
        Assert.Equal(8, TacticMotifs.NetGain(b, line, 5));    // + Txa4
        Assert.Equal(3, TacticMotifs.NetGain(b, line, 6));    // − bxa4
        Assert.Equal(8, TacticMotifs.NetGain(RookHub.Api.Services.Games.TacticsBoard.TryParse("8/4P3/8/8/8/k7/8/K7 w - - 0 1")!, ["e7e8q"], 8));
        Assert.Equal(1, TacticMotifs.NetGain(RookHub.Api.Services.Games.TacticsBoard.TryParse("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1")!, ["e5d6"], 8));
    }

    [Fact]
    public void Broken_Input_Empty()
    {
        Assert.Empty(D("kaputt", "e2e4"));
        Assert.Empty(D("4k3/8/8/8/8/8/8/4K3 w - - 0 1", "e2e4"));   // leeres Feld
        Assert.Empty(G("kaputt", "e2e4"));
        Assert.Empty(G("4k3/8/8/8/8/8/8/4K3 w - - 0 1", "e2", "x"));
    }
}
