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
            // Ohne eigenen Einleitungs-Kommentar tritt der allgemeine Linien-Kommentar an seine
            // Stelle — der Writer liest beides unter dem Schlüssel -1.
            if (!comments.ContainsKey(-1) && !string.IsNullOrWhiteSpace(p.Comment))
                comments[-1] = p.Comment!;

            var sb = new StringBuilder();
            sb.Append(PgnWriter.Tag("Event", bookName));
            sb.Append(PgnWriter.Tag("Site", "RookHub"));
            if (!string.IsNullOrWhiteSpace(p.Title)) sb.Append(PgnWriter.Tag("White", p.Title));
            if (!string.IsNullOrWhiteSpace(p.Chapter)) sb.Append(PgnWriter.Tag("Black", p.Chapter));
            if (!string.IsNullOrWhiteSpace(p.Round)) sb.Append(PgnWriter.Tag("Round", p.Round));
            sb.Append(PgnWriter.Tag("FEN", p.Fen));
            sb.Append(PgnWriter.Tag("SetUp", "1"));
            // Chessable-Verknüpfung erhalten: die oid ist der Schlüssel, über den die Extension eine
            // trainierte Linie ihrem RookHub-Gegenstück zuordnet (POST …/chessable/line-trained).
            // Ohne diesen Header verlor ein aus dem Kurs erzeugtes Repertoire die Verbindung.
            if (!string.IsNullOrWhiteSpace(p.ChessableOid))
                sb.Append(PgnWriter.Tag("ChessableOid", p.ChessableOid));
            sb.Append('\n');
            // Das Ergebnis hängt DIESER Aufrufer an, mit einem Leerzeichen davor — auch an eine
            // zug- und kommentarlose Info-Linie, deren Zugtext sonst leer wäre (Bestand: " *").
            sb.Append(PgnWriter.MoveText(sans, p.Fen, comments, result: null, before: TrainingMarkers(sans, p.StartPly, p.Moves)))
              .Append(" *");
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

    /// <summary>
    /// Der <c>[%tqu]</c>-Marker als „Text vor dem Halbzug" für <see cref="PgnWriter.MoveText"/>.
    /// <para>Er gehört hinter den letzten VORGESPIELTEN Zug, also unmittelbar vor den ersten Zug
    /// des Lösers — genau so zählt ihn <see cref="PgnParser.FindTquMoveIndex"/> zurück. Ohne ihn
    /// gilt beim nächsten Import jede Linie als „ab der FEN lösen", und bei Chessable-Partien
    /// stünde die falsche Seite am Zug.</para>
    /// </summary>
    /// <param name="startPly">Index des letzten vorgespielten Halbzugs; <c>-1</c> = ab dem ersten
    /// Zug lösen, dann gibt es keinen Marker.</param>
    /// <param name="movesUci">Die UCI-Hauptlinie (für die uci-Angabe im Marker).</param>
    private static Dictionary<int, string>? TrainingMarkers(List<string> sans, int startPly, string? movesUci)
    {
        var at = startPly + 1;
        if (startPly < 0 || at >= sans.Count) return null;
        var ucis = (movesUci ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var uci = at < ucis.Length ? ucis[at] : "";
        return new Dictionary<int, string> { [at] = $"{{[%tqu \"En\",\"find the move\",\"\",\"\",\"{uci}\",\"\",10]}}" };
    }

    /// <summary>Zug → UCI in der Schreibweise, die im ganzen Server gilt
    /// (<see cref="PgnParser.ToUci"/>) — sie ist genau dafür öffentlich. Eine eigene Fassung wäre
    /// die erste Stelle, an der die beiden auseinanderlaufen, und sie steckt im EXPORT: ein PGN mit
    /// anders geschriebener Rochade/Umwandlung liest der eigene Import falsch wieder ein.</summary>
    private static string ToUci(Move m) => PgnParser.ToUci(m);
}
