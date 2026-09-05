using System.Text.Json;
using System.Text.Json.Nodes;
using Chess;

namespace RookHub.Api.Services;

/// <summary>
/// Broker-Ergebniszeile → Kandidatenliste, wie die Wertung sie braucht
/// (<c>[{"uci":"e2e4","cp":30}]</c>, Bewertung <b>aus Sicht der Seite am Zug</b>).
///
/// <para>Zwei Eigenheiten des Lichess-Brokers, die hier begradigt werden — beide sind im Frontend
/// schon einmal gelöst (<c>mapRemoteLine</c>, <c>castling-uci.util.ts</c>), gelten aber genauso
/// serverseitig:</para>
/// <list type="number">
/// <item><b>Bewertung aus WEISS-Sicht.</b> Für die Wertung eines schwarzen Zuges muss das Vorzeichen
/// gedreht werden — sonst bekäme Schwarz Punkte für Weiß' Vorteil.</item>
/// <item><b>Rochade als König-schlägt-Turm</b> (<c>e1h1</c>, lila-engine nutzt Chess960-Notation).
/// Der Partiezug steht als <c>e1g1</c> da; ohne Umschreiben fänden sich die beiden nie.</item>
/// </list>
/// </summary>
public static class BrokerCandidates
{
    /// <summary>Ein Kandidat: Zug + Bewertung (genau eines von <c>cp</c>/<c>mate</c>).</summary>
    public readonly record struct Candidate(string Uci, int? Cp, int? Mate);

    /// <summary>
    /// Liest die <c>pvs</c> einer Broker-Zeile. <c>null</c>, wenn die Zeile unbrauchbar ist
    /// (kein JSON, keine pvs, keine Stellung).
    /// </summary>
    /// <param name="fen">Stellung, zu der die Zeile gehört — bestimmt Seite am Zug und Rochade-Form.</param>
    public static List<Candidate>? Parse(string? resultJson, string fen)
    {
        if (string.IsNullOrWhiteSpace(resultJson) || string.IsNullOrWhiteSpace(fen)) return null;

        ChessBoard board;
        try { board = ChessBoard.LoadFromFen(fen); }
        catch { return null; }

        // Weiß am Zug? Dann bleibt die Broker-Bewertung, sonst wird sie gedreht.
        var whiteToMove = fen.Split(' ') is { Length: >= 2 } parts && parts[1] == "w";
        var legal = board.Moves(generateSan: true);

        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            if (!doc.RootElement.TryGetProperty("pvs", out var pvs) || pvs.ValueKind != JsonValueKind.Array)
                return null;

            var list = new List<Candidate>();
            foreach (var pv in pvs.EnumerateArray())
            {
                if (!pv.TryGetProperty("moves", out var moves)) continue;
                var first = FirstMove(moves);
                if (first is null) continue;
                var uci = NormalizeUci(legal, first);
                if (uci is null) continue;   // Zug passt zu keinem legalen Zug → Zeile überspringen

                int? cp = pv.TryGetProperty("cp", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
                int? mate = pv.TryGetProperty("mate", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : null;
                if (cp is null && mate is null) continue;

                if (!whiteToMove)
                {
                    cp = cp is int cv ? -cv : null;
                    mate = mate is int mv ? -mv : null;
                }
                if (list.All(x => !string.Equals(x.Uci, uci, StringComparison.OrdinalIgnoreCase)))
                    list.Add(new Candidate(uci, cp, mate));
            }
            return list.Count == 0 ? null : list;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Serialisiert die Kandidaten so, wie sie in <c>GameAnalysisPosition.CandidatesJson</c> landen.</summary>
    public static string ToJson(IEnumerable<Candidate> candidates)
    {
        var arr = new JsonArray();
        foreach (var c in candidates)
        {
            var o = new JsonObject { ["uci"] = c.Uci };
            if (c.Cp is int cp) o["cp"] = cp;
            if (c.Mate is int m) o["mate"] = m;
            arr.Add(o);
        }
        return arr.ToJsonString();
    }

    /// <summary>Gegenstück zu <see cref="ToJson"/> — für die Wertung.</summary>
    public static List<GuessScoring.Candidate> FromJson(string? json)
    {
        var result = new List<GuessScoring.Candidate>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var uci = e.TryGetProperty("uci", out var u) ? u.GetString() : null;
                if (string.IsNullOrWhiteSpace(uci)) continue;
                if (e.TryGetProperty("mate", out var m) && m.ValueKind == JsonValueKind.Number)
                    result.Add(new GuessScoring.Candidate(uci, GuessScoring.Eval.FromMate(m.GetInt32())));
                else if (e.TryGetProperty("cp", out var c) && c.ValueKind == JsonValueKind.Number)
                    result.Add(new GuessScoring.Candidate(uci, GuessScoring.Eval.FromCp(c.GetInt32())));
            }
        }
        catch (JsonException) { /* unbrauchbare Zeile → leere Liste, die Stellung gilt als nicht wertbar */ }
        return result;
    }

    /// <summary>Bewertung der Hauptvariante als Text (<c>+0.34</c>/<c>#3</c>), Sicht der Seite am Zug.</summary>
    public static string? EvalTextOf(IReadOnlyList<Candidate> candidates)
    {
        if (candidates.Count == 0) return null;
        var c = candidates[0];
        if (c.Mate is int m) return "#" + m;
        if (c.Cp is int cp)
        {
            var v = cp / 100.0;
            return (v > 0 ? "+" : "") + v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }
        return null;
    }

    private static string? FirstMove(JsonElement moves)
    {
        // Der Broker schickt die Variante als Array; ältere Zeilen auch als Leerzeichen-Text.
        if (moves.ValueKind == JsonValueKind.Array)
            return moves.GetArrayLength() > 0 ? moves[0].GetString() : null;
        if (moves.ValueKind == JsonValueKind.String)
            return moves.GetString()?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return null;
    }

    /// <summary>Broker-UCI auf die Standardform bringen: Rochade kommt als König-schlägt-Turm herein.
    /// Verglichen wird gegen die LEGALEN Züge der Stellung — <c>e1h1</c> ist ein legaler Turmzug,
    /// wenn der König woanders steht, deshalb wird nicht blind ersetzt.</summary>
    private static string? NormalizeUci(Move[] legal, string brokerUci)
    {
        var wanted = brokerUci.Trim().ToLowerInvariant();
        foreach (var m in legal)
        {
            if (GamePlies.ToUci(m) == wanted) return GamePlies.ToUci(m);
            // Rochade: Königszug auf das Turmfeld (e1h1 / e1a1).
            if (m.Parameter?.ShortStr is "O-O" or "O-O-O")
            {
                var kingFrom = m.OriginalPosition.ToString();
                var rookFile = m.Parameter.ShortStr == "O-O" ? "h" : "a";
                var rank = kingFrom[1];
                if (wanted == kingFrom + rookFile + rank) return GamePlies.ToUci(m);
            }
        }
        return null;
    }
}
