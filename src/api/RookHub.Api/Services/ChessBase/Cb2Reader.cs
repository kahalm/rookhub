using System.Text;

namespace RookHub.Api.Services.ChessBase;

/// <summary>
/// Liest das ChessBase-2-Format (<c>.2cbh</c>-Familie, ChessBase 17 und später) — nach Morphys <c>format/v2</c>,
/// geprüft gegen den MIT-lizenzierten Leser von oschess-cb-bridge v1.0.3. Wie <see cref="CbhReader"/> nur Kopfdaten,
/// Namen, Turnier und die Hauptvariante.
///
/// <para>Anders als das klassische Format ist ein Zug hier ein WORT, das Figur, Ausgangs-, Zielfeld, geschlagene Figur und
/// Umwandlung aufzählt — dieselbe Bedeutung in jeder Stellung (<see cref="Cb2MoveTable"/>). Die Hauptvariante steht
/// vollständig vorn, bis zum ersten <c>ffff</c>; <c>fffd</c> kündigt eine Variante an, die erst danach kommt.</para>
/// </summary>
internal static class Cb2Reader
{
    private const int RecordSize = 192;
    private const ushort NullMove = 0xfffa, StartPosition = 0xfffb, MovesMarker = 0xfffc, Alternative = 0xfffd, EndOfLine = 0xffff;

    public static ChessBaseReadResult Read(ChessBaseFiles files, int maxGames)
    {
        var cbh = files.Require(".2cbh");
        var cbg = files.Require(".2cbg");
        var entities = new Entities(files.Require(".2lid"));

        var games = new List<ChessBaseGame>();
        int deleted = 0, texts = 0;
        var records = cbh.Length < RecordSize ? 0 : cbh.Length / RecordSize - 1;
        for (var id = 1; id <= records; id++)
        {
            var r = RecordSize * id;
            var type = cbh[r];
            if ((type & 1) == 0) continue;
            if ((type & 0x80) != 0) { deleted++; continue; }
            if ((type & 2) != 0 || cbh[r + 2] != 1) { texts++; continue; }  // Lehrtext oder Analyse, keine Partie
            if (games.Count >= maxGames) return new(ChessBaseFormat.Cb2, games, deleted, texts, true);

            var white = entities.Player(I64(cbh, r + 0x18));
            var black = entities.Player(I64(cbh, r + 0x20));
            var (place, title) = entities.Tournament(I64(cbh, r + 0x28));
            var annotator = entities.Player(I64(cbh, r + 0x30));
            var g = new ChessBaseGame
            {
                Id = id,
                White = ChessBaseFields.Player(white.Last, white.First),
                Black = ChessBaseFields.Player(black.Last, black.First),
                WhiteFideId = white.Fide > 0 ? white.Fide.ToString() : null,
                BlackFideId = black.Fide > 0 ? black.Fide.ToString() : null,
                Event = ChessBaseFields.OrUnknown(title),
                Site = ChessBaseFields.OrUnknown(place),
                Annotator = ChessBaseFields.Player(annotator.Last, annotator.First) is var a && a != "?" ? a : null,
                Result = ChessBaseFields.Result(cbh[r + 0x58]),
                Round = ChessBaseFields.Round(I16(cbh, r + 0x5a), I16(cbh, r + 0x5c)),
                WhiteElo = I16(cbh, r + 0x60),
                BlackElo = I16(cbh, r + 0x70),
                Eco = ChessBaseFields.Eco(U16(cbh, r + 0x80)),
                Date = ChessBaseFields.Date(BitConverter.ToInt32(cbh, r + 0xbc)),
            };
            try
            {
                var (fen, sans) = Moves(cbg, I64(cbh, r + 0x08));
                games.Add(g with { StartFen = fen, Moves = sans });
            }
            catch (ChessBaseMoveException e) { games.Add(g with { Error = e.Message }); }
        }
        return new(ChessBaseFormat.Cb2, games, deleted, texts, false);
    }

    /// <summary>Die Hauptvariante des Satzes an <paramref name="offset"/> in <c>.2cbg</c>: 8 Byte Kennung, Länge A des
    /// Inhalts, Reserve B, Prüfsumme, 2 Byte Art (<c>01 00</c> Schach, <c>02 00</c> Chess960), dann A Byte Wörter.</summary>
    internal static (string? StartFen, List<string> Sans) Moves(byte[] cbg, long offset)
    {
        if (offset < 0 || offset + 26 > cbg.Length) throw new ChessBaseMoveException("Zugsatz außerhalb der Datei.");
        var o = (int)offset;
        if (BitConverter.ToUInt64(cbg, o) != 0x1122334455667788UL) throw new ChessBaseMoveException("Zugsatz ohne Kennung.");
        var length = BitConverter.ToInt32(cbg, o + 8);
        if (length < 0 || o + 26 + (long)length > cbg.Length || length % 2 != 0)
            throw new ChessBaseMoveException("Zugsatz mit falscher Länge.");
        var variant = cbg[o + 24] | cbg[o + 25] << 8;
        if (variant == 2) throw new ChessBaseMoveException("Chess960 wird nicht übernommen.");
        if (variant != 1) throw new ChessBaseMoveException("Kein Zugsatz einer Partie.");

        var words = new ushort[length / 2];
        for (var i = 0; i < words.Length; i++) words[i] = BitConverter.ToUInt16(cbg, o + 26 + 2 * i);

        var at = 0;
        string? fen = null;
        if (at < words.Length && words[at] == StartPosition)
        {
            at++;
            var end = Array.IndexOf(words, MovesMarker, at);
            if (end < 0) throw new ChessBaseMoveException("Startstellung ohne Züge.");
            if (end - at == 1 || (end - at > 0 && words[at] == 1000))
                throw new ChessBaseMoveException("Chess960 wird nicht übernommen.");
            fen = SetupFen(words.AsSpan(at, end - at));
            at = end;
        }
        if (at >= words.Length || words[at] != MovesMarker) throw new ChessBaseMoveException("Zugstrom ohne Anfang.");
        at++;
        var board = MainlineBoard.FromFen(fen ?? MainlineBoard.StandardFen);
        for (; at < words.Length; at++)
        {
            var w = words[at];
            if (w == EndOfLine) return (fen, board.Sans);
            if (w == Alternative) continue;
            if (w == NullMove) throw new ChessBaseMoveException("Nullzug in der Hauptvariante.");
            var mv = Cb2MoveTable.Decode(w) ?? throw new ChessBaseMoveException($"Wort {w:x4} ist kein Zug.");
            if (mv.White != board.WhiteToMove) throw new ChessBaseMoveException("Zug der falschen Seite.");
            if (mv.Castle != 0) board.Castle(kingSide: mv.Castle > 0);
            else board.Play(mv.From, mv.To, mv.Promotion);
        }
        throw new ChessBaseMoveException("Zugstrom ohne Ende.");
    }

    /// <summary>Aufgestellte Stellung: Zugnummer, Seite am Zug (niedriges Byte) + en passant-Linie (hohes Byte),
    /// Rochaderechte (1 weiß lang, 2 weiß kurz, 4 schwarz lang, 8 schwarz kurz), dann je Figur ein Wort ab <c>c02d</c>.</summary>
    internal static string SetupFen(ReadOnlySpan<ushort> w)
    {
        if (w.Length < 3) throw new ChessBaseMoveException("Startstellung abgeschnitten.");
        var board = new char[64];
        foreach (var word in w[3..])
        {
            var (white, kind, sq) = Cb2MoveTable.DecodePiece(word) ?? throw new ChessBaseMoveException($"Wort {word:x4} ist keine Figur.");
            var c = kind switch { MainlineBoard.King => 'k', MainlineBoard.Queen => 'q', MainlineBoard.Knight => 'n', MainlineBoard.Bishop => 'b', MainlineBoard.Rook => 'r', _ => 'p' };
            board[sq] = white ? char.ToUpperInvariant(c) : c;
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
        var blackToMove = (w[1] & 0xff) == 1;
        var ep = w[1] >> 8;
        var castling = new StringBuilder();
        if ((w[2] & 2) != 0) castling.Append('K');
        if ((w[2] & 1) != 0) castling.Append('Q');
        if ((w[2] & 8) != 0) castling.Append('k');
        if ((w[2] & 4) != 0) castling.Append('q');
        var epSquare = ep is >= 1 and <= 8 ? $"{(char)('a' + ep - 1)}{(blackToMove ? 3 : 6)}" : "-";
        return $"{sb} {(blackToMove ? 'b' : 'w')} {(castling.Length == 0 ? "-" : castling.ToString())} {epSquare} 0 {Math.Max(1, (int)w[0])}";
    }

    /// <summary>
    /// <c>.2lid</c>: Kopf (big-endian) mit Containergröße und Anzahl je Entitätsart; danach Blöcke, Block <i>i</i> hält
    /// Entität <i>i</i> JEDER Art, jede in ihrem Container. Ein Satz beginnt mit seiner Länge (0 = unbenutzt), Texte als
    /// Länge + UTF-8.
    /// </summary>
    private sealed class Entities
    {
        private readonly byte[] _b;
        private readonly int _head;
        private readonly long _block;
        private readonly long[] _start = new long[6];
        private readonly long[] _count = new long[6];

        public Entities(byte[] b)
        {
            _b = b;
            if (b.Length < 8) return;
            _head = BE32(b, 0);
            var types = Math.Min(BE32(b, 4), 6);
            long offset = 0;
            for (var i = 0; i < types && 0x14 + 20 * i + 8 <= b.Length; i++)
            {
                var container = BE32(b, 8 + 20 * i);
                _count[i] = BE64(b, 0x0c + 20 * i);
                _start[i] = offset;
                offset += container;
            }
            _block = offset;
        }

        private int Record(int type, long id)
        {
            if (_block <= 0 || id < 0 || id >= _count[type]) return -1;
            var at = _head + id * _block + _start[type];
            if (at + 4 > _b.Length) return -1;
            var length = BitConverter.ToInt32(_b, (int)at);
            return length <= 0 || at + 4 + length > _b.Length ? -1 : (int)at + 4;
        }

        private string Text(ref int at, int end)
        {
            if (at + 4 > end) return string.Empty;
            var n = BitConverter.ToInt32(_b, at);
            at += 4;
            if (n < 0 || at + n > end) { at = end; return string.Empty; }
            var s = Encoding.UTF8.GetString(_b, at, n).TrimEnd('\0').Trim();
            at += n;
            return s;
        }

        public (string Last, string First, long Fide) Player(long id)
        {
            var at = Record(0, id);
            if (at < 0) return (string.Empty, string.Empty, 0);
            var end = at + BitConverter.ToInt32(_b, at - 4);
            var last = Text(ref at, end);
            var first = Text(ref at, end);
            long fide = 0;
            if (at + 24 <= end) fide = BitConverter.ToInt64(_b, at + 16);   // 0, 0, ChessBase-Id, Länge 8, FIDE-ID
            return (last, first, fide);
        }

        /// <summary>Ort VOR dem Titel — so steht es in der Datei.</summary>
        public (string Place, string Title) Tournament(long id)
        {
            var at = Record(1, id);
            if (at < 0) return (string.Empty, string.Empty);
            var end = at + BitConverter.ToInt32(_b, at - 4);
            var place = Text(ref at, end);
            var title = Text(ref at, end);
            return (place, title);
        }
    }

    private static long I64(byte[] b, int o) => BitConverter.ToInt64(b, o);
    private static int I16(byte[] b, int o) => BitConverter.ToInt16(b, o);
    private static int U16(byte[] b, int o) => BitConverter.ToUInt16(b, o);
    private static int BE32(byte[] b, int o) => b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3];
    private static long BE64(byte[] b, int o) => (long)(uint)BE32(b, o) << 32 | (uint)BE32(b, o + 4);
}

/// <summary>
/// Die Zugwörter von 2CBH: jeder Zug, den eine Figur auf dem LEEREN Brett machen kann, in fester Reihenfolge
/// aufgezählt (Morphy <c>format/v2/2-moves.md</c>, „Move words"). Wort 0 ist unbenutzt; König, Dame, Springer, Läufer,
/// Turm von Weiß, dann von Schwarz; je Ausgangsfeld (<c>a1</c>, <c>a2</c>, … spaltenweise) die Ziele je Richtung nach
/// außen, jedes Ziel sechs Wörter (ohne Schlag, schlägt Dame, Springer, Läufer, Turm, Bauer); danach die Bauern und die
/// Rochaden (<c>b129</c>–<c>b12c</c>). Aufgebaut einmal beim ersten Gebrauch.
/// </summary>
internal static class Cb2MoveTable
{
    /// <summary>Ein Zug: Seite, Felder (zeilenweise), Umwandlung (Art, 0 = keine), Rochade (+1 kurz, −1 lang, 0 keine).</summary>
    public readonly record struct MoveWord(bool White, int From, int To, int Promotion, int Castle);

    public const ushort FirstCastle = 0xb129, FirstCastle960 = 0xb12d, FirstPiece = 0xc02d, LastPiece = 0xc30c;

    private static readonly Lazy<MoveWord[]> Table = new(Build);

    public static int Count => Table.Value.Length;

    public static MoveWord? Decode(ushort word)
    {
        if (word == 0) return null;
        if (word < FirstCastle) return word < Table.Value.Length ? Table.Value[word] : null;
        return word switch
        {
            0xb129 => new MoveWord(true, 0, 0, 0, -1),
            0xb12a => new MoveWord(true, 0, 0, 0, 1),
            0xb12b => new MoveWord(false, 0, 0, 0, -1),
            0xb12c => new MoveWord(false, 0, 0, 0, 1),
            _ => null,                                                       // Chess960-Rochaden, Figurenwörter
        };
    }

    /// <summary>Figur einer aufgestellten Stellung: <c>c02d</c> + 64 · Art + Feld für Weiß K, D, S, L, T, dann Schwarz,
    /// dann die Bauern (48 Wörter je Farbe, Reihen 2–7).</summary>
    public static (bool White, int Kind, int Square)? DecodePiece(ushort word)
    {
        int[] order = { MainlineBoard.King, MainlineBoard.Queen, MainlineBoard.Knight, MainlineBoard.Bishop, MainlineBoard.Rook };
        const int blackPieces = FirstPiece + 5 * 64, whitePawns = blackPieces + 5 * 64, blackPawns = whitePawns + 48;
        if (word < FirstPiece || word > LastPiece) return null;
        if (word < whitePawns)
        {
            var white = word < blackPieces;
            var i = word - (white ? FirstPiece : blackPieces);
            return (white, order[i / 64], MainlineBoard.FromCb(i % 64));
        }
        var whitePawn = word < blackPawns;
        var j = word - (whitePawn ? whitePawns : blackPawns);
        return (whitePawn, MainlineBoard.Pawn, MainlineBoard.FromCb(j / 6 * 8 + j % 6 + 1));
    }

    private static MoveWord[] Build()
    {
        var t = new List<MoveWord>(FirstCastle) { default };
        (int F, int R)[] king = { (-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1) };
        (int F, int R)[] knight = { (-2, -1), (-2, 1), (2, -1), (2, 1), (-1, -2), (-1, 2), (1, -2), (1, 2) };
        (int F, int R)[] bishop = { (-1, -1), (1, -1), (1, 1), (-1, 1) };
        (int F, int R)[] rook = { (-1, 0), (0, -1), (1, 0), (0, 1) };
        var queen = bishop.Concat(rook).ToArray();
        foreach (var white in new[] { true, false })
        {
            foreach (var (dirs, slides) in new[] { (king, false), (queen, true), (knight, false), (bishop, true), (rook, true) })
            {
                for (var cb = 0; cb < 64; cb++)
                {
                    int f = cb / 8, r = cb % 8;
                    foreach (var (df, dr) in dirs)
                    {
                        for (var step = 1; ; step++)
                        {
                            int nf = f + df * step, nr = r + dr * step;
                            if (nf is < 0 or > 7 || nr is < 0 or > 7) break;
                            for (var captured = 0; captured < 6; captured++)
                                t.Add(new MoveWord(white, r * 8 + f, nr * 8 + nf, 0, 0));
                            if (!slides) break;
                        }
                    }
                }
            }
        }
        int[] promotions = { MainlineBoard.Queen, MainlineBoard.Knight, MainlineBoard.Bishop, MainlineBoard.Rook };
        foreach (var white in new[] { true, false })
        {
            var (d, start, pre, ep) = white ? (1, 2, 7, 5) : (-1, 7, 2, 4);
            for (var f = 0; f < 8; f++)
            {
                for (var rank = 2; rank <= 7; rank++)
                {
                    var r = rank - 1;
                    var from = r * 8 + f;
                    MoveWord Pawn(int toF, int toR, int promo) => new(white, from, toR * 8 + toF, promo, 0);
                    if (rank == start) { t.Add(Pawn(f, r + 2 * d, 0)); t.Add(Pawn(f, r + d, 0)); }
                    else if (rank == pre) foreach (var p in promotions) t.Add(Pawn(f, r + d, p));
                    else t.Add(Pawn(f, r + d, 0));
                    foreach (var df in new[] { -1, 1 })
                    {
                        var nf = f + df;
                        if (nf is < 0 or > 7) continue;
                        if (rank == pre)
                        {
                            for (var captured = 0; captured < 4; captured++)
                                foreach (var p in promotions) t.Add(Pawn(nf, r + d, p));
                        }
                        else
                        {
                            for (var captured = 0; captured < 5; captured++) t.Add(Pawn(nf, r + d, 0));
                            if (rank == ep) t.Add(Pawn(nf, r + d, 0));
                        }
                    }
                }
            }
        }
        if (t.Count != FirstCastle) throw new InvalidOperationException($"2CBH-Zugtabelle hat {t.Count} statt {FirstCastle} Einträge.");
        return t.ToArray();
    }
}
