using RookHub.Api.Models;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Engines;

/// <summary>
/// Vereinspartie 261, Fehler 19...Le5 statt 19...f4 (Prod-Vergleich 2854, 22.11.2026): die Widerlegung 20.f4 greift den Läufer
/// an. Der Text erklärte lang, warum 20...Lxf4 nicht geht (21.Lxf4, Figur weg — „nutzlos, absolut offensichtlich“), und
/// sagte nichts zur nicht offensichtlichen Verteidigung 20...exf3 e.p.; die Auswahl der anderen Verteidigungen
/// (<see cref="MoveComparisonDefences"/>) kannte den En-passant-Schlag nicht (Zielfeld f3, geschlagen wird f4). Dazu stand
/// im Text „das Feld f4 wurde nur vom Läufer selbst gedeckt“ — f4 ist nach 20.f4 durch den Läufer e3 gedeckt.
/// </summary>
public class MoveComparisonObviousDefenceTests
{
    /// <summary>Stellung vor 19...Le5 (Schwarz am Zug).</summary>
    private const string Fen2854 = "r4rk1/p5bp/2p1b1p1/5p2/2P1p3/1P2B3/P3BPPP/1R1R2K1 b - - 0 19";
    private const string Best = "f5f4";
    private const string Weak = "g7e5";
    /// <summary>Die Prod-Linien von 2854 (gekürzt), Weiß-Sicht.</summary>
    private const string BestJson = """{"depth":30,"pvs":[{"moves":["e3c5","f8e8","d1d6","e6f5"],"cp":6}]}""";
    private const string WeakJson = """{"depth":30,"pvs":[{"moves":["f2f4","e5c7","b3b4","a7a6","a2a4","f8b8"],"cp":65}]}""";
    /// <summary>Die Prod-Zeile 20...Lxf4 (Kind=2): 21.Lxf4 Tac8 …</summary>
    private const string BxF4Json = """{"depth":30,"pvs":[{"moves":["e3f4","a8c8","d1d6","e6f7"],"cp":701}]}""";
    /// <summary>20...exf3 e.p. 21.Lxf3 (ausgedacht, legal) — etwa so gut wie die Hauptlinie.</summary>
    private const string ExF3Json = """{"depth":30,"pvs":[{"moves":["e2f3","e5c7","f3c6","a8c8"],"cp":70}]}""";

    private static MoveComparison Comparison()
    {
        var c = new MoveComparison { Fen = Fen2854, Depth = 30, BestUci = Best, Language = "de", Status = MoveComparisonStatus.Explaining };
        c.Lines.Add(new MoveComparisonLine { Kind = MoveComparisonLineKind.Candidate, CandidateUci = Best, Ordinal = 0,
            Fen = MoveComparisonBoard.PlayUci(Fen2854, Best)!, State = MoveComparisonLineState.Done, ResultJson = BestJson, ReachedDepth = 30 });
        c.Lines.Add(new MoveComparisonLine { Kind = MoveComparisonLineKind.Candidate, CandidateUci = Weak, Ordinal = 1,
            Fen = MoveComparisonBoard.PlayUci(Fen2854, Weak)!, State = MoveComparisonLineState.Done, ResultJson = WeakJson, ReachedDepth = 30 });
        return c;
    }

    private static MoveComparison Explained()
    {
        var c = Comparison();
        foreach (var line in MoveComparisonDefences.Plan(c, MoveComparisonDefences.DefaultMax))
        {
            line.ResultJson = line.ReplyUci == "e5f4" ? BxF4Json : ExF3Json;
            line.ReachedDepth = 30;
            line.State = MoveComparisonLineState.Done;
            c.Lines.Add(line);
        }
        return c;
    }

    private static MoveComparisonPrompts.PairFacts Facts(MoveComparison c) =>
        MoveComparisonPrompts.PairFactsOf(c, c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == Weak))!;

    [Fact]
    public void Plan_EnPassant_zaehltAlsSchlagenDerGezogenenFigur_undKommtVorDemEinsteller()
    {
        var lines = MoveComparisonDefences.Plan(Comparison(), MoveComparisonDefences.DefaultMax);

        // Nach 20.f4: 20...exf3 e.p. (nicht offensichtlich) vor 20...Lxf4 (21.Lxf4 nimmt einfach zurück).
        Assert.Equal(["e4f3", "e5f4"], lines.Select(l => l.ReplyUci));
        Assert.All(lines, l => Assert.Equal(1, l.Ordinal));
        // Die Stellung nach dem En-passant-Schlag: Bauer f4 weg, schwarzer Bauer auf f3.
        Assert.StartsWith("r4rk1/p6p/2p1b1p1/4bp2/2P5/1P2Bp2/P3B1PP/1R1R2K1 w", lines[0].Fen);
    }

    [Fact]
    public void Plan_Deckel1_nimmtDieNichtOffensichtlicheVerteidigung()
    {
        Assert.Equal(["e4f3"], MoveComparisonDefences.Plan(Comparison(), 1).Select(l => l.ReplyUci));
    }

    [Fact]
    public void UserPrompt_exf3WirdErklaert_Lxf4NurAlsOffensichtlicherEinsteller()
    {
        var prompt = ClubFableExplanations.UserPrompt(Facts(Explained()));

        Assert.Contains("- 20...exf3 instead of 20...Bc7: evaluation from the reader's view -0.70", prompt);
        Assert.Contains("Engine line: 21.Bxf3 Bc7 22.Bxc6 Rac8.", prompt);
        Assert.Contains("- 20...Bxf4 instead of 20...Bc7: obviously loses material (21.Bxf4 simply recaptures) — do not discuss.", prompt);
        Assert.DoesNotContain("21.Bxf4 Rac8", prompt);
        Assert.DoesNotContain("Concretely, after 20...Bxf4", prompt);
        // Der irreführende Feld-Fakt „f4 nur vom Läufer gedeckt“ entsteht nicht mehr.
        Assert.DoesNotContain("the square f4 was covered only by the bishop on e5", prompt);
        // exf3 steht vor Lxf4.
        Assert.True(prompt.IndexOf("20...exf3 instead", StringComparison.Ordinal) < prompt.IndexOf("20...Bxf4 instead", StringComparison.Ordinal));
    }

    [Fact]
    public void AllowedMoves_ohneDenEinsteller_mitExf3()
    {
        var allowed = Facts(Explained()).AllowedMoves().ToList();
        Assert.Contains("exf3", allowed);
        Assert.Contains("Bxf3", allowed);
        Assert.DoesNotContain("Bxf4", allowed);
        Assert.False(GameMoveExplanationService.MentionsOnly(
            ClubFableExplanations.EnglishSan("20...Lxf4 scheitert an 21.Lxf4."), Facts(Explained()).AllowedMoves()));
        Assert.True(GameMoveExplanationService.MentionsOnly(
            ClubFableExplanations.EnglishSan("Auch 20...exf3 hilft nicht: 21.Lxf3."), Facts(Explained()).AllowedMoves()));
    }

    [Fact]
    public void ObviousLoss_nurMitSchlagUndMaterialverlust()
    {
        var c = Explained();
        // Ist die Erwiderung kein Schlag, bleibt 20...Lxf4 eine echte Verteidigung mit Linie.
        c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Defence && l.ReplyUci == "e5f4").ResultJson
            = """{"depth":30,"pvs":[{"moves":["d1d8","a8d8"],"cp":701}]}""";
        var prompt = ClubFableExplanations.UserPrompt(Facts(c));
        Assert.DoesNotContain("obviously loses material", prompt);
        Assert.Contains("- 20...Bxf4 instead of 20...Bc7: evaluation from the reader's view -7.01", prompt);
    }

    [Fact]
    public void Nachpruefung_nurVomLaeuferSelbstGedeckt_wirdBeanstandet()
    {
        var f = Facts(Explained());
        const string text = "Dabei ist 20...Bxf4 keine Alternative: Das Feld f4 wurde nach 19...Be5 nur vom Läufer selbst gedeckt, "
            + "nach 21.Bxf4 ist Weiß eine Figur für einen Bauern vorn.";
        Assert.Equal(["f4"], MoveComparisonClaims.FalseSoleDefender(text, f));
        Assert.Equal(["f4"], MoveComparisonClaims.FalseSoleDefender("After 20.f4 the pawn on f4 is defended only by the knight.", f));
        Assert.Equal(["f4"], MoveComparisonClaims.FalseSoleDefender("Die einzige Deckung des Bauern f4 ist der Turm auf d1.", f));
        // Wahr: der Bauer f4 ist nur vom Läufer (e3) gedeckt.
        Assert.Empty(MoveComparisonClaims.FalseSoleDefender("Der Bauer auf f4 ist nur vom Läufer auf e3 gedeckt.", f));
        Assert.Empty(MoveComparisonClaims.FalseSoleDefender("The pawn on f4 is defended only by the bishop.", f));
        Assert.Empty(MoveComparisonClaims.FalseSoleDefender("Der Läufer auf e5 ist gut gedeckt.", f));
        Assert.Contains("nur von einer Figur gedeckt", MoveComparisonClaims.Hint([], ["f4"], german: true));
    }
}
