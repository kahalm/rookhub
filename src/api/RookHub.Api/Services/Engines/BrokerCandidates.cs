using System.Text.Json;
using System.Text.Json.Nodes;
using Chess;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Library;

namespace RookHub.Api.Services.Engines;

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
    /// <summary>
    /// Ein Kandidat: Zug + Bewertung (genau eines von <c>cp</c>/<c>mate</c>) und seit 0.521.0 die
    /// VARIANTE dazu (<see cref="Pv"/>) — für „Computer-Linien" auf der Partieseite.
    /// </summary>
    /// <param name="Pv">Die Hauptvariante der Engine, ROH wie vom Broker (UCI, Rochade ggf. als
    /// König-schlägt-Turm), beginnend mit dem Kandidatenzug, höchstens <see cref="MaxPvPlies"/> Halbzüge.
    /// Nicht umgeschrieben: dafür müsste man jeden Zug nachspielen — das tut der Client beim Umrechnen
    /// in SAN ohnehin (<c>uciLineToSan</c>). <c>null</c> bei Zeilen von vor 0.521.0.</param>
    public readonly record struct Candidate(string Uci, int? Cp, int? Mate, IReadOnlyList<string>? Pv = null);

    /// <summary>So viele Halbzüge einer Variante werden abgelegt. Die Engine liefert bei Tiefe 30 oft
    /// 20 und mehr; lesen will man auf der Partieseite ein paar Züge, und fünf Kandidaten je Stellung
    /// mal 80 Stellungen sollen die Zeile nicht aufblähen.</summary>
    public const int MaxPvPlies = 16;

    /// <summary>
    /// Liest die <c>pvs</c> einer Broker-Zeile. <c>null</c>, wenn die Zeile unbrauchbar ist
    /// (kein JSON, keine pvs, keine Stellung) — oder wenn sie nicht zu <paramref name="fen"/> gehört
    /// (<see cref="MismatchOf(JsonElement, Move[])"/>): seit dem Provider-Versatz vom 16./17.11.2026 wird eine Zeile mit
    /// EINER fremden Linie oder fremdem <c>bestmove</c> ganz verworfen statt gefiltert — blieb früher zufällig ein legaler
    /// Zug übrig, landete die Bewertung einer anderen Stellung still in der Analyse.
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
        // Nur die UCI-Form wird verglichen — SAN zu erzeugen kostet hier je Stellung Arbeit fuer nichts.
        var legal = board.Moves(generateSan: false);

        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("pvs", out var pvs) || pvs.ValueKind != JsonValueKind.Array)
                return null;
            if (MismatchOf(doc.RootElement, legal) is not null) return null;   // gehört zu einer anderen Stellung

            var list = new List<Candidate>();
            foreach (var pv in pvs.EnumerateArray())
            {
                if (pv.ValueKind != JsonValueKind.Object || !pv.TryGetProperty("moves", out var moves)) continue;
                var first = FirstMove(moves);
                if (first is null) continue;
                var uci = NormalizeUci(legal, first);
                if (uci is null) continue;   // nach MismatchOf nicht mehr möglich — Riegel bleibt

                int? cp = pv.TryGetProperty("cp", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
                int? mate = pv.TryGetProperty("mate", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : null;
                if (cp is null && mate is null) continue;

                if (!whiteToMove)
                {
                    cp = cp is int cv ? -cv : null;
                    mate = mate is int mv ? -mv : null;
                }
                if (list.All(x => !string.Equals(x.Uci, uci, StringComparison.OrdinalIgnoreCase)))
                    list.Add(new Candidate(uci, cp, mate, AllMoves(moves)));
            }
            return list.Count == 0 ? null : list;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Gehört die Broker-Zeile NICHT zu dieser Stellung? Dann der Grund, sonst <c>null</c>. Sichere Merkmale (eine Engine
    /// liefert für ihre eigene Stellung nie einen illegalen Wurzelzug):
    /// <list type="bullet">
    /// <item>Eine Linie, deren erster Zug in der Stellung illegal ist — auch die LEERE Linie: der Broker (eigener wie
    /// lila-engine) spielt jede Variante auf der Stellung nach und schneidet sie beim ersten illegalen Zug ab, aus einer
    /// fremden Linie wird so <c>"moves":[]</c>. Eine Linie ohne <c>pv</c> (Matt/Patt: <c>info depth 0 score mate 0</c>)
    /// wird gar nicht erst gesendet.</item>
    /// <item>Ein <c>bestmove</c>, der in der Stellung illegal ist. Fehlt er (<c>bestmove (none)</c> bei Matt/Patt, oder die
    /// Suche läuft noch), ist das KEIN Merkmal.</item>
    /// </list>
    /// Rochade gilt in beiden Schreibweisen (<c>e1g1</c> und König-schlägt-Turm <c>e1h1</c>, der Provider setzt
    /// <c>UCI_Chess960 true</c>). Die ganze Variante zu prüfen bringt hier nichts: der Broker hat sie schon auf den
    /// legalen Anfang gekürzt.
    /// </summary>
    public static string? MismatchOf(string? line, string fen)
    {
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(fen)) return null;
        Move[] legal;
        try { legal = ChessBoard.LoadFromFen(fen).Moves(generateSan: false); }
        catch { return null; }
        return MismatchOf(line, legal);
    }

    /// <inheritdoc cref="MismatchOf(string?, string)"/>
    /// <param name="legal">Die legalen Züge der Stellung (einmal je Stellung erzeugt — der Worker prüft jede Zeile).</param>
    internal static string? MismatchOf(string line, Move[] legal)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? MismatchOf(doc.RootElement, legal) : null;
        }
        catch (JsonException) { return null; }
    }

    internal static string? MismatchOf(JsonElement root, Move[] legal)
    {
        if (root.TryGetProperty("pvs", out var pvs) && pvs.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var pv in pvs.EnumerateArray())
            {
                index++;
                if (pv.ValueKind != JsonValueKind.Object || !pv.TryGetProperty("moves", out var moves)) continue;
                if (moves.ValueKind is not (JsonValueKind.Array or JsonValueKind.String)) continue;
                var first = FirstMove(moves);
                if (string.IsNullOrWhiteSpace(first)) return $"Linie {index} ohne legalen ersten Zug";
                if (NormalizeUci(legal, first) is null) return $"Linie {index}: {first} ist hier illegal";
            }
        }
        if (root.TryGetProperty("bestmove", out var b) && b.ValueKind == JsonValueKind.String
            && b.GetString() is { Length: > 0 } best && best != "(none)" && NormalizeUci(legal, best) is null)
            return $"bestmove {best} ist hier illegal";
        return null;
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
            if (c.Pv is { Count: > 0 } pv) o["pv"] = new JsonArray(pv.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
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

    /// <summary>
    /// Die abgelegten Varianten je Kandidatenzug (UCI in Kleinbuchstaben → Züge). Leer bei Zeilen von vor
    /// 0.521.0 oder unbrauchbarem JSON — die Wertung (<see cref="FromJson"/>) braucht sie nicht, die
    /// „Computer-Linien" der Partieseite schon.
    /// </summary>
    public static Dictionary<string, List<string>> PvsFromJson(string? json)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var uci = e.TryGetProperty("uci", out var u) ? u.GetString() : null;
                if (string.IsNullOrWhiteSpace(uci) || !e.TryGetProperty("pv", out var pv)) continue;
                var moves = AllMoves(pv);
                if (moves is { Count: > 0 }) result.TryAdd(uci, moves);
            }
        }
        catch (JsonException) { /* unbrauchbare Zeile → keine Varianten */ }
        return result;
    }

    /// <summary>Bewertung der Hauptvariante als Text (<c>+0.34</c>/<c>#3</c>), Sicht der Seite am Zug.</summary>
    public static string? EvalTextOf(IReadOnlyList<Candidate> candidates)
    {
        if (candidates.Count == 0) return null;
        var c = candidates[0];
        if (c.Cp is null && c.Mate is null) return null;
        return new GuessScoring.Eval(c.Cp, c.Mate).Text;
    }

    /// <summary>Alle Züge einer Variante (Array oder Leerzeichen-Text), gekappt auf <see cref="MaxPvPlies"/>.</summary>
    private static List<string>? AllMoves(JsonElement moves)
    {
        IEnumerable<string?> raw = moves.ValueKind switch
        {
            JsonValueKind.Array => moves.EnumerateArray().Select(m => m.ValueKind == JsonValueKind.String ? m.GetString() : null),
            JsonValueKind.String => (moves.GetString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries),
            _ => Array.Empty<string?>(),
        };
        var list = raw.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!.Trim()).Take(MaxPvPlies).ToList();
        return list.Count == 0 ? null : list;
    }

    private static string? FirstMove(JsonElement moves)
    {
        // Der Broker schickt die Variante als Array; ältere Zeilen auch als Leerzeichen-Text.
        if (moves.ValueKind == JsonValueKind.Array)
            return moves.GetArrayLength() > 0 && moves[0].ValueKind == JsonValueKind.String ? moves[0].GetString() : null;
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
            var uci = GamePlies.ToUci(m);
            if (uci == wanted) return uci;
            // Rochade: Königszug auf das Turmfeld (e1h1 / e1a1).
            if (m.Parameter?.ShortStr is "O-O" or "O-O-O")
            {
                var kingFrom = m.OriginalPosition.ToString();
                var rookFile = m.Parameter.ShortStr == "O-O" ? "h" : "a";
                var rank = kingFrom[1];
                if (wanted == kingFrom + rookFile + rank) return uci;
            }
        }
        return null;
    }
}
