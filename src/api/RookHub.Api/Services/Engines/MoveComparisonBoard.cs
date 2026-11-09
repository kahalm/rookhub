using Chess;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.Engines;

/// <summary>Brett-Werkzeuge des Zugvergleichs (aus <see cref="MoveComparisonService"/> herausgezogen, 2026-11-09): legaler Zug
/// zu einer UCI-Angabe, Stellung danach, SAN, Figur auf einem Feld und ob eine Antwort nach dem besten Zug derselbe Zug ist.</summary>
internal static class MoveComparisonBoard
{
    /// <summary>
    /// Ist die Antwort auf den schwächeren Zug nach dem besten noch DERSELBE Zug (0.908.0, Befund an Vereinspartie 236: nach
    /// 11...c5 stand der Test „Sxd7" mit +6,52 da — der Springer zog nach dem besten Zug einfach gratis nach d7, nichts wurde
    /// geschlagen)? Nein, wenn auf dem Startfeld eine andere (oder keine) Figur steht, oder wenn die Antwort nach dem schwächeren
    /// Zug etwas schlug und hier nichts schlägt. Umgekehrt (still dort, schlägt hier) bleibt sie: genau das kann der Grund sein.
    /// </summary>
    internal static bool ReplyTransfers(string weakFen, string bestFen, string uci)
    {
        if (string.IsNullOrEmpty(uci) || uci.Length < 4) return false;
        var from = uci[..2];
        var to = uci.Substring(2, 2);
        var piece = PieceAt(weakFen, from);
        if (piece is null || piece != PieceAt(bestFen, from)) return false;
        bool Captures(string fen)
        {
            var target = PieceAt(fen, to);
            if (target is { } t) return char.IsUpper(t) != char.IsUpper(piece.Value);   // eigene Figur = Rochade (König schlägt Turm)
            return char.ToLowerInvariant(piece.Value) == 'p' && from[0] != to[0];      // en passant
        }
        return !Captures(weakFen) || Captures(bestFen);
    }

    /// <summary>Figur auf einem Feld („e4") laut FEN, <c>null</c> = leer oder unlesbar.</summary>
    internal static char? PieceAt(string fen, string square)
    {
        if (string.IsNullOrEmpty(fen) || square.Length != 2) return null;
        var file = square[0] - 'a';
        var rank = square[1] - '1';
        if (file is < 0 or > 7 || rank is < 0 or > 7) return null;
        var rows = fen.Split(' ')[0].Split('/');
        if (rows.Length != 8) return null;
        var col = 0;
        foreach (var ch in rows[7 - rank])
        {
            if (char.IsDigit(ch)) { col += ch - '0'; continue; }
            if (col == file) return ch;
            col++;
            if (col > file) return null;
        }
        return null;
    }

    /// <summary>Ein legaler Zug zu einer UCI-Angabe — auch als König-schlägt-Turm (so schreibt der Broker Rochaden).</summary>
    internal static Move? FindMove(Move[] legal, string? raw)
    {
        var uci = (raw ?? "").Trim().ToLowerInvariant();
        if (uci.Length is < 4 or > 5) return null;
        var move = Array.Find(legal, m => GamePlies.ToUci(m) == uci);
        if (move != null) return move;
        var castle = uci switch { "e1h1" => "e1g1", "e1a1" => "e1c1", "e8h8" => "e8g8", "e8a8" => "e8c8", _ => null };
        return castle is null ? null : Array.Find(legal, m => GamePlies.ToUci(m) == castle && m.Parameter?.ShortStr is "O-O" or "O-O-O");
    }

    /// <summary>Die Stellung nach dem Zug, <c>null</c>, wenn er dort nicht geht.</summary>
    internal static string? PlayUci(string fen, string uci)
    {
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            var move = FindMove(board.Moves(), uci);
            if (move is null) return null;
            board.Move(move);
            return board.ToFen();
        }
        catch { return null; }
    }

    internal static string? AfterMove(string fen, string san)
    {
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            var move = Array.Find(board.Moves(generateSan: true), m => m.San == san);
            if (move is null) return null;
            board.Move(move);
            return board.ToFen();
        }
        catch { return null; }
    }

    internal static string? SanOf(string fen, string uci)
    {
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            var legal = board.Moves(generateSan: true);
            return FindMove(legal, uci)?.San;
        }
        catch { return null; }
    }
}
