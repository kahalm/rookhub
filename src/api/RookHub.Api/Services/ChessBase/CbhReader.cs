using System.Text;

namespace RookHub.Api.Services.ChessBase;

/// <summary>
/// Liest das klassische ChessBase-Format (<c>.cbh</c>-Familie, ChessBase 6 bis heute) — nach der
/// Formatbeschreibung in Morphys <c>format/v1</c> (reverse engineered, ChessBase veröffentlicht nichts), geprüft gegen den
/// MIT-lizenzierten Leser von oschess-cb-bridge v1.0.3. Nur, was die Vereins-Datenbank braucht: Kopfdaten, Namen,
/// Turnier und die HAUPTVARIANTE — keine Anmerkungen, keine Varianten.
///
/// <para><b>Die Hauptvariante steht vorn.</b> Der Zugbaum ist in Tiefensuche geschrieben, an jeder Stellung die
/// Hauptfortsetzung zuerst; <c>254</c> merkt eine Stellung, <c>255</c> kehrt zur gemerkten zurück. Alles vor der ERSTEN
/// <c>255</c> ist also die Hauptvariante — die <c>254</c> davor gehören zu Varianten, die erst danach kommen.</para>
///
/// <para><b>Die kompakte Kodierung nennt Figuren, nicht Felder</b> („der zweite Turm drei Felder nach oben"). Figuren
/// einer Art sind in der Reihenfolge nummeriert, in der man sie beim Absuchen der Startstellung findet (<c>a1</c>,
/// <c>a2</c>, … <c>h8</c>); wird eine geschlagen, rücken die dahinter nach, ein umgewandelter Bauer kommt hinten dazu, und
/// jeder Bauer behält seine Nummer die ganze Partie.</para>
/// </summary>
internal static class CbhReader
{
    private const int HeadSize = 46;

    public static ChessBaseReadResult Read(ChessBaseFiles files, int maxGames, CancellationToken ct = default)
    {
        var cbh = files.Require(".cbh");
        var cbg = files.Require(".cbg");
        var players = new Entities(files.Require(".cbp"));
        var tournaments = new Entities(files.Require(".cbt"));
        var annotators = files.Get(".cbc") is { } cbc ? new Entities(cbc) : null;

        var games = new List<ChessBaseGame>();
        int deleted = 0, texts = 0;
        var records = cbh.Length < HeadSize ? 0 : (cbh.Length - HeadSize) / HeadSize;
        for (var id = 1; id <= records; id++)
        {
            ct.ThrowIfCancellationRequested();
            var r = HeadSize * id;
            var type = cbh[r];
            if ((type & 1) == 0) continue;                                  // leerer Satz hinter der letzten Partie
            if ((type & 0x80) != 0) { deleted++; continue; }
            if ((type & 2) != 0) { texts++; continue; }                     // Lehrtext, keine Partie
            if (games.Count >= maxGames) return new(ChessBaseFormat.Cbh, games, deleted, texts, true);

            var tournament = (int)U24(cbh, r + 0x0f);
            var g = new ChessBaseGame
            {
                Id = id,
                White = PlayerName(players, (int)U24(cbh, r + 0x09)),
                Black = PlayerName(players, (int)U24(cbh, r + 0x0c)),
                Event = ChessBaseFields.OrUnknown(tournaments.Text(tournament, 0, 40)),
                Site = ChessBaseFields.OrUnknown(tournaments.Text(tournament, 40, 30)),
                Annotator = annotators?.Text((int)U24(cbh, r + 0x12), 0, 45) is { Length: > 0 } a ? a : null,
                Date = ChessBaseFields.Date((int)U24(cbh, r + 0x18)),
                Result = ChessBaseFields.Result(cbh[r + 0x1b]),
                Round = ChessBaseFields.Round(cbh[r + 0x1d], cbh[r + 0x1e]),
                WhiteElo = U16(cbh, r + 0x1f),
                BlackElo = U16(cbh, r + 0x21),
                Eco = ChessBaseFields.Eco(U16(cbh, r + 0x23)),
            };
            try
            {
                var (fen, sans) = Moves(cbg, U32(cbh, r + 0x01), ct);
                games.Add(g with { StartFen = fen, Moves = sans });
            }
            catch (ChessBaseMoveException e) { games.Add(g with { Error = e.Message }); }
        }
        return new(ChessBaseFormat.Cbh, games, deleted, texts, false);
    }

    private static string PlayerName(Entities players, int id) =>
        ChessBaseFields.Player(players.Text(id, 0, 30), players.Text(id, 30, 20));

    /// <summary>Die Hauptvariante des Satzes an <paramref name="offset"/> in <c>.cbg</c>.</summary>
    internal static (string? StartFen, List<string> Sans) Moves(byte[] cbg, long offset, CancellationToken ct = default)
    {
        if (offset < 0 || offset + 4 > cbg.Length) throw new ChessBaseMoveException("Zugsatz außerhalb der Datei.");
        var o = (int)offset;
        var flags = cbg[o];
        var size = (int)U24(cbg, o + 1);
        if (size < 4 || o + size > cbg.Length) throw new ChessBaseMoveException("Zugsatz mit falscher Länge.");
        var p = o + 4;
        string? fen = null;
        if ((flags & 0x40) != 0)
        {
            if (p + 28 > o + size) throw new ChessBaseMoveException("Startstellung abgeschnitten.");
            fen = SetupFen(cbg.AsSpan(p, 28));
            p += 28;
        }
        var mode = flags & 0x3f;
        var (table, pre, simple) = mode switch
        {
            0 => (CbhTables.Mode0, true, false),
            4 => (CbhTables.Mode4, false, false),
            5 => (CbhTables.Mode5, false, true),
            10 or 11 => throw new ChessBaseMoveException("Chess960 wird nicht übernommen."),
            _ => throw new ChessBaseMoveException($"Zugkodierung {mode} ist nicht bekannt."),
        };
        var board = MainlineBoard.FromFen(fen ?? MainlineBoard.StandardFen);
        var stream = cbg.AsSpan(p, o + size - p);
        byte T(byte b)
        {
            var n = (byte)board.Sans.Count;                                 // Züge bisher; Varianten kommen erst danach
            return pre ? table[(byte)(b - n)] : (byte)(table[b] - n);
        }
        if (simple) Simple(stream, board, T, ct);
        else Compact(stream, board, T, ct);
        return (fen, board.Sans);
    }

    private static void Compact(ReadOnlySpan<byte> s, MainlineBoard board, Func<byte, byte> t, CancellationToken ct)
    {
        var pieces = Pieces.Scan(board);
        var i = 0;
        while (i < s.Length)
        {
            // Je Byte, nicht je Partie: ein Satz darf bis 16 MB lang sein, und die Codes 236/254 spielen keinen Zug —
            // ohne diese Prüfung liefe ein Strom aus lauter Überspring-Codes am Zeitbudget vorbei.
            ct.ThrowIfCancellationRequested();
            var v = t(s[i++]);
            switch (v)
            {
                case 236: continue;                                          // wird übersprungen
                case 254: continue;                                          // eine Variante kommt später
                case 255: return;                                            // Ende der Hauptvariante
                case 235:
                    if (i + 2 > s.Length) throw new ChessBaseMoveException("Zwei-Byte-Zug abgeschnitten.");
                    var word = t(s[i]) << 8 | t(s[i + 1]);
                    i += 2;
                    pieces.Update(BySquares(board, word));
                    break;
                case >= 237:
                    throw new ChessBaseMoveException($"Code {v} ist kein Zug.");
                default:
                    pieces.Update(ByCode(board, pieces, v));
                    break;
            }
        }
        if (s.Length > 0) throw new ChessBaseMoveException("Zugstrom ohne Ende.");
    }

    private static void Simple(ReadOnlySpan<byte> s, MainlineBoard board, Func<byte, byte> t, CancellationToken ct)
    {
        for (var i = 0; i + 1 < s.Length; i += 2)
        {
            ct.ThrowIfCancellationRequested();
            var word = t(s[i]) << 8 | t(s[i + 1]);
            BySquares(board, word & 0x3fff);
            if ((word & 0x4000) != 0) return;                               // hier endet die Hauptvariante
        }
        if (s.Length > 0) throw new ChessBaseMoveException("Zugstrom ohne Ende.");
    }

    /// <summary>Ein Zug über seine Felder (Zwei-Byte-Form und einfache Kodierung): Bits 0–5 von, 6–11 nach, 12–13 die
    /// Umwandlung (Dame, Turm, Läufer, Springer).</summary>
    private static MainlineBoard.Effect BySquares(MainlineBoard board, int word)
    {
        var from = MainlineBoard.FromCb(word & 63);
        var to = MainlineBoard.FromCb(word >> 6 & 63);
        if (from == to) throw new ChessBaseMoveException((word & 0x0fff) == 0 ? "Nullzug in der Hauptvariante." : "Zug auf dasselbe Feld.");
        var promo = Math.Abs(board[from]) == MainlineBoard.Pawn && to / 8 is 0 or 7
            ? new[] { MainlineBoard.Queen, MainlineBoard.Rook, MainlineBoard.Bishop, MainlineBoard.Knight }[word >> 12 & 3]
            : 0;
        return board.Play(from, to, promo);
    }

    private static readonly (int X, int Y)[] KingSteps = { (0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1) };
    private static readonly (int X, int Y)[] KnightSteps = { (2, 1), (1, 2), (-1, 2), (-2, 1), (-2, -1), (-1, -2), (1, -2), (2, -1) };
    private static readonly (int X, int Y)[] QueenLines = { (0, 1), (1, 0), (1, 1), (1, -1) };
    private static readonly (int X, int Y)[] RookLines = { (0, 1), (1, 0) };
    private static readonly (int X, int Y)[] BishopLines = { (1, 1), (1, -1) };

    /// <summary>Ein Ein-Byte-Code der kompakten Kodierung (nach der Übersetzung).</summary>
    private static MainlineBoard.Effect ByCode(MainlineBoard board, Pieces pieces, int code)
    {
        var white = board.WhiteToMove;
        (int X, int Y) Line(int k, (int X, int Y)[] dirs) { var d = dirs[k / 7]; var n = k % 7 + 1; return (d.X * n, d.Y * n); }
        int kind, index;
        (int X, int Y) delta;
        switch (code)
        {
            case 0: throw new ChessBaseMoveException("Nullzug in der Hauptvariante.");
            case <= 8:
                return board.Play(board.KingSquare(white), Step(board.KingSquare(white), KingSteps[code - 1]), 0);
            case 9: return board.Castle(kingSide: true);
            case 10: return board.Castle(kingSide: false);
            case <= 38: (kind, index, delta) = (0, 0, Line(code - 11, QueenLines)); break;
            case <= 52: (kind, index, delta) = (1, 0, Line(code - 39, RookLines)); break;
            case <= 66: (kind, index, delta) = (1, 1, Line(code - 53, RookLines)); break;
            case <= 80: (kind, index, delta) = (2, 0, Line(code - 67, BishopLines)); break;
            case <= 94: (kind, index, delta) = (2, 1, Line(code - 81, BishopLines)); break;
            case <= 102: (kind, index, delta) = (3, 0, KnightSteps[code - 95]); break;
            case <= 110: (kind, index, delta) = (3, 1, KnightSteps[code - 103]); break;
            case <= 142:
            {
                var pawn = (code - 111) / 4;
                var f = white ? 1 : -1;
                var d = ((code - 111) % 4) switch { 0 => (0, f), 1 => (0, 2 * f), 2 => (f, f), _ => (-f, f) };
                var from = pieces.Pawn(white, pawn);
                if (from < 0) throw new ChessBaseMoveException($"Bauer Nummer {pawn + 1} gibt es nicht.");
                var to = Step(from, d);
                if (to / 8 is 0 or 7) throw new ChessBaseMoveException("Umwandlung in einem Ein-Byte-Zug.");
                return board.Play(from, to, 0);
            }
            case <= 170: (kind, index, delta) = (0, 1, Line(code - 143, QueenLines)); break;
            case <= 198: (kind, index, delta) = (0, 2, Line(code - 171, QueenLines)); break;
            case <= 212: (kind, index, delta) = (1, 2, Line(code - 199, RookLines)); break;
            case <= 226: (kind, index, delta) = (2, 2, Line(code - 213, BishopLines)); break;
            case <= 234: (kind, index, delta) = (3, 2, KnightSteps[code - 227]); break;
            default: throw new ChessBaseMoveException($"Code {code} ist kein Zug.");
        }
        var sq = pieces.Get(white, kind, index);
        if (sq < 0) throw new ChessBaseMoveException($"Figur {Pieces.KindName(kind)} Nummer {index + 1} gibt es nicht.");
        return board.Play(sq, Step(sq, delta), 0);
    }

    /// <summary>Feld + Schritt, jede Koordinate modulo 8 (so kodiert ChessBase Züge nach links und unten).</summary>
    private static int Step(int sq, (int X, int Y) d)
    {
        var x = ((sq % 8 + d.X) % 8 + 8) % 8;
        var y = ((sq / 8 + d.Y) % 8 + 8) % 8;
        return y * 8 + x;
    }

    /// <summary>
    /// Startstellung (28 Bytes): Byte 1 en passant-Linie (Bits 0–3) und Seite am Zug (Bit 4), Byte 2 Rochaderechte
    /// (0 weiß lang, 1 weiß kurz, 2 schwarz lang, 3 schwarz kurz), Byte 3 Zugnummer, dann 192 Bit: je Feld (<c>a1</c>,
    /// <c>a2</c>, …) ein 0-Bit für leer oder 1, Farbe, drei Bit Figur.
    /// </summary>
    internal static string SetupFen(ReadOnlySpan<byte> record)
    {
        var s = record.ToArray();                                          // die lokale Funktion unten braucht ein Array
        var board = new char[64];
        var bit = 0;
        bool Next() { var b = (s[4 + bit / 8] >> (7 - bit % 8) & 1) == 1; bit++; return b; }
        for (var cb = 0; cb < 64 && bit < 192; cb++)
        {
            if (!Next()) continue;
            if (bit + 4 > 192) throw new ChessBaseMoveException("Startstellung abgeschnitten.");
            var black = Next();
            var code = (Next() ? 4 : 0) | (Next() ? 2 : 0) | (Next() ? 1 : 0);
            var c = code switch { 1 => 'k', 2 => 'q', 3 => 'n', 4 => 'b', 5 => 'r', 6 => 'p', _ => throw new ChessBaseMoveException("Unbekannte Figur in der Startstellung.") };
            board[MainlineBoard.FromCb(cb)] = black ? c : char.ToUpperInvariant(c);
        }
        var sb = new StringBuilder();
        for (var rank = 7; rank >= 0; rank--)
        {
            var empty = 0;
            for (var file = 0; file < 8; file++)
            {
                var c = board[rank * 8 + file];
                if (c == '\0') { empty++; continue; }
                if (empty > 0) { sb.Append(empty); empty = 0; }
                sb.Append(c);
            }
            if (empty > 0) sb.Append(empty);
            if (rank > 0) sb.Append('/');
        }
        var blackToMove = (s[1] & 0x10) != 0;
        var castling = new StringBuilder();
        if ((s[2] & 2) != 0) castling.Append('K');
        if ((s[2] & 1) != 0) castling.Append('Q');
        if ((s[2] & 8) != 0) castling.Append('k');
        if ((s[2] & 4) != 0) castling.Append('q');
        var ep = s[1] & 0x0f;
        var epSquare = ep is >= 1 and <= 8 ? $"{(char)('a' + ep - 1)}{(blackToMove ? 3 : 6)}" : "-";
        var move = Math.Max(1, (int)s[3]);
        return $"{sb} {(blackToMove ? 'b' : 'w')} {(castling.Length == 0 ? "-" : castling.ToString())} {epSquare} 0 {move}";
    }

    /// <summary>
    /// Die Figurenlisten der kompakten Kodierung je Seite: Dame, Turm, Läufer, Springer (in dieser Reihenfolge, wie die
    /// Codes sie nennen) und die acht Bauern mit festen Nummern.
    /// </summary>
    private sealed class Pieces
    {
        private static readonly int[] Kinds = { MainlineBoard.Queen, MainlineBoard.Rook, MainlineBoard.Bishop, MainlineBoard.Knight };
        private readonly List<int>[,] _lists = new List<int>[2, 4];
        private readonly int[,] _pawns = new int[2, 8];

        private Pieces()
        {
            for (var c = 0; c < 2; c++)
            {
                for (var k = 0; k < 4; k++) _lists[c, k] = new List<int>();
                for (var p = 0; p < 8; p++) _pawns[c, p] = -1;
            }
        }

        public static string KindName(int kind) => kind switch { 0 => "Dame", 1 => "Turm", 2 => "Läufer", _ => "Springer" };

        /// <summary>Nummeriert die Figuren der Startstellung in ChessBase-Feldreihenfolge (<c>a1</c>, <c>a2</c>, … <c>h8</c>).</summary>
        public static Pieces Scan(MainlineBoard board)
        {
            var p = new Pieces();
            var nextPawn = new int[2];
            for (var cb = 0; cb < 64; cb++)
            {
                var sq = MainlineBoard.FromCb(cb);
                var piece = board[sq];
                if (piece == 0) continue;
                var c = piece > 0 ? 0 : 1;
                var kind = Math.Abs(piece);
                if (kind == MainlineBoard.Pawn)
                {
                    if (nextPawn[c] >= 8) throw new ChessBaseMoveException("Mehr als acht Bauern.");
                    p._pawns[c, nextPawn[c]++] = sq;
                }
                else if (Array.IndexOf(Kinds, kind) is var k and >= 0) p._lists[c, k].Add(sq);
            }
            return p;
        }

        public int Get(bool white, int kind, int index)
        {
            var list = _lists[white ? 0 : 1, kind];
            return index < list.Count ? list[index] : -1;
        }

        public int Pawn(bool white, int index) => _pawns[white ? 0 : 1, index];

        /// <summary>Führt einen gespielten Zug in den Listen nach: das Geschlagene fällt heraus (die dahinter rücken nach,
        /// ein Bauer hinterlässt seine Nummer leer), die gezogene Figur wandert mit, eine Umwandlung kommt hinten an.</summary>
        public void Update(MainlineBoard.Effect e)
        {
            var me = e.Piece > 0 ? 0 : 1;
            var them = 1 - me;
            if (e.Captured != 0)
            {
                var ck = Math.Abs(e.Captured);
                if (ck == MainlineBoard.Pawn) Clear(them, e.CapturedSquare);
                else if (Array.IndexOf(Kinds, ck) is var k and >= 0) _lists[them, k].Remove(e.CapturedSquare);
            }
            var kind = Math.Abs(e.Piece);
            if (kind == MainlineBoard.Pawn)
            {
                var slot = Slot(me, e.From);
                if (slot < 0) return;
                if (e.Promotion != 0)
                {
                    _pawns[me, slot] = -1;
                    _lists[me, Array.IndexOf(Kinds, e.Promotion)].Add(e.To);
                }
                else _pawns[me, slot] = e.To;
            }
            else if (Array.IndexOf(Kinds, kind) is var k and >= 0) Relocate(_lists[me, k], e.From, e.To);
            if (e.RookFrom >= 0) Relocate(_lists[me, 1], e.RookFrom, e.RookTo);
        }

        private int Slot(int c, int sq)
        {
            for (var i = 0; i < 8; i++) if (_pawns[c, i] == sq) return i;
            return -1;
        }

        private void Clear(int c, int sq)
        {
            var i = Slot(c, sq);
            if (i >= 0) _pawns[c, i] = -1;
        }

        private static void Relocate(List<int> list, int from, int to)
        {
            var i = list.IndexOf(from);
            if (i >= 0) list[i] = to;
        }
    }

    /// <summary>Die Einträge einer Entitätsdatei (<c>.cbp</c>, <c>.cbt</c>, <c>.cbc</c>): Kopf von 28 (bzw. 32) Byte, dann
    /// Sätze aus 9 Byte Baumknoten und den Daten; Zeichen in ISO 8859-1, bis zur ersten Null.</summary>
    private sealed class Entities
    {
        private readonly byte[] _b;
        private readonly int _head, _record, _count;

        public Entities(byte[] b)
        {
            _b = b;
            if (b.Length < 28) return;
            _count = BitConverter.ToInt32(b, 0);
            var data = BitConverter.ToInt32(b, 0x0c);
            var extra = BitConverter.ToInt32(b, 0x18);
            _head = 28 + (extra == 4 ? 4 : 0);
            _record = 9 + data;
        }

        public string Text(int id, int offset, int length)
        {
            if (_record <= 9 || id < 0 || id >= _count) return string.Empty;
            var at = _head + (long)id * _record + 9 + offset;
            if (at < 0 || at + length > _b.Length) return string.Empty;
            var span = _b.AsSpan((int)at, length);
            var end = span.IndexOf((byte)0);
            return Encoding.Latin1.GetString(end < 0 ? span : span[..end]).Trim();
        }
    }

    private static uint U24(byte[] b, int o) => (uint)(b[o] << 16 | b[o + 1] << 8 | b[o + 2]);
    private static int U16(byte[] b, int o) => b[o] << 8 | b[o + 1];
    private static long U32(byte[] b, int o) => (long)b[o] << 24 | (long)b[o + 1] << 16 | (long)b[o + 2] << 8 | b[o + 3];
}
