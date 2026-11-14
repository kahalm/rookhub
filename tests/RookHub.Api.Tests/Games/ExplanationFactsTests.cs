using RookHub.Api.Services;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>Die geprüften Fakten für „Warum war das ein Fehler?" (0.572.0): Lage in Worten, Ergebnis der Linien,
/// Angriffe des ersten Zugs. Grenzen LITERAL — der Nutzer nannte sie (2026-09-28).</summary>
public class ExplanationFactsTests
{
    private static GameMistakes.Flaw Flaw(bool white, string before, string after) => new(
        Ply: 10, White: white, Class: "mistake", WinBefore: 60, WinAfter: 40,
        FenBefore: "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", PlayedSan: "e4", PlayedUci: "e2e4",
        BestSan: null, BestLine: [], Refutation: [], EvalBefore: before, EvalAfter: after);

    [Theory]
    [InlineData("+1.35", 1.35, null)]
    [InlineData("-0.40", -0.40, null)]
    [InlineData("0.00", 0.0, null)]
    [InlineData("mate in 3", 0.0, 3)]
    [InlineData("gets mated in 2", 0.0, -2)]
    [InlineData("mate", 0.0, 0)]
    public void Parse_ReadsEvalText(string text, double pawns, int? mate)
    {
        var e = ExplanationFacts.Parse(text)!.Value;
        Assert.Equal(pawns, e.Pawns, 3);
        Assert.Equal(mate, e.Mate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gut")]
    [InlineData("mate in x")]
    public void Parse_Unreadable_IsNull(string? text) => Assert.Null(ExplanationFacts.Parse(text));

    [Theory]
    [InlineData("+2.50", 3)]      // „auf Gewinn, alles ab 2.5"
    [InlineData("+2.49", 2)]
    [InlineData("+1.00", 2)]
    [InlineData("+0.99", 1)]
    [InlineData("+0.40", 1)]
    [InlineData("+0.39", 0)]
    [InlineData("-0.39", 0)]
    [InlineData("-0.40", -1)]
    [InlineData("-0.99", -1)]
    [InlineData("-1.00", -2)]
    [InlineData("-2.49", -2)]
    [InlineData("-2.50", -3)]
    [InlineData("mate in 4", 3)]
    [InlineData("mate", 3)]
    [InlineData("gets mated in 1", -3)]
    public void Level_Thresholds(string text, int level) => Assert.Equal(level, ExplanationFacts.Parse(text)!.Value.Level);

    [Theory]
    [InlineData("-4.00", "-6.00", true)]              // „von −4 auf −6 muss das nicht kommentiert werden"
    [InlineData("gets mated in 5", "gets mated in 1", true)]
    [InlineData("-2.60", "gets mated in 3", true)]
    [InlineData("-1.00", "-3.00", false)]             // von schlechter zu verloren: sehr wohl ein Thema
    [InlineData("+5.80", "+4.40", false)]
    [InlineData("-2.49", "-4.00", false)]
    [InlineData("", "-4.00", false)]
    public void AlreadyLost_BeforeAndAfter(string before, string after, bool expected)
        => Assert.Equal(expected, ExplanationFacts.AlreadyLost(Flaw(white: true, before, after)));

    [Fact]
    public void Situation_OwnMove_StillWinning_ButGaveAwayPart()
        => Assert.Equal("the reader is still winning, but gave away part of the advantage "
                        + "(evaluation from the reader's view: +5.8 before, +4.4 after).",
            ExplanationFacts.Situation(Flaw(white: false, "+5.80", "+4.40"), "black"));

    [Fact]
    public void Situation_OwnMove_FromWorseToLost_AndFromBetterToWorse()
    {
        Assert.Equal("for the reader the position went from clearly worse to lost (evaluation from the reader's view: -1.0 before, -3.0 after).",
            ExplanationFacts.Situation(Flaw(white: true, "-1.00", "-3.00"), "white"));
        Assert.Equal("for the reader the position went from slightly better to slightly worse (evaluation from the reader's view: +0.5 before, -0.5 after).",
            ExplanationFacts.Situation(Flaw(white: true, "+0.50", "-0.50"), "white"));
    }

    /// <summary>Der Fehler des GEGNERS wird aus Sicht des Lesers beschrieben — die Zahlen drehen sich mit.</summary>
    [Fact]
    public void Situation_OpponentsMove_FromTheReadersView()
    {
        // Schwarz (Gegner) von −3 auf −4: der Leser (Weiß) stand auf Gewinn — „dir den Gewinn noch leichter gemacht".
        Assert.Equal("the reader was already winning — the opponent's move made the win even easier "
                     + "(evaluation from the reader's view: +3.0 before, +4.0 after).",
            ExplanationFacts.Situation(Flaw(white: false, "-3.00", "-4.00"), "white"));
        Assert.Equal("the reader was already clearly better — the opponent's move increased the reader's advantage "
                     + "(evaluation from the reader's view: +1.2 before, +2.0 after).",
            ExplanationFacts.Situation(Flaw(white: false, "-1.20", "-2.00"), "white"));
        Assert.Equal("for the reader the position went from about equal to clearly better "
                     + "(evaluation from the reader's view: +0.1 before, +2.0 after).",
            ExplanationFacts.Situation(Flaw(white: false, "-0.10", "-2.00"), "white"));
        Assert.Equal("for the reader the position went from lost to winning (forced mate) "
                     + "(evaluation from the reader's view: -3.0 before, mate in 2 after).",
            ExplanationFacts.Situation(Flaw(white: true, "+3.00", "gets mated in 2"), "black"));
    }

    [Fact]
    public void Situation_UnknownReader_NamesTheMover_AndMissingEvalIsNull()
    {
        Assert.Equal("for Black the position went from about equal to lost (getting mated) (evaluation from Black's view: -0.2 before, gets mated in 1 after).",
            ExplanationFacts.Situation(Flaw(white: false, "-0.20", "gets mated in 1"), ""));
        Assert.Null(ExplanationFacts.Situation(Flaw(white: true, "", "+1.00"), "white"));
    }

    private const string AfterE4D5 = "rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2";

    [Fact]
    public void LineEvents_MaterialBalance_OnlyWhenSomeoneEndsAhead()
    {
        Assert.Equal(["White ends up ahead in material by 1 (White took pawn, Black took nothing)"],
            ExplanationFacts.LineEvents(AfterE4D5, ["exd5"]));
        Assert.Empty(ExplanationFacts.LineEvents(AfterE4D5, ["exd5", "Qxd5"]));   // Abtausch = nichts gewonnen
        Assert.Empty(ExplanationFacts.LineEvents(AfterE4D5, []));
    }

    /// <summary>Prod-Partie 35 (0.585.1): 21…Tc4 22.Txc4 dxc4 ist ein Turmtausch. Ab der Stellung NACH Txc4 gerechnet stand
    /// „Black ends up ahead by 5", und das Modell schrieb „du gewinnst sofort einen Turm" — dabei schlägt Schwarz nur zurück.
    /// Der Fehler selbst gehört in die Bilanz (<c>lead</c>).</summary>
    internal const string Game35Before22 = "5rk1/p4pp1/1q3b2/3p3p/2rNb2P/4P1PB/P2Q1P2/2R2RK1 w - - 2 22";
    internal static readonly string[] Game35Answer = ["dxc4", "Rd1", "Rd8", "Bf1", "Bf3", "Re1", "Ba8", "Bg2", "Bxg2", "Kxg2"];

    [Fact]
    public void LineEvents_TheMistakesOwnCapture_CountsToo_SoARecaptureIsNoGain()
    {
        Assert.Empty(ExplanationFacts.LineEvents(Game35Before22, Game35Answer, gainerWhite: false, lead: "Rxc4"));
        Assert.Empty(ExplanationFacts.LineEvents(Game35Before22, Game35Answer, lead: "Rxc4"));
        // Zum Vergleich der alte Weg ab der Stellung danach — genau diese Zeile ging ans Modell.
        Assert.Contains(ExplanationFacts.LineEvents("5rk1/p4pp1/1q3b2/3p3p/2RNb2P/4P1PB/P2Q1P2/5RK1 b - - 0 22", Game35Answer),
            e => e.StartsWith("Black ends up ahead in material by 5"));
    }

    [Fact]
    public void LineEvents_Lead_OnlyCountsMaterial_ChecksAreAboutTheLine()
    {
        Assert.Empty(ExplanationFacts.LineEvents(AfterE4D5, ["Qxd5"], lead: "exd5"));                       // Tausch
        Assert.Empty(ExplanationFacts.LineEvents(AfterE4D5, ["Nf6"], gainerWhite: false, lead: "exd5"));  // Schwarz holt nichts
        // Der Vorzug gibt Schach — das ist kein Ereignis der Antwort-Linie; die Bilanz zählt ihn aber mit.
        Assert.Equal(["Black ends up ahead in material by 2 (White took pawn, Black took bishop)"],
            ExplanationFacts.LineEvents("r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 2 3", ["Kxf7"], lead: "Bxf7+"));
    }

    /// <summary>Holt in der Antwort-Linie die FALSCHE Seite etwas, ist das kein Grund für den Fehler (Partie 34, 33…La6).</summary>
    [Fact]
    public void LineEvents_GainerFilter()
    {
        Assert.Single(ExplanationFacts.LineEvents(AfterE4D5, ["exd5"], gainerWhite: true));
        Assert.Empty(ExplanationFacts.LineEvents(AfterE4D5, ["exd5"], gainerWhite: false));
    }

    [Fact]
    public void LineEvents_MateAndFirstMoveCheck()
    {
        const string beforeNf6 = "r1bqkbnr/pppp1ppp/2n5/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR b KQkq - 3 3";
        Assert.Equal(["the line ends in checkmate (Qxf7#)", "White ends up ahead in material by 1 (White took pawn, Black took nothing)"],
            ExplanationFacts.LineEvents(beforeNf6, ["Nf6", "Qxf7#"]));
        Assert.Equal(["Bxf7+ gives check", "White ends up ahead in material by 1 (White took pawn, Black took nothing)"],
            ExplanationFacts.LineEvents("r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 2 3", ["Bxf7+"]));
    }

    [Fact]
    public void LineEvents_StopsAtAnUnplayableMove_AndNeverThrows()
    {
        Assert.Equal(["White ends up ahead in material by 1 (White took pawn, Black took nothing)"],
            ExplanationFacts.LineEvents(AfterE4D5, ["exd5", "Zz9", "Qxd5"]));
        Assert.Empty(ExplanationFacts.LineEvents("kein fen", ["e4"]));
    }

    [Fact]
    public void FirstMoveAttacks_ListsAttackedPieces_ButNoPawnsOrKing()
    {
        // Nd3-e5 greift Turm d7 und Dame f7 an; den Bauern c4 nennt es nicht.
        var attacks = ExplanationFacts.FirstMoveAttacks("4k3/3r1q2/8/8/2p5/3N4/8/4K3 w - - 0 1", ["Ne5", "Kd8"]);
        Assert.Equal(2, attacks.Count);
        Assert.Contains("after Ne5 the knight on e5 attacks the rook on d7", attacks);
        Assert.Contains("after Ne5 the knight on e5 attacks the queen on f7", attacks);
    }

    [Fact]
    public void FirstMoveAttacks_CheckOrUnplayable_IsEmpty()
    {
        // Ein Schach steht schon in LineEvents; mit getauschtem Zugrecht wäre die Stellung ungültig.
        Assert.Empty(ExplanationFacts.FirstMoveAttacks("r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 2 3", ["Bxf7+"]));
        Assert.Empty(ExplanationFacts.FirstMoveAttacks(AfterE4D5, ["Nf6"]));
        Assert.Empty(ExplanationFacts.FirstMoveAttacks("kein fen", ["e4"]));
    }

    // ── Schach mit Doppelangriff (0.971.0, Vereinspartie 245: 25...Lc8 26.Txc8+ Txc8 27.Sd7+) ──────────────────────────

    /// <summary>Nach 25...Lc8 26.Txc8+ Txc8 — Weiß am Zug.</summary>
    private const string Game245AfterRxc8 = "1kr5/pp2n3/1q2pb1r/1P1pN1p1/3P2P1/P2BQ3/4NPK1/R7 w - - 0 27";

    [Fact]
    public void CheckForks_KnightForkWithCheck_IsNamed_WithEveryAttackedPiece()
    {
        // 27.Sd7+ gabelt König b8, Dame b6 und Läufer f6; d7 deckt Schwarz nicht. 28.Sxb6+ gibt auch Schach und greift den
        // Turm c8 an, kann aber geschlagen werden (axb6) — kein Fakt.
        Assert.Equal(["Nd7+ gives check and the knight on d7 also attacks the queen on b6 and the bishop on f6 (a fork); "
                      + "the knight on d7 cannot be captured"],
            ExplanationFacts.CheckForks(Game245AfterRxc8, ["Nd7+", "Ka8", "Nxb6+", "axb6"]));
    }

    [Fact]
    public void CheckForks_OnlyWhenThePieceCannotBeCaptured_AndOnlyTheSideThatStartsTheLine()
    {
        Assert.Equal(["Nc7+ gives check and the knight on c7 also attacks the rook on a8 (a fork); the knight on c7 cannot be captured"],
            ExplanationFacts.CheckForks("r3k3/8/8/1N6/8/8/8/4K3 w - - 0 1", ["Nc7+", "Kd7", "Nxa8"]));
        // Mit der Dame auf d8 wird der Springer einfach geschlagen.
        Assert.Empty(ExplanationFacts.CheckForks("r2qk3/8/8/1N6/8/8/8/4K3 w - - 0 1", ["Nc7+", "Qxc7"]));
        // Ein Schach ohne zweites Ziel ist keine Gabel; Bauern zählen nicht als Ziel.
        Assert.Empty(ExplanationFacts.CheckForks("4k3/p7/8/1N6/8/8/8/4K3 w - - 0 1", ["Nc7+", "Kd7"]));
        // Die Gabel des VERTEIDIGERS (zweiter Halbzug der Linie) ist kein Grund für den Fehler.
        Assert.Empty(ExplanationFacts.CheckForks("4k3/8/8/8/1n6/8/8/R3K3 w - - 0 1", ["Ra3", "Nc2+", "Kd2"]));
        // Dieselbe Gabel als erster Zug der Linie zählt — außer sie liegt hinter der Grenze.
        Assert.Single(ExplanationFacts.CheckForks("4k3/8/8/8/1n6/8/8/R3K3 b - - 0 1", ["Nc2+", "Kd2", "Nxa1"]));
        Assert.Empty(ExplanationFacts.CheckForks("4k3/8/8/8/1n6/8/8/R3K3 b - - 0 1", ["Nc2+", "Kd2", "Nxa1"], maxPlies: 0));
        Assert.Empty(ExplanationFacts.CheckForks("kein fen", ["Nc7+"]));
        Assert.Empty(ExplanationFacts.CheckForks(Game245AfterRxc8, ["Zz9"]));
    }

    [Fact]
    public void CheckForks_DiscoveredCheck_IsCalledThat()
    {
        // Der Springer zieht aus der e-Linie (Abzugsschach des Turms) und greift dabei den Turm b7 an.
        Assert.Equal(["Nc5+ gives check and the knight on c5 also attacks the rook on b7 (a discovered check); the knight on c5 cannot be captured"],
            ExplanationFacts.CheckForks("4k3/1r6/8/8/4N3/8/8/4RK2 w - - 0 1", ["Nc5+", "Kd8"]));
    }

    [Fact]
    public void LineEvents_SeveralLeads_CountMaterialFromBeforeTheMistake()
    {
        // 25...Lc8 26.Txc8+ Txc8 | 27.Sd7+ Ka8 28.Sxb6+ axb6: Weiß nahm Läufer und Dame, Schwarz Turm und Springer.
        var events = ExplanationFacts.LineEvents("1k4r1/pp1bn3/1q2pb1r/1P1pN1p1/3P2P1/P1RBQ3/4NPK1/R7 b - - 2 25",
            ["Nd7+", "Ka8", "Nxb6+", "axb6"], gainerWhite: true, lead: "Bc8", leads: ["Rxc8+", "Rxc8"]);
        Assert.Equal(["Nd7+ gives check", "White ends up ahead in material by 4 (White took bishop, queen, Black took rook, knight)"], events);
    }

    // ── Brettgeometrie ───────────────────────────────────────────────────────────────────────────────────────────────

    // ── Abzugsangriff und verlorene Figur (0.974.0, Vereinspartie 251: 20...e4 21.Txe4 dxc3 22.bxc3 Dxa3) ──

    /// <summary>Nach 20...e4 (Weiß am Zug) bzw. nach 21.Txe4 (Schwarz am Zug).</summary>
    private const string Game251AfterE4 = "r4r1k/1p4bp/p1nq2p1/5nB1/2Bpp3/N1P3P1/PP3P1P/R2QR1K1 w - - 0 21";
    private const string Game251AfterRxe4 = "r4r1k/1p4bp/p1nq2p1/5nB1/2BpR3/N1P3P1/PP3P1P/R2Q2K1 b - - 0 21";

    [Fact]
    public void DiscoveredAttacks_PawnLeavesTheFile_QueenBehindItAttacksTheQueen()
    {
        Assert.Equal(
            ["dxc3 opens the d-file for the queen on d6, which now attacks the queen on d1 (a discovered attack)"],
            ExplanationFacts.DiscoveredAttacks(Game251AfterRxe4, ["dxc3", "bxc3", "Qxa3", "Rb1"]));
    }

    [Fact]
    public void DiscoveredAttacks_FileRankDiagonal_OnlyPiecesNotPawnsOrKing_OnlyTheSideThatStartsTheLine()
    {
        Assert.Equal(["Bb5 opens the e-file for the rook on e1, which now attacks the rook on e8 (a discovered attack)"],
            ExplanationFacts.DiscoveredAttacks("4r1k1/8/8/8/8/8/4B3/4R1K1 w - - 0 1", ["Bb5"]));
        Assert.Equal(["Nb5 opens the 4th rank for the rook on a4, which now attacks the rook on h4 (a discovered attack)"],
            ExplanationFacts.DiscoveredAttacks("8/8/8/8/R2N3r/8/8/K6k w - - 0 1", ["Nb5"]));
        Assert.Equal(["Nf5 opens the diagonal for the bishop on b2, which now attacks the queen on g7 (a discovered attack)"],
            ExplanationFacts.DiscoveredAttacks("7k/6q1/8/8/3N4/8/1B6/K7 w - - 0 1", ["Nf5"]));
        // Ein Abzugsschach ist nicht hier zu Hause (CheckForks/LineEvents), ein aufgedeckter Bauer kein Grund.
        Assert.Empty(ExplanationFacts.DiscoveredAttacks("4k3/8/8/8/8/8/4B3/4R1K1 w - - 0 1", ["Bb5+"]));
        Assert.Empty(ExplanationFacts.DiscoveredAttacks("6k1/4p3/8/8/8/8/4B3/4R1K1 w - - 0 1", ["Bb5"]));
        // Der Zug des GEGNERS in der Linie (Halbzug 2) deckt hier den Turm e8 auf e1 auf — das ist nicht der Grund des Lesers.
        Assert.Empty(ExplanationFacts.DiscoveredAttacks("4r1k1/8/8/8/8/8/4b3/4R1K1 w - - 0 1", ["Kh1", "Bb5"]));
        // Die gezogene Figur selbst zählt nicht (das meldet FirstMoveAttacks).
        Assert.Empty(ExplanationFacts.DiscoveredAttacks("4r1k1/8/8/8/8/8/8/R5K1 w - - 0 1", ["Re1"]));
    }

    [Fact]
    public void HangingCaptures_Fall251_DerSpringerA3Faellt_weilDerBauerB2WeggelenktIst()
    {
        Assert.Equal(
            ["Qxa3 takes the knight on a3 and the queen on a3 cannot be recaptured "
             + "(at the start of this line the knight was defended only by the pawn on b2, which is no longer there)"],
            ExplanationFacts.HangingCaptures(Game251AfterE4, ["dxc3", "bxc3", "Qxa3", "Rb1"], leads: ["Rxe4"]));
    }

    [Fact]
    public void HangingCaptures_NurWasNichtZurueckgeschlagenWerdenKann_keinTausch_keineBauern()
    {
        // Ganz ungedeckt von Anfang an.
        Assert.Equal(["Rxd5 takes the knight on d5 and the rook on d5 cannot be recaptured (the knight was not defended at the start of this line)"],
            ExplanationFacts.HangingCaptures("4k3/8/8/3n4/8/8/8/3RK3 w - - 0 1", ["Rxd5"]));
        // Gedeckt: der Turm d8 schlägt zurück — kein Figurengewinn.
        Assert.Empty(ExplanationFacts.HangingCaptures("3rk3/8/8/3n4/8/8/8/3RK3 w - - 0 1", ["Rxd5", "Rxd5"]));
        // Zurückschlagen ist ein Tausch: der Zug vor der Linie hat auf demselben Feld geschlagen.
        Assert.Empty(ExplanationFacts.HangingCaptures("3rk3/8/8/3n4/8/8/8/3RK3 w - - 0 1", ["Rxd5"], leads: ["Rxd5"]));
        // Ein Bauer ist keine Figur.
        Assert.Empty(ExplanationFacts.HangingCaptures("4k3/8/8/3p4/8/8/8/3RK3 w - - 0 1", ["Rxd5"]));
        // Nur die Seite, die die Linie beginnt.
        Assert.Empty(ExplanationFacts.HangingCaptures("4k3/8/8/3n4/8/8/8/3RK3 b - - 0 1", ["Ke7", "Rxd5"]));
    }

    [Fact]
    public void BoardGeometry_AttackedFrom_SlidersStopAtTheFirstPiece_Inclusive()
    {
        var geo = BoardGeometry.FromFen("1kb3r1/pp2n3/1q2pb1r/1P1pN1p1/3P2P1/P1RBQ3/4NPK1/R7 w - - 3 26")!;
        Assert.Equal('R', geo.PieceAt("c3"));
        Assert.Equal('\0', geo.PieceAt("c4"));
        Assert.Equal('\0', geo.PieceAt("z9"));
        // Turm c3: die c-Linie hinauf bis zum Läufer c8 (einschließlich), hinunter bis c1, seitlich bis b3 und zum eigenen Läufer d3.
        Assert.Equal(["a3", "b3", "c1", "c2", "c4", "c5", "c6", "c7", "c8", "d3"], geo.AttackedFrom("c3").Order(StringComparer.Ordinal));
        // Springer e5 — auch die Felder mit eigenen Figuren (gedeckt).
        Assert.Equal(["c4", "c6", "d3", "d7", "f3", "f7", "g4", "g6"], geo.AttackedFrom("e5").Order(StringComparer.Ordinal));
        // Bauern schlagen schräg nach vorn: Weiß hinauf, Schwarz hinunter.
        Assert.Equal(["a6", "c6"], geo.AttackedFrom("b5").Order(StringComparer.Ordinal));
        Assert.Equal(["c4", "e4"], geo.AttackedFrom("d5").Order(StringComparer.Ordinal));
        Assert.Empty(geo.AttackedFrom("c4"));
    }

    [Fact]
    public void BoardGeometry_Attackers_WhoCoversASquare()
    {
        // Nach 25...Lc8 deckt nur der Läufer c8 das Feld d7; c8 selbst decken König, Turm und Springer.
        var geo = BoardGeometry.FromFen("1kb3r1/pp2n3/1q2pb1r/1P1pN1p1/3P2P1/P1RBQ3/4NPK1/R7 w - - 3 26")!;
        Assert.Equal([('b', "c8")], geo.Attackers("d7", white: false));
        Assert.Equal([('n', "e7"), ('k', "b8"), ('r', "g8")], geo.Attackers("c8", white: false));
        Assert.Equal([('N', "e5")], geo.Attackers("d7", white: true));
        Assert.Null(BoardGeometry.FromFen("kein fen"));
        Assert.Null(BoardGeometry.FromFen("8/8/8/8/8/8/8/9 w - - 0 1"));
        Assert.Null(BoardGeometry.FromFen(null));
    }
}
