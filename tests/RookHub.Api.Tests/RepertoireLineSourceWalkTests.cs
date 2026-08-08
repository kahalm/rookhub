using RookHub.Api.Services;
using static RookHub.Api.Services.RepertoireLineSource;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Linien-Walk mit FORTSETZUNGEN (Basis des Zug-Treffers der Ähnlichkeitssuche): je besuchter
/// Stellung muss er den Zug melden, mit dem das Repertoire dort weitergeht — Hauptzug UND die an
/// dieser Stelle abzweigenden Varianten, aufgelöst auf from/to (nicht auf die SAN-Zeichenkette).
/// </summary>
public class RepertoireLineSourceWalkTests
{
    private static PgnMove M(string san, params List<PgnMove>[] variations)
        => new(san, variations.ToList());

    /// <summary>Die alte Signatur (nur fen/ply) muss unverändert dieselben Stellungen melden — an ihr
    /// hängt der exakte Stellungs-Index.</summary>
    [Fact]
    public void WalkPositions_WithoutContinuations_StillReportsFenAndPly()
    {
        var moves = new List<PgnMove> { M("e4"), M("e5"), M("Nf3") };
        var seen = new List<(string Fen, int Ply)>();

        RepertoireLineSource.WalkPositions(new Chess.ChessBoard(), moves, 0, (fen, ply) =>
        {
            seen.Add((fen, ply));
            return true;
        });

        Assert.Equal(3, seen.Count);
        Assert.Equal(new[] { 1, 2, 3 }, seen.Select(s => s.Ply));
    }

    /// <summary>An der Stellung nach 1.e4 e5 geht es im Repertoire mit 2.Sf3 weiter, laut Variante
    /// aber auch mit 2.Lc4 — beide Fortsetzungen gehören zu DIESER Stellung, mit from/to.</summary>
    [Fact]
    public void WalkPositions_WithContinuations_ReportsMainlineAndVariationAtThatPosition()
    {
        // 1. e4 e5 2. Nf3 (2. Bc4 Nf6) Nc6
        var moves = new List<PgnMove>
        {
            M("e4"),
            M("e5"),
            M("Nf3", new List<PgnMove> { M("Bc4"), M("Nf6") }),
            M("Nc6"),
        };
        var byPly = new Dictionary<int, IReadOnlyList<LineContinuation>>();

        RepertoireLineSource.WalkPositions(new Chess.ChessBoard(), moves, 0, visit =>
        {
            if (visit.Ply >= 0 && !byPly.ContainsKey(visit.Ply)) byPly[visit.Ply] = visit.Continuations;
            return true;
        }, withContinuations: true);

        var afterE5 = byPly[2];
        Assert.Equal(2, afterE5.Count);
        var mainline = Assert.Single(afterE5, c => c.IsMainline);
        Assert.Equal("Nf3", mainline.San);
        Assert.Equal(PositionSimilarity.SquareFromAlgebraic("g1"), mainline.From);
        Assert.Equal(PositionSimilarity.SquareFromAlgebraic("f3"), mainline.To);
        Assert.Equal(SimPieceType.Knight, mainline.Piece);
        var variation = Assert.Single(afterE5, c => !c.IsMainline);
        Assert.Equal("Bc4", variation.San);
        Assert.Equal(PositionSimilarity.SquareFromAlgebraic("f1"), variation.From);
        Assert.Equal(PositionSimilarity.SquareFromAlgebraic("c4"), variation.To);

        // Letzte Stellung der Linie: keine Fortsetzung mehr.
        Assert.Empty(byPly[4]);
    }

    /// <summary>Disambiguierte SANs (<c>Nbd2</c>) müssen aufgelöst werden — genau daran scheitert ein
    /// Textvergleich. Und die Umwandlungsfigur gehört mit in die Fortsetzung.</summary>
    [Fact]
    public void ContinuationsAt_ResolvesDisambiguatedSanAndPromotion()
    {
        // Beide Springer (b1 und f3) können nach d2 — „Nd2" allein wäre mehrdeutig.
        var board = Chess.ChessBoard.LoadFromFen("r1bq1rk1/2p1bppp/p1np1n2/1p2p3/4P3/1B1P1N2/PPP2PPP/RNBQR1K1 w - - 1 9");
        var knight = Assert.Single(RepertoireLineSource.ContinuationsAt(board, new List<PgnMove> { M("Nbd2") }, 0));
        Assert.Equal(PositionSimilarity.SquareFromAlgebraic("b1"), knight.From);
        Assert.Equal(PositionSimilarity.SquareFromAlgebraic("d2"), knight.To);
        Assert.Null(knight.Promotion);

        var promo = Chess.ChessBoard.LoadFromFen("8/P6k/8/8/8/8/7K/8 w - - 0 1");
        var queen = Assert.Single(RepertoireLineSource.ContinuationsAt(promo, new List<PgnMove> { M("a8=Q") }, 0));
        Assert.Equal(PositionSimilarity.SquareFromAlgebraic("a7"), queen.From);
        Assert.Equal(PositionSimilarity.SquareFromAlgebraic("a8"), queen.To);
        Assert.Equal('q', queen.Promotion);
        Assert.Equal(SimPieceType.Pawn, queen.Piece);
    }

    /// <summary>Ein nicht auflösbares SAN darf die Linie nicht kippen — es fällt still heraus.</summary>
    [Fact]
    public void ContinuationsAt_UnparsableSan_IsDroppedSilently()
        => Assert.Empty(RepertoireLineSource.ContinuationsAt(new Chess.ChessBoard(), new List<PgnMove> { M("Qh9") }, 0));
}
