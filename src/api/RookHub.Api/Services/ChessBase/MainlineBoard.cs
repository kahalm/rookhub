using Chess;

namespace RookHub.Api.Services.ChessBase;

/// <summary>
/// Die Hauptvariante einer Partie, Zug für Zug nachgespielt: das Brett von Gera.Chess prüft jeden Zug auf Legalität und
/// liefert die SAN, daneben steht ein eigenes Feld-Array — der klassische Leser muss wissen, WELCHE Figur wo steht (die
/// kompakte Kodierung nennt „den zweiten Läufer"), und Gera.Chess fragt man danach nicht zeilenweise.
///
/// <para>Felder sind hier zeilenweise gezählt (<c>a1</c> = 0, <c>b1</c> = 1, … <c>h8</c> = 63); ChessBase zählt
/// spaltenweise (<c>a1</c> = 0, <c>a2</c> = 1) — umgerechnet wird beim Lesen (<see cref="FromCb"/>).</para>
/// </summary>
internal sealed class MainlineBoard
{
    public const int King = 1, Queen = 2, Rook = 3, Bishop = 4, Knight = 5, Pawn = 6;

    /// <summary>Höchstens so viele Halbzüge je Hauptvariante; die längste bekannte Turnierpartie hat 538. Ohne Deckel
    /// dürfte ein einziger Zugsatz bis 16 MB legaler Pendelzüge enthalten — Millionen Züge samt SAN-Liste für EINE
    /// Partie. Längere Partien werden mit Grund übersprungen.</summary>
    public const int MaxPlies = 1000;

    private readonly ChessBoard _board;
    /// <summary>Figur je Feld: 0 leer, sonst die Art, positiv Weiß, negativ Schwarz.</summary>
    private readonly int[] _squares = new int[64];

    private MainlineBoard(ChessBoard board, bool whiteToMove)
    {
        _board = board;
        WhiteToMove = whiteToMove;
    }

    public bool WhiteToMove { get; private set; }

    public List<string> Sans { get; } = new();

    public static string StandardFen => "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public static MainlineBoard FromFen(string fen)
    {
        ChessBoard board;
        try { board = ChessBoard.LoadFromFen(fen); }
        catch (Exception e) { throw new ChessBaseMoveException($"Startstellung ungültig ({e.Message})."); }
        var parts = fen.Split(' ');
        var b = new MainlineBoard(board, parts.Length < 2 || parts[1] != "b");
        var rank = 7;
        var file = 0;
        foreach (var c in parts[0])
        {
            if (c == '/') { rank--; file = 0; continue; }
            if (char.IsDigit(c)) { file += c - '0'; continue; }
            var kind = char.ToLowerInvariant(c) switch { 'k' => King, 'q' => Queen, 'r' => Rook, 'b' => Bishop, 'n' => Knight, _ => Pawn };
            if (rank is >= 0 and < 8 && file is >= 0 and < 8) b._squares[rank * 8 + file] = char.IsUpper(c) ? kind : -kind;
            file++;
        }
        return b;
    }

    /// <summary>ChessBase-Feld (spaltenweise) → zeilenweise.</summary>
    public static int FromCb(int cb) => (cb % 8) * 8 + cb / 8;

    public static string Name(int sq) => $"{(char)('a' + sq % 8)}{sq / 8 + 1}";

    public int this[int sq] => _squares[sq];

    public int KingSquare(bool white)
    {
        var k = white ? King : -King;
        return Array.IndexOf(_squares, k);
    }

    /// <summary>Was ein Zug auf dem Brett verändert hat — der klassische Leser führt damit seine Figurenlisten.</summary>
    public readonly record struct Effect(int Piece, int From, int To, int CapturedSquare, int Captured, int Promotion,
        int RookFrom, int RookTo);

    /// <summary>Spielt den Zug <paramref name="from"/>→<paramref name="to"/> (Umwandlung als Art, 0 = keine). Die Rochade
    /// ist der Königszug um zwei Linien. Ein Zug, den die Stellung nicht hergibt, wirft.</summary>
    public Effect Play(int from, int to, int promotion)
    {
        if (Sans.Count >= MaxPlies)
            throw new ChessBaseMoveException($"Hauptvariante länger als {MaxPlies} Halbzüge.");
        var piece = _squares[from];
        if (piece == 0 || piece > 0 != WhiteToMove)
            throw new ChessBaseMoveException($"Auf {Name(from)} steht keine Figur der Seite am Zug.");
        var uci = Name(from) + Name(to) + promotion switch
        {
            Queen => "q", Rook => "r", Bishop => "b", Knight => "n", _ => "",
        };
        string san;
        try
        {
            if (promotion == 0)
            {
                // Direkt spielen: Gera prüft die Legalität selbst und schreibt die Notation samt „+"/„#" an den Zug. Die
                // Kandidaten der Figur aufzuzählen (und für jeden Schach und Matt zu prüfen) war sechsmal so langsam —
                // 3,5 ms je Partie, zu viel für eine Datenbank mit ein paar tausend Partien in einer Anfrage.
                if (!_board.Move(new Move(Name(from), Name(to))))
                    throw new ChessBaseMoveException($"{uci} ist in dieser Stellung nicht möglich.");
                san = _board.ExecutedMoves[^1].San is { Length: > 0 } s ? s : uci;
            }
            else
            {
                // Die Umwandlung wählt Gera beim direkten Zug selbst (Dame) — die gewünschte Figur gibt es nur über die Liste.
                var legal = Array.Find(_board.Moves(new Position(Name(from)), false, true), m => PgnParser.ToUci(m) == uci)
                    ?? throw new ChessBaseMoveException($"{uci} ist in dieser Stellung nicht möglich.");
                san = string.IsNullOrEmpty(legal.San) ? uci : legal.San;
                if (!_board.Move(legal)) throw new ChessBaseMoveException($"{uci} ließ sich nicht ausführen.");
            }
        }
        catch (ChessBaseMoveException) { throw; }
        catch (Exception e) { throw new ChessBaseMoveException($"{uci} ist in dieser Stellung nicht möglich ({e.GetType().Name})."); }
        Sans.Add(san);

        var kind = Math.Abs(piece);
        var capturedSquare = -1;
        var captured = 0;
        if (_squares[to] != 0) { capturedSquare = to; captured = _squares[to]; }
        else if (kind == Pawn && from % 8 != to % 8)
        {
            capturedSquare = (from / 8) * 8 + to % 8;                     // en passant: der Bauer neben dem Ziel
            captured = _squares[capturedSquare];
            _squares[capturedSquare] = 0;
        }
        int rookFrom = -1, rookTo = -1;
        if (kind == King && Math.Abs(to % 8 - from % 8) == 2)
        {
            var row = from / 8 * 8;
            (rookFrom, rookTo) = to % 8 == 6 ? (row + 7, row + 5) : (row, row + 3);
            _squares[rookTo] = _squares[rookFrom];
            _squares[rookFrom] = 0;
        }
        _squares[to] = promotion != 0 ? Math.Sign(piece) * promotion : piece;
        _squares[from] = 0;
        WhiteToMove = !WhiteToMove;
        return new Effect(piece, from, to, capturedSquare, captured, promotion, rookFrom, rookTo);
    }

    /// <summary>Rochade der Seite am Zug in der Grundstellung der Figuren (König auf e1/e8).</summary>
    public Effect Castle(bool kingSide)
    {
        var row = WhiteToMove ? 0 : 56;
        return Play(row + 4, row + (kingSide ? 6 : 2), 0);
    }
}

/// <summary>Ein Zug der Hauptvariante lässt sich nicht lesen oder nicht spielen — die Partie wird übersprungen.</summary>
public sealed class ChessBaseMoveException(string message) : Exception(message);
