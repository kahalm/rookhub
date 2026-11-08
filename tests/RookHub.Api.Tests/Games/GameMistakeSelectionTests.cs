using RookHub.Api.DTOs.Games;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>
/// Die Fehler-Auswahl am Server (0.908.0) — der Spiegel von <c>collectMistakes</c>. Dieselbe kleine Partie mit LITERALEN
/// Werten prüft <c>mistakes.util.spec.ts</c> („server mirror"): wer die Regeln auf einer Seite ändert, sieht die andere rot.
/// </summary>
public class GameMistakeSelectionTests
{
    // 1.e4 e5 2.Qh5 Nc6 3.Bc4 Nf6?? — Stellungen wie chess.js sie schreibt.
    internal static readonly string[] Fens =
    [
        "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
        "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1",
        "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
        "rnbqkbnr/pppp1ppp/8/4p2Q/4P3/8/PPPP1PPP/RNB1KBNR b KQkq - 1 2",
        "r1bqkbnr/pppp1ppp/2n5/4p2Q/4P3/8/PPPP1PPP/RNB1KBNR w KQkq - 2 3",
        "r1bqkbnr/pppp1ppp/2n5/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR b KQkq - 3 3",
        "r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4",
    ];
    internal static readonly string[] Ucis = ["e2e4", "e7e5", "d1h5", "b8c6", "f1c4", "g8f6"];

    private static GameEvalPlyDto Row(int ply, int? cp, int? mate, string best) =>
        new() { Ply = ply, Cp = cp, Mate = mate, Depth = 20, BestUci = best, PlayedUci = Ucis[ply] };

    /// <summary>Weiß-Sicht. 0/1/3: Bestzug bzw. kein Verlust; 2: 2.Qh5 Ungenauigkeit (+0,30 → −0,60); 4: 3.Bc4 Fehler, aber
    /// der „Bestzug" e1e3 geht nicht; 5: 3…Nf6 grober Fehler (−3,00 → Weiß setzt matt).</summary>
    internal static List<GameEvalPlyDto> Plies() =>
    [
        Row(0, 30, null, "e2e4"),
        Row(1, 30, null, "d7d5"),
        Row(2, 30, null, "g1f3"),
        Row(3, -60, null, "b8c6"),
        Row(4, -60, null, "e1e3"),
        Row(5, -300, null, "g7g6"),
    ];

    [Fact]
    public void Select_SmallGame_SameAsClient()
    {
        var picked = GameMistakeSelection.Select(Plies(), new GameEvalScoreDto { Mate = 1 }, Fens, Ucis, 6);

        Assert.Equal([2, 5], picked.Select(m => m.Ply));
        Assert.Equal(["d1h5", "g8f6"], picked.Select(m => m.PlayedUci));
        Assert.Equal(["g1f3", "g7g6"], picked.Select(m => m.BestUci));
        Assert.Equal(Fens[2], picked[0].FenBefore);
    }

    [Fact]
    public void Select_BestEqualsPlayed_OrMissingBest_IsSkipped()
    {
        var plies = Plies();
        plies[2].BestUci = "D1H5";   // gleich dem Partiezug (Groß/klein egal)
        plies[5].BestUci = null;
        Assert.Empty(GameMistakeSelection.Select(plies, new GameEvalScoreDto { Mate = 1 }, Fens, Ucis, 6));
    }

    [Fact]
    public void Select_WithoutEvaluations_IsEmpty()
        => Assert.Empty(GameMistakeSelection.Select([], null, Fens, Ucis, 6));
}
