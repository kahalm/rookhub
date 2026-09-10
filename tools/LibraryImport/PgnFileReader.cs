using System.Text;

namespace RookHub.Tools.LibraryImport;

/// <summary>
/// Zerlegt eine PGN-SAMMLUNG im Strom in einzelne Partien.
///
/// <para><b>Warum nicht der Parser der API:</b> <c>PgnParser.SplitGames</c> nimmt die ganze Datei
/// als Zeichenkette. Bei 338 MB waeren das ueber 600 MB im Speicher, nur um sie danach in 130 000
/// Stuecke zu schneiden. Hier laeuft der Leser zeilenweise durch und gibt jede Partie ab, sobald
/// sie vollstaendig ist — der Speicherbedarf haengt an der laengsten EINZELNEN Partie.</para>
///
/// <para>Die Grenze zwischen zwei Partien ist die erste Kopfzeile NACH Zugtext. Eine Leerzeile
/// taugt dafuer nicht: zwischen Kopf und Zugtext derselben Partie steht auch eine.</para>
/// </summary>
public static class PgnFileReader
{
    public sealed record Game(IReadOnlyDictionary<string, string> Headers, string MoveText, string Pgn);

    public static IEnumerable<Game> Read(string path)
    {
        // Die Datei traegt ein BOM und CRLF; beides raeumt der StreamReader bzw. das Trimmen weg.
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        var moves = new StringBuilder();
        var raw = new StringBuilder();
        var sawMoves = false;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            var isHeader = trimmed.StartsWith('[') && trimmed.EndsWith(']');

            if (isHeader && sawMoves)
            {
                // Die naechste Partie faengt an — die aktuelle ist fertig.
                if (headers.Count > 0 || moves.Length > 0)
                    yield return new Game(headers, moves.ToString(), raw.ToString().TrimEnd());
                headers = new Dictionary<string, string>(StringComparer.Ordinal);
                moves.Clear();
                raw.Clear();
                sawMoves = false;
            }

            raw.Append(line).Append('\n');

            if (isHeader)
            {
                var (key, value) = SplitTag(trimmed);
                if (key is not null) headers[key] = value!;
                continue;
            }

            if (trimmed.Length == 0) continue;

            sawMoves = true;
            moves.Append(trimmed).Append(' ');
        }

        if (headers.Count > 0 || moves.Length > 0)
            yield return new Game(headers, moves.ToString(), raw.ToString().TrimEnd());
    }

    /// <summary>„[White \"Carlsen, Magnus\"]" → („White", „Carlsen, Magnus"). Anfuehrungszeichen im
    /// Wert kommen vor (Turniernamen); genommen wird deshalb vom ersten bis zum LETZTEN.</summary>
    private static (string? Key, string? Value) SplitTag(string line)
    {
        var inner = line[1..^1];
        var space = inner.IndexOf(' ');
        if (space <= 0) return (null, null);

        var key = inner[..space];
        var rest = inner[(space + 1)..].Trim();
        var first = rest.IndexOf('"');
        var last = rest.LastIndexOf('"');
        var value = first >= 0 && last > first ? rest[(first + 1)..last] : rest;
        return (key, value);
    }
}
