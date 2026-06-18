using System.Text;
using System.Text.Json;
using Chess;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Baut aus den gespeicherten <see cref="BookPuzzle"/> (FEN + UCI-Hauptlinie) ein PGN — ein Spiel
/// je Puzzle/Linie. UCI→SAN über die Schach-Lib (legale Züge je Halbzug abgleichen). Die
/// Pro-Zug-Kommentare (<see cref="BookPuzzle.MoveComments"/>) werden als PGN-Kommentare
/// <c>{…}</c> hinter den jeweiligen Halbzug eingebettet (Schlüssel <c>-1</c> = Einleitung).
/// Puzzles, die sich nicht spielen lassen, werden übersprungen (Export bleibt robust).
/// <para>Hinweis: Dieser Rekonstruktions-Export kennt KEINE Varianten (sie liegen nicht in der DB).
/// Solange das Buch ein <see cref="Book.SourcePgn"/> hat, wird ohnehin dieses Roh-PGN ausgeliefert;
/// dieser Exporter greift nur beim quellenlosen Altbestand.</para>
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

            var comments = ParseMoveComments(p.MoveComments);

            var sb = new StringBuilder();
            sb.Append($"[Event \"{Escape(bookName)}\"]\n");
            sb.Append("[Site \"RookHub\"]\n");
            if (!string.IsNullOrWhiteSpace(p.Title)) sb.Append($"[White \"{Escape(p.Title!)}\"]\n");
            if (!string.IsNullOrWhiteSpace(p.Chapter)) sb.Append($"[Black \"{Escape(p.Chapter!)}\"]\n");
            if (!string.IsNullOrWhiteSpace(p.Round)) sb.Append($"[Round \"{Escape(p.Round)}\"]\n");
            sb.Append($"[FEN \"{p.Fen}\"]\n");
            sb.Append("[SetUp \"1\"]\n\n");
            sb.Append(MoveText(p.Fen, sans, comments, p.Comment)).Append(" *");
            return sb.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>MoveComments-JSON (<c>{ "plyIndex": "text" }</c>) → Dictionary; tolerant bei leer/kaputt.</summary>
    private static Dictionary<int, string> ParseMoveComments(string? json)
    {
        var map = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(json)) return map;
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (raw is null) return map;
            foreach (var kv in raw)
                if (int.TryParse(kv.Key, out var ply) && !string.IsNullOrWhiteSpace(kv.Value))
                    map[ply] = kv.Value;
        }
        catch { /* defekte Kommentare ignorieren, Export bleibt robust */ }
        return map;
    }

    private static string MoveText(string fen, List<string> sans, Dictionary<int, string> comments, string? lineComment)
    {
        var parts = fen.Split(' ');
        bool white = parts.Length < 2 || parts[1] != "b";
        int no = parts.Length >= 6 && int.TryParse(parts[5], out var fm) && fm > 0 ? fm : 1;
        var sb = new StringBuilder();
        bool first = true;

        // Einleitungskommentar (Ply -1), sonst der allgemeine Linien-Kommentar als Vorspann.
        var intro = comments.TryGetValue(-1, out var c0) ? c0 : lineComment;
        if (!string.IsNullOrWhiteSpace(intro)) sb.Append($"{{{CleanComment(intro)}}} ");

        for (int i = 0; i < sans.Count; i++)
        {
            var san = sans[i];
            if (white) sb.Append($"{no}. {san} ");
            else { sb.Append(first ? $"{no}... {san} " : $"{san} "); no++; }
            if (comments.TryGetValue(i, out var cm) && !string.IsNullOrWhiteSpace(cm))
                sb.Append($"{{{CleanComment(cm)}}} ");
            white = !white;
            first = false;
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Macht Text PGN-kommentartauglich: schließende Klammer ersetzen, Whitespace glätten.</summary>
    private static string CleanComment(string s)
        => string.Join(' ', s.Replace('}', ')').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
