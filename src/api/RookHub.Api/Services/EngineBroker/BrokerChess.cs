using Chess;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Das bisschen Schachregeln, das der Broker braucht — über Gera.Chess, wie überall in der API.
///
/// <para><b>Rochade als König-schlägt-Turm.</b> Der Provider setzt <c>UCI_Chess960 true</c>; die Engine
/// erwartet und liefert Rochaden dann als <c>e1h1</c>. lila-engine gibt alle Züge mit
/// <c>CastlingMode::Chess960</c> aus, und darauf verlassen sich <c>castling-uci.util.ts</c> im Browser und
/// <c>BrokerCandidates</c>/<c>GameMistakes</c> im Server. Angenommen wird beides (<c>e1g1</c> und
/// <c>e1h1</c>, wie shakmaty <c>UciMove::to_move</c>), ausgegeben immer König-schlägt-Turm.</para>
///
/// <para><b>Schnell genug.</b> Legale Züge werden nur für das AUSGANGSFELD erzeugt
/// (<c>Moves(Position)</c>): gemessen 2 ms für eine 30-Züge-Variante statt 14 ms mit der ganzen Zugliste.</para>
/// </summary>
public static class BrokerChess
{
    public static ChessBoard Load(string fen) => ChessBoard.LoadFromFen(fen);

    public static bool BlackToMove(ChessBoard board) => board.Turn == PieceColor.Black;

    /// <summary>Spielt den (syntaktisch gültigen) UCI-Zug, wenn er in der Stellung legal ist, und liefert
    /// seine Chess960-Schreibweise; <c>null</c> = illegal (das Brett bleibt dann unverändert).</summary>
    public static string? TryPlay(ChessBoard board, string uci)
    {
        if (uci.Length is not (4 or 5) || uci == "0000" || uci[1] == '@') return null;
        var from = uci[..2];
        var to = uci[2..4];
        char? promotion = uci.Length == 5 ? uci[4] : null;

        Piece? piece;
        try { piece = board[from]; }
        catch (Exception) { return null; }
        if (piece is null || piece.Color != board.Turn) return null;

        Move[] moves;
        try { moves = board.Moves(new Position(from), false, false); }
        catch (Exception) { return null; }

        if (piece.Type == PieceType.King && promotion is null)
        {
            foreach (var m in moves)
            {
                var castle = m.Parameter?.ShortStr;
                if (castle is not ("O-O" or "O-O-O")) continue;
                var rookSquare = (castle == "O-O" ? "h" : "a") + from[1];
                if (to != rookSquare && to != m.NewPosition.ToString()) continue;
                return Apply(board, m) ? from + rookSquare : null;
            }
        }

        foreach (var m in moves)
        {
            if (m.NewPosition.ToString() != to) continue;
            var param = m.Parameter?.ShortStr;
            if (param is "O-O" or "O-O-O") continue;
            var isPromotion = param is { Length: >= 2 } && param[0] == '=';
            if (isPromotion != promotion.HasValue) continue;
            if (isPromotion && char.ToLowerInvariant(param![1]) != promotion) continue;
            return Apply(board, m) ? uci : null;
        }
        return null;
    }

    private static bool Apply(ChessBoard board, Move m)
    {
        try { return board.Move(m); }
        catch (Exception) { return false; }
    }
}
