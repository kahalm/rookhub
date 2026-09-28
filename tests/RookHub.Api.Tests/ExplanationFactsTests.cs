using RookHub.Api.Services;

namespace RookHub.Api.Tests;

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
}
