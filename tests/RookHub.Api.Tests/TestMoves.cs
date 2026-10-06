using Chess;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Ein Zug, der in der Stellung geht — die Endstellung einer Analyse (0.689.0) hat keinen Partiezug, ein
/// gefälschtes Ergebnis braucht trotzdem einen legalen.</summary>
internal static class TestMoves
{
    public static string MoveOf(GameAnalysisPosition p) =>
        p.GameMoveUci != "" ? p.GameMoveUci : GamePlies.ToUci(ChessBoard.LoadFromFen(p.Fen).Moves()[0]);
}
