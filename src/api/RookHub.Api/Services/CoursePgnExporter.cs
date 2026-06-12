using System.Text;
using Chess;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Baut aus den gespeicherten <see cref="BookPuzzle"/> (FEN + UCI-Hauptlinie) ein PGN — ein Spiel
/// je Puzzle/Linie. UCI→SAN über die Schach-Lib (legale Züge je Halbzug abgleichen). Puzzles, die
/// sich nicht spielen lassen, werden übersprungen (Export bleibt robust).
/// </summary>
public static class CoursePgnExporter
{
    public static string ToPgn(string bookName, IReadOnlyList<BookPuzzle> puzzles)
    {
        var sb = new StringBuilder();
        foreach (var p in puzzles)
        {
            var game = TryBuildGame(bookName, p);
            if (game is not null) sb.Append(game).Append("\n\n");
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    private static string? TryBuildGame(string bookName, BookPuzzle p)
    {
        if (string.IsNullOrWhiteSpace(p.Fen)) return null;
        try
        {
            var board = ChessBoard.LoadFromFen(p.Fen);
            var sans = new List<string>();
            foreach (var uci in p.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var mv = Array.Find(board.Moves(generateSan: true), m => ToUci(m) == uci);
                if (mv is null) break; // UCI passt zu keinem legalen Zug → hier abbrechen
                sans.Add(string.IsNullOrEmpty(mv.San) ? uci : mv.San);
                board.Move(mv);
            }

            var sb = new StringBuilder();
            sb.Append($"[Event \"{Escape(bookName)}\"]\n");
            sb.Append("[Site \"RookHub\"]\n");
            if (!string.IsNullOrWhiteSpace(p.Title)) sb.Append($"[White \"{Escape(p.Title!)}\"]\n");
            if (!string.IsNullOrWhiteSpace(p.Chapter)) sb.Append($"[Black \"{Escape(p.Chapter!)}\"]\n");
            if (!string.IsNullOrWhiteSpace(p.Round)) sb.Append($"[Round \"{Escape(p.Round)}\"]\n");
            sb.Append($"[FEN \"{p.Fen}\"]\n");
            sb.Append("[SetUp \"1\"]\n\n");
            sb.Append(MoveText(p.Fen, sans)).Append(" *");
            return sb.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string MoveText(string fen, List<string> sans)
    {
        var parts = fen.Split(' ');
        bool white = parts.Length < 2 || parts[1] != "b";
        int no = parts.Length >= 6 && int.TryParse(parts[5], out var fm) && fm > 0 ? fm : 1;
        var sb = new StringBuilder();
        bool first = true;
        foreach (var san in sans)
        {
            if (white) sb.Append($"{no}. {san} ");
            else { sb.Append(first ? $"{no}... {san} " : $"{san} "); no++; }
            white = !white;
            first = false;
        }
        return sb.ToString().TrimEnd();
    }

    private static string ToUci(Move m)
    {
        var u = m.OriginalPosition.ToString() + m.NewPosition.ToString();
        var ss = m.Parameter?.ShortStr;
        if (!string.IsNullOrEmpty(ss) && ss.StartsWith('=') && ss.Length >= 2)
            u += char.ToLowerInvariant(ss[1]);
        return u;
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
