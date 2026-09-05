using Chess;

namespace RookHub.Api.Services;

/// <summary>
/// PGN → Halbzug-Liste: je Halbzug die Stellung DAVOR, der gespielte Zug in Standard-UCI und in SAN.
/// Reine Funktion (kein DB, keine Engine), damit sie ohne Infrastruktur testbar ist.
///
/// <para>Grundlage der Partie-Analyse: Jede dieser Stellungen wird einzeln von der Engine gerechnet.
/// Gelesen wird die HAUPTVARIANTE (Varianten in Klammern gehören nicht zur Partie), Startstellung ist
/// der PGN-Header <c>[FEN]</c> bzw. die Grundstellung.</para>
/// </summary>
public static class GamePlies
{
    /// <summary>Ein Halbzug der Partie.</summary>
    /// <param name="Ply">0-basiert; 0 = vor dem ersten Zug von Weiß.</param>
    /// <param name="Fen">Stellung VOR dem Zug.</param>
    /// <param name="Uci">Der gespielte Zug in Standard-UCI (Rochade <c>e1g1</c>, Umwandlung <c>e7e8q</c>).</param>
    /// <param name="San">Derselbe Zug in der Schreibweise des Bretts.</param>
    public readonly record struct Ply(int Index, string Fen, string Uci, string San);

    /// <summary>Kopfdaten der Partie aus den PGN-Headern.</summary>
    public readonly record struct GameHeader(string? White, string? Black, string? Result, string? Event, string StartFen);

    /// <summary>
    /// Zerlegt die ERSTE Partie eines PGN. <c>null</c>, wenn sich kein einziger Zug spielen lässt
    /// (kaputtes PGN, illegale Startstellung) — ein Teilergebnis wäre eine andere Partie.
    /// </summary>
    /// <param name="maxPlies">Deckel; darüber wird abgeschnitten (die Partie ist dann eben kürzer analysiert).</param>
    public static (GameHeader Header, List<Ply> Plies)? Parse(string? pgn, int maxPlies = 300)
    {
        if (string.IsNullOrWhiteSpace(pgn)) return null;

        var game = PgnParser.SplitGames(pgn).FirstOrDefault();
        if (game.MoveText is null) return null;

        var headers = game.Headers ?? new Dictionary<string, string>();
        headers.TryGetValue("FEN", out var fenHeader);
        var startFen = string.IsNullOrWhiteSpace(fenHeader) ? StartFen() : fenHeader.Trim();

        ChessBoard board;
        try { board = ChessBoard.LoadFromFen(startFen); }
        catch { return null; }

        // Die Hauptvariante als UCI — derselbe Weg wie beim Kurs-Import (Varianten/Kommentare raus).
        var uciMoves = PgnParser.TryExtractUciMainline(startFen, game.MoveText);
        if (uciMoves is null || uciMoves.Count == 0) return null;

        var plies = new List<Ply>(Math.Min(uciMoves.Count, maxPlies));
        for (var i = 0; i < uciMoves.Count && plies.Count < maxPlies; i++)
        {
            var fenBefore = board.ToFen();
            var legal = Array.Find(board.Moves(generateSan: true), m => ToUci(m) == uciMoves[i]);
            if (legal is null) break;   // ab hier ist die Partie nicht mehr nachspielbar → Präfix behalten
            plies.Add(new Ply(plies.Count, fenBefore, uciMoves[i],
                string.IsNullOrEmpty(legal.San) ? uciMoves[i] : legal.San));
            try { board.Move(legal); } catch { break; }
        }

        if (plies.Count == 0) return null;

        headers.TryGetValue("White", out var white);
        headers.TryGetValue("Black", out var black);
        headers.TryGetValue("Result", out var result);
        headers.TryGetValue("Event", out var evt);
        return (new GameHeader(Trim(white, 120), Trim(black, 120), Trim(result, 32), Trim(evt, 200), startFen), plies);
    }

    /// <summary>UCI eines Zuges des Bretts — Rochade als Königszug (<c>e1g1</c>), nicht als
    /// König-schlägt-Turm; der Broker liefert die andere Form, die beim EINLESEN umgeschrieben wird.
    /// Reicht bewusst an <see cref="PgnParser.ToUci"/> durch: die Zugliste der Partie kommt von dort,
    /// eine zweite Fassung derselben Regeln waere die erste Stelle, an der beide auseinanderlaufen.</summary>
    public static string ToUci(Move m) => PgnParser.ToUci(m);

    public static string StartFen() => new ChessBoard().ToFen();

    private static string? Trim(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : (s.Length <= max ? s.Trim() : s[..max].Trim());
}
