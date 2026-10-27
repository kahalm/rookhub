namespace RookHub.Api.Services.Games;

/// <summary>
/// Ein kleines, schnelles Brett nur für die Taktik der Zug-Klassen (<see cref="MoveTactics"/>, 0.854.0): Figur auf
/// einem Feld, Angreifer eines Feldes (Pseudo-Angriffe ohne Röntgen), legale Züge, Schach und Matt in einem Zug. Gera.Chess
/// kennt keine Angreifer-Abfrage, und die Opfer-Erkennung fragt sie für jede Figur jeder Stellung — der Spiegel von
/// chess.js im Client (<c>attackers</c>, <c>moves({ verbose: true })</c>, <c>isAttacked</c>, <c>inCheck</c>).
///
/// <para>Felder 0..63 = a1..h8 (Index = Reihe × 8 + Linie). Figuren als FEN-Zeichen (Groß = Weiß). Rochade erzeugt das
/// Brett nicht als Zug — die Taktik fragt nur nach Schlagzügen und nach Matt in einem Zug, und ein Rochade-Matt bleibt
/// dabei unbeachtet; <see cref="Apply(string)"/> führt eine Rochade aus der Partie aber aus (König zwei Linien weit
/// oder auf den eigenen Turm). Wirft nie: eine unlesbare FEN heißt <c>null</c> aus <see cref="TryParse"/>.</para>
/// </summary>
public sealed class TacticsBoard
{
    /// <summary>Ein Zug auf diesem Brett. <see cref="Captured"/> = geschlagene Figur (beim en passant der Bauer daneben).</summary>
    public readonly record struct Move(int From, int To, char Piece, char? Captured, char? Promotion);

    private readonly char[] _squares;

    /// <summary>Weiß am Zug?</summary>
    public bool WhiteToMove { get; }

    /// <summary>En-passant-Zielfeld (0..63) oder <c>null</c>.</summary>
    public int? EpSquare { get; }

    private TacticsBoard(char[] squares, bool whiteToMove, int? ep)
    {
        _squares = squares;
        WhiteToMove = whiteToMove;
        EpSquare = ep;
    }

    /// <summary>Stellung aus einer FEN (nur Brett, Seite am Zug, en passant); <c>null</c>, wenn das Brett nicht lesbar ist
    /// oder ein König fehlt.</summary>
    public static TacticsBoard? TryParse(string? fen)
    {
        if (string.IsNullOrWhiteSpace(fen)) return null;
        var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rows = parts[0].Split('/');
        if (rows.Length != 8) return null;
        var squares = new char[64];
        for (var r = 0; r < 8; r++)
        {
            var rank = 7 - r;
            var file = 0;
            foreach (var ch in rows[r])
            {
                if (char.IsDigit(ch)) { file += ch - '0'; continue; }
                if ("pnbrqkPNBRQK".IndexOf(ch) < 0 || file > 7) return null;
                squares[rank * 8 + file] = ch;
                file++;
            }
            if (file != 8) return null;
        }
        if (Array.IndexOf(squares, 'K') < 0 || Array.IndexOf(squares, 'k') < 0) return null;
        var white = FenFields.WhiteToMove(fen);
        int? ep = parts.Length >= 4 && SquareIndex(parts[3]) is int e ? e : null;
        return new TacticsBoard(squares, white, ep);
    }

    /// <summary>FEN dieser Stellung (Rochaderechte „-", Zähler „0 1") — reicht für die Taktik, die nur Brett, Seite am Zug
    /// und en passant liest.</summary>
    public string ToFen()
    {
        var sb = new System.Text.StringBuilder();
        for (var rank = 7; rank >= 0; rank--)
        {
            var empty = 0;
            for (var file = 0; file < 8; file++)
            {
                var p = _squares[rank * 8 + file];
                if (p == '\0') { empty++; continue; }
                if (empty > 0) { sb.Append(empty); empty = 0; }
                sb.Append(p);
            }
            if (empty > 0) sb.Append(empty);
            if (rank > 0) sb.Append('/');
        }
        sb.Append(WhiteToMove ? " w - " : " b - ").Append(EpSquare is int e ? SquareName(e) : "-").Append(" 0 1");
        return sb.ToString();
    }

    /// <summary>„e4" → 28; <c>null</c> bei allem anderen.</summary>
    public static int? SquareIndex(string? name) =>
        name is { Length: 2 } && name[0] is >= 'a' and <= 'h' && name[1] is >= '1' and <= '8'
            ? (name[1] - '1') * 8 + (name[0] - 'a')
            : null;

    /// <summary>28 → „e4".</summary>
    public static string SquareName(int sq) => $"{(char)('a' + sq % 8)}{(char)('1' + sq / 8)}";

    /// <summary>Figur auf dem Feld oder <c>null</c>.</summary>
    public char? At(int sq) => _squares[sq] == '\0' ? null : _squares[sq];

    public static bool IsWhite(char piece) => char.IsUpper(piece);

    /// <summary>Alle besetzten Felder in der Reihenfolge von chess.js <c>board()</c> (Reihe 8 zuerst, je Reihe a→h) — bei zwei
    /// gleich teuren Opfern nennen Client und Server so dasselbe.</summary>
    public IEnumerable<(int Square, char Piece)> Pieces()
    {
        for (var rank = 7; rank >= 0; rank--)
            for (var file = 0; file < 8; file++)
                if (_squares[rank * 8 + file] is var p and not '\0') yield return (rank * 8 + file, p);
    }

    private static readonly (int Df, int Dr)[] KnightSteps = [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];
    private static readonly (int Df, int Dr)[] KingSteps = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];
    private static readonly (int Df, int Dr)[] Diagonals = [(1, 1), (-1, 1), (-1, -1), (1, -1)];
    private static readonly (int Df, int Dr)[] Lines = [(1, 0), (0, 1), (-1, 0), (0, -1)];

    private static int? Step(int sq, int df, int dr)
    {
        var f = sq % 8 + df;
        var r = sq / 8 + dr;
        return f is >= 0 and < 8 && r is >= 0 and < 8 ? r * 8 + f : null;
    }

    /// <summary>Felder der Farbe <paramref name="white"/>, deren Figuren <paramref name="sq"/> angreifen — Pseudo-Angriffe
    /// ohne Legalität und ohne Röntgen (wie chess.js <c>attackers</c>).</summary>
    public List<int> Attackers(int sq, bool white)
    {
        var result = new List<int>();
        // Bauern: ein weißer Bauer auf s greift s+7/s+9 an — also steht er auf sq−7/sq−9 (eine Reihe tiefer).
        var pawn = white ? 'P' : 'p';
        foreach (var df in new[] { -1, 1 })
            if (Step(sq, df, white ? -1 : 1) is int p && _squares[p] == pawn) result.Add(p);
        var knight = white ? 'N' : 'n';
        foreach (var (df, dr) in KnightSteps)
            if (Step(sq, df, dr) is int n && _squares[n] == knight) result.Add(n);
        var king = white ? 'K' : 'k';
        foreach (var (df, dr) in KingSteps)
            if (Step(sq, df, dr) is int k && _squares[k] == king) result.Add(k);
        Slide(sq, white, Diagonals, white ? 'B' : 'b', white ? 'Q' : 'q', result);
        Slide(sq, white, Lines, white ? 'R' : 'r', white ? 'Q' : 'q', result);
        return result;
    }

    private void Slide(int sq, bool white, (int Df, int Dr)[] dirs, char slider, char queen, List<int> into)
    {
        foreach (var (df, dr) in dirs)
        {
            var cur = sq;
            while (Step(cur, df, dr) is int next)
            {
                cur = next;
                var p = _squares[cur];
                if (p == '\0') continue;
                if (p == slider || p == queen) into.Add(cur);
                break;
            }
        }
    }

    /// <summary>Greift Farbe <paramref name="byWhite"/> das Feld an (Pseudo)?</summary>
    public bool IsAttacked(int sq, bool byWhite) => Attackers(sq, byWhite).Count > 0;

    private int KingSquare(bool white) => Array.IndexOf(_squares, white ? 'K' : 'k');

    /// <summary>Steht die Seite am Zug im Schach?</summary>
    public bool InCheck() => KingSquare(WhiteToMove) is var k and >= 0 && IsAttacked(k, !WhiteToMove);

    /// <summary>Den Zug ausführen — neues Brett, die andere Seite am Zug.</summary>
    public TacticsBoard Apply(Move m)
    {
        var squares = (char[])_squares.Clone();
        var piece = squares[m.From];
        squares[m.From] = '\0';
        if (char.ToLowerInvariant(piece) == 'p' && EpSquare == m.To && m.From % 8 != m.To % 8 && _squares[m.To] == '\0')
            squares[m.To + (IsWhite(piece) ? -8 : 8)] = '\0';
        squares[m.To] = m.Promotion is char promo ? (IsWhite(piece) ? char.ToUpperInvariant(promo) : char.ToLowerInvariant(promo)) : piece;
        int? ep = char.ToLowerInvariant(piece) == 'p' && Math.Abs(m.To - m.From) == 16 ? (m.From + m.To) / 2 : null;
        return new TacticsBoard(squares, !WhiteToMove, ep);
    }

    /// <summary>
    /// Einen Partiezug als UCI ausführen (auch die Rochade, als <c>e1g1</c> oder König-schlägt-Turm <c>e1h1</c>); <c>null</c>,
    /// wenn auf dem Ausgangsfeld nichts steht oder der Zug unlesbar ist. Prüft die Legalität NICHT — die Partie ist gespielt.
    /// </summary>
    public TacticsBoard? Apply(string? uci)
    {
        if (uci is not { Length: >= 4 } || SquareIndex(uci[..2]) is not int from || SquareIndex(uci[2..4]) is not int to) return null;
        var piece = _squares[from];
        if (piece == '\0') return null;
        if (char.ToLowerInvariant(piece) == 'k' && (Math.Abs(to % 8 - from % 8) > 1 || (_squares[to] != '\0' && IsWhite(_squares[to]) == IsWhite(piece))))
        {
            var kingside = to % 8 > from % 8;
            var rank = from / 8;
            var rookFrom = kingside ? FindRook(rank, from % 8 + 1, 7, piece) : FindRook(rank, 0, from % 8 - 1, piece);
            var squares = (char[])_squares.Clone();
            squares[from] = '\0';
            if (rookFrom is int rf) squares[rf] = '\0';
            squares[rank * 8 + (kingside ? 6 : 2)] = piece;
            if (rookFrom is int rf2) squares[rank * 8 + (kingside ? 5 : 3)] = _squares[rf2];
            return new TacticsBoard(squares, !WhiteToMove, null);
        }
        char? promo = uci.Length >= 5 && "qrbn".Contains(char.ToLowerInvariant(uci[4])) ? char.ToLowerInvariant(uci[4]) : null;
        return Apply(new Move(from, to, piece, _squares[to] == '\0' ? null : _squares[to], promo));
    }

    private int? FindRook(int rank, int fromFile, int toFile, char king)
    {
        var rook = IsWhite(king) ? 'R' : 'r';
        for (var f = fromFile; f <= toFile; f++)
            if (_squares[rank * 8 + f] == rook) return rank * 8 + f;
        return null;
    }

    /// <summary>Alle legalen Züge der Seite am Zug, ohne Rochade (siehe Klassenbeschreibung). Umwandlungen einzeln je Figur.</summary>
    public List<Move> LegalMoves()
    {
        var result = new List<Move>();
        foreach (var m in PseudoMoves())
            if (!Apply(m).KingAttackedOf(WhiteToMove)) result.Add(m);
        return result;
    }

    private bool KingAttackedOf(bool white) => KingSquare(white) is var k and >= 0 && IsAttacked(k, !white);

    private IEnumerable<Move> PseudoMoves()
    {
        var white = WhiteToMove;
        for (var sq = 0; sq < 64; sq++)
        {
            var p = _squares[sq];
            if (p == '\0' || IsWhite(p) != white) continue;
            switch (char.ToLowerInvariant(p))
            {
                case 'p':
                    foreach (var m in PawnMoves(sq, p, white)) yield return m;
                    break;
                case 'n':
                    foreach (var m in Steps(sq, p, white, KnightSteps)) yield return m;
                    break;
                case 'k':
                    foreach (var m in Steps(sq, p, white, KingSteps)) yield return m;
                    break;
                case 'b':
                    foreach (var m in Slides(sq, p, white, Diagonals)) yield return m;
                    break;
                case 'r':
                    foreach (var m in Slides(sq, p, white, Lines)) yield return m;
                    break;
                case 'q':
                    foreach (var m in Slides(sq, p, white, Diagonals)) yield return m;
                    foreach (var m in Slides(sq, p, white, Lines)) yield return m;
                    break;
            }
        }
    }

    private IEnumerable<Move> PawnMoves(int sq, char p, bool white)
    {
        var dir = white ? 1 : -1;
        var lastRank = white ? 7 : 0;
        IEnumerable<Move> Emit(int to, char? captured)
        {
            if (to / 8 == lastRank)
                foreach (var promo in "qrbn") yield return new Move(sq, to, p, captured, promo);
            else yield return new Move(sq, to, p, captured, null);
        }
        if (Step(sq, 0, dir) is int one && _squares[one] == '\0')
        {
            foreach (var m in Emit(one, null)) yield return m;
            var startRank = white ? 1 : 6;
            if (sq / 8 == startRank && Step(one, 0, dir) is int two && _squares[two] == '\0')
                yield return new Move(sq, two, p, null, null);
        }
        foreach (var df in new[] { -1, 1 })
        {
            if (Step(sq, df, dir) is not int to) continue;
            var target = _squares[to];
            if (target != '\0' && IsWhite(target) != white)
                foreach (var m in Emit(to, target)) yield return m;
            else if (target == '\0' && EpSquare == to)
                yield return new Move(sq, to, p, white ? 'p' : 'P', null);
        }
    }

    private IEnumerable<Move> Steps(int sq, char p, bool white, (int Df, int Dr)[] steps)
    {
        foreach (var (df, dr) in steps)
        {
            if (Step(sq, df, dr) is not int to) continue;
            var target = _squares[to];
            if (target == '\0') yield return new Move(sq, to, p, null, null);
            else if (IsWhite(target) != white) yield return new Move(sq, to, p, target, null);
        }
    }

    private IEnumerable<Move> Slides(int sq, char p, bool white, (int Df, int Dr)[] dirs)
    {
        foreach (var (df, dr) in dirs)
        {
            var cur = sq;
            while (Step(cur, df, dr) is int to)
            {
                cur = to;
                var target = _squares[to];
                if (target == '\0') { yield return new Move(sq, to, p, null, null); continue; }
                if (IsWhite(target) != white) yield return new Move(sq, to, p, target, null);
                break;
            }
        }
    }

    /// <summary>Hat die Seite am Zug ein Matt in einem Zug? (Spiegel von chess.js <c>moves().some(m =&gt; m.endsWith('#'))</c>.)</summary>
    public bool HasMateInOne()
    {
        foreach (var m in LegalMoves())
        {
            var after = Apply(m);
            if (after.InCheck() && after.LegalMoves().Count == 0) return true;
        }
        return false;
    }
}
