namespace RookHub.Api.Services;

/// <summary>
/// Felder einer FEN, die mehrere Dienste lesen. Stand bis 0.624.0 als private Kopie in
/// <see cref="GameAccuracy"/>, <see cref="GameMistakes"/>, <see cref="GameRecapService"/> und
/// <see cref="MoveComparisonService"/> (Codereview 2026-09-29, N11-006).
/// </summary>
public static class FenFields
{
    /// <summary>
    /// Ist Weiß am Zug? Nur ein Zugfeld „b" heißt Schwarz — fehlt das Feld, gilt Weiß (wie
    /// <c>whiteToMove</c> im Client, <c>game-review.util.ts</c>).
    ///
    /// <para>Bewusst NICHT hierher gezogen: <c>GameEvals.WhiteToMove</c> nimmt bei fehlendem Feld
    /// SCHWARZ an und ist mit <c>BrokerCandidates</c> gekoppelt (siehe dort) — beide nur gemeinsam
    /// umstellen, sonst dreht sich eine Bewertung doppelt. Unschädlich, solange jede FEN vorher durch
    /// <c>ChessBoard.LoadFromFen</c> geht (das Muster verlangt das Feld).</para>
    /// </summary>
    public static bool WhiteToMove(string fen) => !(fen.Split(' ') is { Length: >= 2 } parts && parts[1] == "b");
}
