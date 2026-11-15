using RookHub.Api.DTOs.Games;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>
/// „Ohnehin verloren" (Wunsch 2026-11-15, Vereinspartie 234: „Mistake 13 of 13, Inaccuracy −6.58 → −11.64 — ab −4 keine
/// Fehler mehr"): stand der Ziehende VOR dem Zug bei −4,00 oder schlechter, ist der Zug kein Fehler. Dieselben LITERALEN
/// Werte prüft <c>game-review.util.spec.ts</c> („ohnehin verloren") — die Regel steht auf beiden Seiten.
/// </summary>
public class GameMistakesTests
{
    // 1.e4 c5 2.Sf3 d6
    private static readonly string[] Fens =
    {
        "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
        "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1",
        "rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq c6 0 2",
        "rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2",
        "rnbqkbnr/pp2pppp/3p4/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 3",
    };
    private static readonly string[] Ucis = ["e2e4", "c7c5", "g1f3", "d7d6"];
    private static readonly string[] Sans = ["e4", "c5", "Nf3", "d6"];

    [Fact]
    public void LostAnyway_GrenzeMinusVier_GanzzahligAusSichtDesZiehenden()
    {
        Assert.Equal(-4, GameMistakes.LostAnywayPawns);
        Assert.True(GameMistakes.LostAnyway(-400, null, moverWhite: true));
        Assert.False(GameMistakes.LostAnyway(-399, null, moverWhite: true));
        Assert.True(GameMistakes.LostAnyway(400, null, moverWhite: false));    // Weiß-Sicht +4,00 = Schwarz bei −4,00
        Assert.False(GameMistakes.LostAnyway(399, null, moverWhite: false));
        Assert.False(GameMistakes.LostAnyway(658, null, moverWhite: true));    // die Gewinnerseite nie
        Assert.False(GameMistakes.LostAnyway(-658, null, moverWhite: false));
        Assert.False(GameMistakes.LostAnyway(null, null, moverWhite: true));
    }

    [Fact]
    public void LostAnyway_MattGegenDenZiehenden_zaehltAlsSchlechter()
    {
        Assert.True(GameMistakes.LostAnyway(null, -3, moverWhite: true));
        Assert.True(GameMistakes.LostAnyway(null, 3, moverWhite: false));
        Assert.False(GameMistakes.LostAnyway(null, 3, moverWhite: true));
        Assert.False(GameMistakes.LostAnyway(null, -3, moverWhite: false));
        Assert.True(GameMistakes.LostAnyway(null, 0, moverWhite: true));
    }

    [Theory]
    [InlineData(8.145, 2.455, false, -658, "good")]         // Partie 234: −6,58 → −11,64, bisher Ungenauigkeit
    [InlineData(18.707, 2.455, false, -399, "mistake")]     // −3,99 → −11 bleibt ein Fehler
    [InlineData(18.651, 2.455, false, -400, "good")]        // genau −4,00 zählt schon
    [InlineData(18.651, 17.651, false, -400, "excellent")]  // nur Fehlerklassen werden ersetzt
    [InlineData(18.651, 2.455, true, -400, "best")]
    [InlineData(90.108, 59.103, false, 600, "blunder")]     // Gewinnerseite: +6 → +1 bleibt ein grober Fehler
    public void Classify_MitBewertungDavor(double before, double after, bool isBest, int cpBefore, string expected)
        => Assert.Equal(expected, GameMistakes.Classify(before, after, isBest, cpBefore, null, moverWhite: true));

    /// <summary>Weiß-Sicht je Stellung 0..4 (Zentibauern); die Halbzüge in <paramref name="bad"/> sind nicht der Bestzug.</summary>
    private static (List<GameEvalPlyDto> Plies, GameEvalScoreDto Final) Game(int[] cp, params int[] bad)
    {
        var plies = Enumerable.Range(0, 4).Select(i => new GameEvalPlyDto
        {
            Ply = i, Depth = 20, Cp = cp[i], BestUci = bad.Contains(i) ? "a2a3" : Ucis[i], PlayedUci = Ucis[i],
        }).ToList();
        return (plies, new GameEvalScoreDto { Cp = cp[4] });
    }

    private static string Classes(int[] cp, params int[] bad)
    {
        var (plies, final) = Game(cp, bad);
        return GameMoveClasses.Compute(plies, final, Fens, Ucis, 4);
    }

    [Fact]
    public void Compute_Partie234_MinusSechsAchtundfuenfzig_istKeinFehlerMehr()
    {
        Assert.Equal("SSSO", Classes([658, 658, 658, 658, 1164], 3));        // Schwarz −6,58 → −11,64
        Assert.Equal("SSOS", Classes([-658, -658, -658, -1164, -1164], 2));  // Weiß genauso (Vorzeichen)
    }

    [Fact]
    public void Compute_Grenze_MinusDreiNeunundneunzigBleibtFehler_MinusVierNicht()
    {
        Assert.Equal("SSSM", Classes([399, 399, 399, 399, 1100], 3));
        Assert.Equal("SSSO", Classes([400, 400, 400, 400, 1100], 3));
    }

    [Fact]
    public void Compute_Gewinnerseite_bleibtGroberFehler()
        => Assert.Equal("SSPS", Classes([600, 600, 600, 100, 100], 2));

    [Fact]
    public void Compute_MissFragtNachDerBereinigtenKlasseDesGegners()
    {
        // Schwarz 1…c5 von −4,00 auf −10,00 (kein Fehler mehr) → Weiß 2.Sf3 (+10,00 → +0,40) ist ein grober Fehler, kein Miss.
        Assert.Equal("SOPS", Classes([400, 400, 1000, 40, 40], 1, 2));
        // Ab −3,99 macht Schwarz einen Fehler, und Weiß verpasst die Strafe.
        Assert.Equal("SMXS", Classes([399, 399, 1000, 40, 40], 1, 2));
    }

    /// <summary>Positionszeilen wie in der DB: Kandidaten aus Sicht der Seite AM ZUG, der Bestzug zuerst.</summary>
    private static List<GameAnalysisPosition> Positions(params string[] candidates)
        => Enumerable.Range(0, 4).Select(i => new GameAnalysisPosition
        {
            Ply = i, Fen = Fens[i], GameMoveUci = Ucis[i], GameMoveSan = Sans[i], CandidatesJson = candidates[i], Depth = 20,
        }).ToList();

    [Fact]
    public void Find_Partie234_MinusSechsAchtundfuenfzig_keinFehler()
    {
        Assert.Empty(GameMistakes.Find(Positions(
            """[{"uci":"e2e4","cp":658}]""", """[{"uci":"c7c5","cp":-658}]""", """[{"uci":"g1f3","cp":658}]""",
            """[{"uci":"b8c6","cp":-658},{"uci":"d7d6","cp":-1164}]"""), 4));
        // Weiß am Zug (Vorzeichen).
        Assert.Empty(GameMistakes.Find(Positions(
            """[{"uci":"e2e4","cp":-658}]""", """[{"uci":"c7c5","cp":658}]""",
            """[{"uci":"d2d4","cp":-658},{"uci":"g1f3","cp":-1164}]""", """[{"uci":"d7d6","cp":1164}]"""), 4));
    }

    [Fact]
    public void Find_Grenze_MinusDreiNeunundneunzigBleibtFehler_MinusVierNicht()
    {
        var f = Assert.Single(GameMistakes.Find(Positions(
            """[{"uci":"e2e4","cp":399}]""", """[{"uci":"c7c5","cp":-399}]""", """[{"uci":"g1f3","cp":399}]""",
            """[{"uci":"b8c6","cp":-399},{"uci":"d7d6","cp":-1100}]"""), 4));
        Assert.Equal(3, f.Ply);
        Assert.Equal("mistake", f.Class);
        Assert.Equal("-3.99", f.EvalBefore);

        Assert.Empty(GameMistakes.Find(Positions(
            """[{"uci":"e2e4","cp":400}]""", """[{"uci":"c7c5","cp":-400}]""", """[{"uci":"g1f3","cp":400}]""",
            """[{"uci":"b8c6","cp":-400},{"uci":"d7d6","cp":-1100}]"""), 4));
    }

    [Fact]
    public void Find_MattGegenDenZiehendenDavor_keinFehler()
        => Assert.Empty(GameMistakes.Find(Positions(
            """[{"uci":"e2e4","mate":-4}]""", """[{"uci":"c7c5","mate":4}]""",
            """[{"uci":"d2d4","mate":-3},{"uci":"g1f3","mate":-2}]""", """[{"uci":"d7d6","mate":2}]"""), 4));

    [Fact]
    public void Find_Gewinnerseite_bleibtGroberFehler()
    {
        var f = Assert.Single(GameMistakes.Find(Positions(
            """[{"uci":"e2e4","cp":600}]""", """[{"uci":"c7c5","cp":-600}]""",
            """[{"uci":"d2d4","cp":600},{"uci":"g1f3","cp":100}]""", """[{"uci":"d7d6","cp":-100}]"""), 4));
        Assert.Equal(2, f.Ply);
        Assert.Equal("blunder", f.Class);
    }

    [Fact]
    public void Find_MissFragtNachDerBereinigtenKlasseDesGegners()
    {
        string[] Rows(int cp) =>
        [
            $$"""[{"uci":"e2e4","cp":{{cp}}}]""", $$"""[{"uci":"e7e5","cp":{{-cp}}},{"uci":"c7c5","cp":-1000}]""",
            """[{"uci":"d2d4","cp":1000},{"uci":"g1f3","cp":40}]""", """[{"uci":"d7d6","cp":-40}]""",
        ];
        var lost = Assert.Single(GameMistakes.Find(Positions(Rows(400)), 4));
        Assert.Equal((2, "blunder"), (lost.Ply, lost.Class));
        Assert.Equal([(1, "mistake"), (2, "miss")], GameMistakes.Find(Positions(Rows(399)), 4).Select(f => (f.Ply, f.Class)));
    }
}
