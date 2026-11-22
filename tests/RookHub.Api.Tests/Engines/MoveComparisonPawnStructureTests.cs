using RookHub.Api.Models;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Engines;

/// <summary>
/// Bauernstruktur-Fakten (1.001.1, <see cref="PawnStructure"/>). Befund Vereinspartie 261, Fehler 21...exf3 statt 21...h5
/// (22.11.2026): nach 21...h5 22.Te1 Te8 23.Tbd1 a5 24.Lf1 Tad8 25.fxe4 Txd1 26.Txd1 fxe4 hat Schwarz einen isolierten
/// e-Bauern, der Text sagte nur „steht ausgeglichen (−0,2)“ — der Nutzer: „warum ist das kein Problem für Schwarz?“.
/// Grund: e4 ist ein Freibauer, und Weiß hat dafür die Mehrheit am Damenflügel.
/// </summary>
public class MoveComparisonPawnStructureTests
{
    /// <summary>Stellung vor 21...exf3/21...h5 (Schwarz am Zug).</summary>
    private const string Before21 = "r1r3k1/p6p/2p1b1p1/2B1bp2/2P1p3/1P3P2/P3B1PP/1R1R2K1 b - - 0 21";
    /// <summary>Die Endstellung der Linie nach 26...fxe4 (Stockfish 0,00).</summary>
    private const string End26 = "4r1k1/8/2p1b1p1/p1B1b2p/2P1p3/1P6/P5PP/3R1BK1 w - - 0 27";

    private static string Json(int cp, string moves)
        => "{\"depth\":30,\"pvs\":[{\"depth\":30,\"cp\":" + cp + ",\"moves\":["
           + string.Join(',', moves.Split(' ').Select(x => $"\"{x}\"")) + "]}]}";

    private static MoveComparisonPrompts.PairFacts Facts()
    {
        var best = MoveComparisonBoard.PlayUci(Before21, "h7h5")!;
        var weak = MoveComparisonBoard.PlayUci(Before21, "e4f3")!;
        var bestReplies = MoveComparisonEval.Pvs(best, Json(20, "d1e1 c8e8 b1d1 a7a5 e2f1 a8d8 f3e4 d8d1 e1d1 f5e4"));
        var weakReplies = MoveComparisonEval.Pvs(weak, Json(90, "e2f3 h7h5"));
        return new MoveComparisonPrompts.PairFacts(Before21, false, 30, "h5", best, "-0.20", bestReplies,
            "exf3", weak, "-0.90", weakReplies, []);
    }

    [Fact]
    public void Notable_EndstellungNach26Fxe4_isolierterFreibauerUndMehrheiten()
    {
        Assert.Equal(
            ["Black's pawn e4 is isolated but passed", "White has a 3-vs-2 queenside majority", "Black has a 3-vs-2 kingside majority"],
            PawnStructure.Notable(Before21, End26));
        // Nichts Neues gegenüber der Ausgangsstellung (c6 und a7 waren schon isoliert) → keine Fakten, auch keine Mehrheiten.
        Assert.Empty(PawnStructure.Notable(Before21, Before21));
    }

    [Fact]
    public void Flags_Isolani_Doppelbauer_Rueckstaendig_Blockade()
    {
        // Weiß: Isolani d4 (halboffene Linie, blockiert durch den Springer d5); Doppelbauern auf der f-Linie.
        var geo = BoardGeometry.FromFen("4k3/pp3ppp/4p3/3n4/3P4/5P2/5PPP/4K3 w - - 0 1")!;
        Assert.Equal(new PawnStructure.Flags(true, false, false, false, true, "the black knight on d5"), PawnStructure.Of(geo, "d4"));
        Assert.True(PawnStructure.Of(geo, "f2")!.Doubled);
        // Rückständig: c3 hinter b4/d4, c4 deckt der Bauer b5/d5 des Gegners.
        var back = BoardGeometry.FromFen("4k3/8/8/1p6/1P1P4/2P5/8/4K3 w - - 0 1")!;
        Assert.True(PawnStructure.Of(back, "c3")!.Backward);
        Assert.Null(PawnStructure.Of(back, "e4"));
    }

    [Fact]
    public void UserPrompt_bestLinie_nenntDieStruktur_beideModelle()
    {
        foreach (var detailed in new[] { false, true })
        {
            var prompt = MoveComparisonPrompts.UserPrompt(Facts(), detailed);
            Assert.Contains("Concretely, after 21...h5 and that line: pawn structure at the end of this line (after 26...fxe4): "
                + "Black's pawn e4 is isolated but passed; White has a 3-vs-2 queenside majority; Black has a 3-vs-2 kingside majority.", prompt);
        }
    }

    [Fact]
    public void Auftraege_StrukturschwaecheBeiGleicherBewertung_einSatzAusDenFakten()
    {
        Assert.Contains("auffällige Bauernschwäche", ClubFableExplanations.SystemPrompt);
        Assert.Contains("warum sie nicht zählt", ClubFableExplanations.SystemPrompt);
        Assert.Contains("why it\ndoes not matter", MoveComparisonPrompts.SystemPrompt("de").Replace("\r", ""));
    }

    [Fact]
    public void Nachpruefung_Strukturaussagen_amBrett()
    {
        var f = Facts();
        Assert.Empty(MoveComparisonClaims.FalsePawnClaims("Nach 26...fxe4 ist der Bauer e4 isoliert, aber ein Freibauer.", f));
        Assert.Empty(MoveComparisonClaims.FalsePawnClaims("Der Bauer e4 ist nicht isoliert.", f));   // verneint: nicht geprüft
        Assert.Equal(["c4"], MoveComparisonClaims.FalsePawnClaims("Weiß hat auf c4 einen Freibauern.", f));
        Assert.Equal(["g2"], MoveComparisonClaims.FalsePawnClaims("The pawn on g2 is doubled.", f));
        Assert.Equal(["c4"], MoveComparisonClaims.Check("Der Bauer c4 ist ein Freibauer.", f).Pawns);
        Assert.Contains("Bauern auf c4 falsch", MoveComparisonClaims.Hint([], [], german: true, ["c4"]));
    }
}
