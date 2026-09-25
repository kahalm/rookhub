using System.Text.Json.Nodes;
using Chess;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>Ein Analyse-Auftrag, wie ihn der Provider bekommt (Lichess <c>ExternalEngineWork</c>).
/// GENAU EINES von <see cref="Depth"/>/<see cref="Movetime"/>/<see cref="Nodes"/>.</summary>
public sealed record EngineWork(
    string SessionId,
    int Threads,
    int Hash,
    int MultiPv,
    string InitialFen,
    IReadOnlyList<string> Moves,
    int? Depth = null,
    int? Movetime = null,
    long? Nodes = null)
{
    /// <summary>Das <c>work</c>-Objekt für den Provider — Reihenfolge wie lila-engine <c>api.rs</c>
    /// (der Provider liest nach Namen und nimmt das erste von movetime, depth, nodes).</summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject
        {
            ["sessionId"] = SessionId,
            ["threads"] = Threads,
            ["hash"] = Hash,
        };
        if (Movetime is { } mt) o["movetime"] = mt;
        else if (Depth is { } d) o["depth"] = d;
        else if (Nodes is { } n) o["nodes"] = n;
        o["multiPv"] = MultiPv;
        o["variant"] = "chess";
        o["initialFen"] = InitialFen;
        o["moves"] = new JsonArray([.. Moves.Select(m => (JsonNode?)JsonValue.Create(m))]);
        return o;
    }
}

public sealed class InvalidWorkException(string message) : Exception(message);

/// <summary>Ein geprüfter Auftrag plus die Stellung, auf der die Varianten nachgespielt werden.</summary>
public sealed record SanitizedWork(EngineWork Work, string RootFen);

/// <summary>
/// Port von lila-engine <c>api.rs::Work::sanitize</c>: Threads/Hash auf die Maxima der Engine klemmen,
/// <c>multiPv</c> 1..5, Ausgangsstellung legal, höchstens 600 Züge, jeder legal, Rochaden als
/// König-schlägt-Turm (der Provider setzt <c>UCI_Chess960 true</c> — ein <c>e1g1</c> wäre dort ein
/// Königszug nach g1 und Stockfish verwürfe den Rest der Zugfolge).
///
/// <para><b>Strenger als Gera.Chess, so streng wie shakmaty:</b> Gera lädt auch Stellungen, die lila-engine
/// abweist — der Gegner im Schach, Bauern auf der Grundreihe, Rochaderechte ohne König/Turm auf dem
/// Startfeld, ein unmögliches en-passant-Feld. Stockfish 19 BEENDET sich bei einer ungültigen Stellung
/// (siehe engine-provider/Dockerfile), und mit ihm der Provider: EINE krumme Stellung kostete den ganzen
/// Engine-Pool einen Neustart. Hier ist die Stelle, an der sie gar nicht erst hinkommt.</para>
/// </summary>
public static class WorkSanitizer
{
    public const int MaxMoves = 600;
    public const int MaxMultiPv = 5;

    public static SanitizedWork Sanitize(EngineWork work, int maxThreads, int maxHash)
    {
        if (string.IsNullOrEmpty(work.SessionId)) throw new InvalidWorkException("sessionId is required");
        var limits = (work.Depth.HasValue ? 1 : 0) + (work.Movetime.HasValue ? 1 : 0) + (work.Nodes.HasValue ? 1 : 0);
        if (limits != 1) throw new InvalidWorkException("exactly one of depth/movetime/nodes is required");
        if (work.Depth is < 1 || work.Movetime is < 1 || work.Nodes is < 1)
            throw new InvalidWorkException("search limit must be positive");
        if (work.Threads < 1 || work.Hash < 1) throw new InvalidWorkException("threads and hash must be positive");
        if (work.MultiPv > MaxMultiPv) throw new InvalidWorkException("invalid multipv: supported range is 1 to 5");
        if (work.Moves.Count > MaxMoves) throw new InvalidWorkException("too many moves");

        var fen = NormalizeFen(work.InitialFen);
        var board = LoadStrict(fen);

        var moves = new List<string>(work.Moves.Count);
        foreach (var raw in work.Moves)
        {
            if (!UciLineParser.TryParseMove(raw ?? string.Empty, out var uci))
                throw new InvalidWorkException($"illegal uci move: {raw}");
            var played = BrokerChess.TryPlay(board, uci) ?? throw new InvalidWorkException($"illegal uci move: {raw}");
            moves.Add(played);
        }

        var sanitized = work with
        {
            Threads = Math.Min(work.Threads, Math.Max(1, maxThreads)),
            Hash = Math.Min(work.Hash, Math.Max(1, maxHash)),
            MultiPv = Math.Max(1, work.MultiPv),
            InitialFen = fen,
            Moves = moves,
        };
        return new SanitizedWork(sanitized, board.ToFen());
    }

    /// <summary>Leerraum vereinheitlichen und fehlende Zähler ergänzen (shakmaty nimmt eine FEN ohne sie an,
    /// Gera.Chess nicht).</summary>
    public static string NormalizeFen(string? fen)
    {
        var parts = (fen ?? string.Empty).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 4 or > 6) throw new InvalidWorkException("illegal initial position: malformed fen");
        var list = parts.ToList();
        if (list.Count == 4) list.Add("0");
        if (list.Count == 5) list.Add("1");
        return string.Join(' ', list);
    }

    private static ChessBoard LoadStrict(string fen)
    {
        ChessBoard board;
        try { board = BrokerChess.Load(fen); }
        catch (Exception ex) { throw new InvalidWorkException($"illegal initial position: {ex.Message}"); }

        var parts = fen.Split(' ');
        var rows = ExpandPlacement(parts[0]);
        var whiteToMove = parts[1] == "w";

        // Bauern auf der Grundreihe (shakmaty PAWNS_ON_BACKRANK).
        if (rows[0].Contains('P') || rows[0].Contains('p') || rows[7].Contains('P') || rows[7].Contains('p'))
            throw new InvalidWorkException("illegal initial position: pawns on backrank");

        // Der Gegner steht im Schach (shakmaty OPPOSITE_CHECK) — die Seite am Zug könnte den König schlagen.
        if (whiteToMove ? board.BlackKingChecked : board.WhiteKingChecked)
            throw new InvalidWorkException("illegal initial position: opposite check");

        // Rochaderechte nur mit König und Turm auf den Standardfeldern (shakmaty BAD_CASTLING_RIGHTS; eine
        // Chess960-Aufstellung kennt RookHub nicht).
        foreach (var right in parts[2] == "-" ? "" : parts[2])
        {
            var (rank, king, rook, rookFile) = right switch
            {
                'K' => (0, 'K', 'R', 7),
                'Q' => (0, 'K', 'R', 0),
                'k' => (7, 'k', 'r', 7),
                'q' => (7, 'k', 'r', 0),
                _ => throw new InvalidWorkException("illegal initial position: bad castling rights"),
            };
            if (rows[rank][4] != king || rows[rank][rookFile] != rook)
                throw new InvalidWorkException("illegal initial position: bad castling rights");
        }

        // En passant: Feld auf der richtigen Reihe, leer, das Feld dahinter leer, davor der gezogene Bauer
        // (shakmaty INVALID_EP_SQUARE).
        if (parts[3] != "-")
        {
            var file = parts[3][0] - 'a';
            var epRank = parts[3][1] - '1';
            var (expectedRank, pawnRank, fromRank, pawn) = whiteToMove ? (5, 4, 6, 'p') : (2, 3, 1, 'P');
            if (epRank != expectedRank || rows[epRank][file] != '.' || rows[fromRank][file] != '.' || rows[pawnRank][file] != pawn)
                throw new InvalidWorkException("illegal initial position: invalid ep square");
        }
        return board;
    }

    /// <summary>Stellungsteil als acht Zeilen, Index 0 = Reihe 1, leere Felder als <c>.</c>.</summary>
    private static char[][] ExpandPlacement(string placement)
    {
        var ranks = placement.Split('/');
        var rows = new char[8][];
        for (var i = 0; i < 8; i++)
        {
            var row = new List<char>(8);
            foreach (var c in ranks[i])
            {
                if (char.IsAsciiDigit(c)) row.AddRange(Enumerable.Repeat('.', c - '0'));
                else row.Add(c);
            }
            if (row.Count != 8) throw new InvalidWorkException("illegal initial position: malformed fen");
            rows[7 - i] = [.. row];
        }
        return rows;
    }
}
