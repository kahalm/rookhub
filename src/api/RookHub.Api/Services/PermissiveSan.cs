using System.Text;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Permissiver SAN→UCI-Parser für <b>illegale</b> Diagramm-Stellungen — Chessable-Muster-/Info-Seiten
/// benutzen bewusst regelwidrige Stellungen (z. B. ganz ohne König), die Gera.Chess (Legalitätsprüfung
/// in <c>LoadFromFen</c>/<c>Move</c>) ablehnt, sodass <see cref="PgnParser.TryExtractUciMainline"/> für
/// sie <c>null</c> liefert und die Demonstrationszüge verloren gehen.
/// <para>Dieser Parser arbeitet rein über Figuren-<b>Geometrie</b> + SAN-Disambiguierung (Datei/Reihe),
/// OHNE Legalität (kein Schach/Fesselung/Rochade-Recht). Er ist ausschließlich für die reinen
/// Durchklick-<c>IsInfoOnly</c>-Linien gedacht (nicht zum Lösen). Unterstützt kurze SAN
/// (<c>Nxe4</c>, <c>Qgg8</c>, <c>Q8h7</c>, <c>e8=Q</c>), lange algebraische Notation (<c>d4-e5</c>,
/// <c>Nc3xb5</c>, <c>Kh8-g8</c>) und Rochade. Nicht auflösbare Züge beenden die Sequenz (Präfix bleibt).</para>
/// </summary>
public static partial class PermissiveSan
{
    // <piece?><from><-|x><to><=P?>  — lange algebraische Notation (From-Feld explizit).
    [GeneratedRegex(@"^([KQRBN]?)([a-h][1-8])[-x]([a-h][1-8])=?([QRBN])?$")]
    private static partial Regex LongAlgRegex();
    // <P><disambigFile?><disambigRank?>x?<toFile><toRank>  — kurze Figuren-SAN.
    [GeneratedRegex(@"^([KQRBN])([a-h]?)([1-8]?)x?([a-h])([1-8])$")]
    private static partial Regex PieceSanRegex();
    // <fromFile>x<toFile><toRank>=P?  — Bauernschlag.
    [GeneratedRegex(@"^([a-h])x([a-h])([1-8])=?([QRBN])?$")]
    private static partial Regex PawnCaptureRegex();
    // <toFile><toRank>=P?  — Bauernzug (Vorstoß).
    [GeneratedRegex(@"^([a-h])([1-8])=?([QRBN])?$")]
    private static partial Regex PawnPushRegex();

    private readonly record struct Mv(int From, int To, char Promo);

    /// <summary>Löst die SAN-Hauptlinie ab der (ggf. illegalen) FEN nach UCI auf. <c>null</c>, wenn die
    /// Brett-FEN unbrauchbar ist oder kein einziger Zug auflösbar war; sonst das (ggf. kürzere) Präfix.</summary>
    public static List<string>? TryResolve(string fen, IReadOnlyList<string> sans)
    {
        var board = LoadBoard(fen);
        if (board == null) return null;
        bool white = SideToMove(fen);
        var uci = new List<string>();
        foreach (var rawSan in sans)
        {
            var san = Clean(rawSan);
            if (san.Length == 0) break;              // "--"/Ergebnis/leer → Sequenz endet hier
            var mv = Resolve(board, white, san);
            if (mv is not Mv m) break;               // nicht auflösbar → Präfix behalten
            Apply(board, m);
            uci.Add(Sq(m.From) + Sq(m.To) + (m.Promo == '\0' ? "" : char.ToLowerInvariant(m.Promo).ToString()));
            white = !white;
        }
        return uci.Count == 0 ? null : uci;
    }

    // ---- SAN-Auflösung -----------------------------------------------------
    private static Mv? Resolve(char[] b, bool white, string san)
    {
        if (san is "O-O" or "0-0") return Castle(b, white, kingside: true);
        if (san is "O-O-O" or "0-0-0") return Castle(b, white, kingside: false);

        var la = LongAlgRegex().Match(san);
        if (la.Success)
        {
            int from = SqIndex(la.Groups[2].Value), to = SqIndex(la.Groups[3].Value);
            return new Mv(from, to, PromoChar(la.Groups[4].Value, white));
        }

        var pc = PawnCaptureRegex().Match(san);
        if (pc.Success)
        {
            int toFile = la_(pc.Groups[2].Value[0]), toRank = pc.Groups[3].Value[0] - '1';
            int fromRank = toRank + (white ? -1 : 1);
            int fromFile = pc.Groups[1].Value[0] - 'a';
            if (fromRank is < 0 or > 7) return null;
            return new Mv(fromRank * 8 + fromFile, toRank * 8 + toFile, PromoChar(pc.Groups[4].Value, white));
        }

        var pp = PawnPushRegex().Match(san);
        if (pp.Success)
        {
            int file = pp.Groups[1].Value[0] - 'a', toRank = pp.Groups[2].Value[0] - '1';
            char pawn = white ? 'P' : 'p';
            int step = white ? -1 : 1;               // From liegt „hinter" To (aus Sicht der Zugrichtung)
            int r1 = toRank + step;
            if (r1 is >= 0 and <= 7 && b[r1 * 8 + file] == pawn) return new Mv(r1 * 8 + file, toRank * 8 + file, PromoChar(pp.Groups[3].Value, white));
            int r2 = toRank + 2 * step;
            if (r2 is >= 0 and <= 7 && b[r2 * 8 + file] == pawn) return new Mv(r2 * 8 + file, toRank * 8 + file, '\0');
            return null;
        }

        var ps = PieceSanRegex().Match(san);
        if (ps.Success)
        {
            char type = ps.Groups[1].Value[0];
            char piece = white ? type : char.ToLowerInvariant(type);
            int? dFile = ps.Groups[2].Value.Length > 0 ? ps.Groups[2].Value[0] - 'a' : null;
            int? dRank = ps.Groups[3].Value.Length > 0 ? ps.Groups[3].Value[0] - '1' : null;
            int to = (ps.Groups[5].Value[0] - '1') * 8 + (ps.Groups[4].Value[0] - 'a');
            int? from = null;
            for (int s = 0; s < 64; s++)
            {
                if (b[s] != piece) continue;
                if (dFile is int df && s % 8 != df) continue;
                if (dRank is int dr && s / 8 != dr) continue;
                if (!Reaches(b, type, s, to)) continue;
                from = s; break;
            }
            return from is int f ? new Mv(f, to, '\0') : null;
        }
        return null;
    }

    private static Mv? Castle(char[] b, bool white, bool kingside)
    {
        int rank = white ? 0 : 7;
        int kFrom = rank * 8 + 4;                    // e1/e8 (auch wenn kein König vorhanden — Anzeige-Only)
        int kTo = rank * 8 + (kingside ? 6 : 2);
        return new Mv(kFrom, kTo, '\0');
    }

    // ---- Geometrie (pseudo-legal, ohne Schach/Fesselung) -------------------
    private static bool Reaches(char[] b, char type, int from, int to)
    {
        int fr = from / 8, ff = from % 8, tr = to / 8, tf = to % 8;
        int dr = tr - fr, df = tf - ff;
        switch (char.ToUpperInvariant(type))
        {
            case 'N': return (Math.Abs(dr), Math.Abs(df)) is (1, 2) or (2, 1);
            case 'K': return Math.Abs(dr) <= 1 && Math.Abs(df) <= 1 && (dr != 0 || df != 0);
            case 'R': return (dr == 0 || df == 0) && PathClear(b, fr, ff, tr, tf);
            case 'B': return Math.Abs(dr) == Math.Abs(df) && dr != 0 && PathClear(b, fr, ff, tr, tf);
            case 'Q': return (dr == 0 || df == 0 || Math.Abs(dr) == Math.Abs(df)) && (dr != 0 || df != 0) && PathClear(b, fr, ff, tr, tf);
            default: return false;
        }
    }

    private static bool PathClear(char[] b, int fr, int ff, int tr, int tf)
    {
        int sr = Math.Sign(tr - fr), sf = Math.Sign(tf - ff);
        int r = fr + sr, f = ff + sf;
        while (r != tr || f != tf)
        {
            if (b[r * 8 + f] != '\0') return false;
            r += sr; f += sf;
        }
        return true;
    }

    // ---- Anwenden (ohne Legalität) ----------------------------------------
    private static void Apply(char[] b, Mv m)
    {
        char piece = b[m.From];
        b[m.From] = '\0';
        // En passant: Bauer schlägt diagonal auf ein LEERES Feld → geschlagener Bauer steht „daneben".
        if ((piece is 'P' or 'p') && (m.From % 8) != (m.To % 8) && b[m.To] == '\0')
            b[(m.From / 8) * 8 + (m.To % 8)] = '\0';
        // Rochade: König zwei Felder → Turm mitziehen.
        if (piece is 'K' or 'k' && Math.Abs((m.To % 8) - (m.From % 8)) == 2)
        {
            int rank = m.From / 8;
            bool kingside = (m.To % 8) == 6;
            int rookFrom = rank * 8 + (kingside ? 7 : 0), rookTo = rank * 8 + (kingside ? 5 : 3);
            b[rookTo] = b[rookFrom]; b[rookFrom] = '\0';
        }
        b[m.To] = m.Promo == '\0' ? piece : (char.IsUpper(piece) ? char.ToUpperInvariant(m.Promo) : char.ToLowerInvariant(m.Promo));
    }

    // ---- FEN / Hilfen ------------------------------------------------------
    /// <summary>Brett (Feld 0 der FEN) in ein 64er-Array (Index = Reihe*8+Datei, Reihe 0 = 1. Reihe).
    /// <c>null</c> bei unbrauchbarem Brettfeld.</summary>
    private static char[]? LoadBoard(string fen)
    {
        var board = new char[64];
        var rows = (fen.Split(' ', 2)[0]).Split('/');
        if (rows.Length != 8) return null;
        for (int i = 0; i < 8; i++)
        {
            int rank = 7 - i, file = 0;             // erste PGN-Reihe = 8. Reihe (Index 7)
            foreach (char c in rows[i])
            {
                if (char.IsDigit(c)) file += c - '0';
                else if ("KQRBNPkqrbnp".IndexOf(c) >= 0) { if (file > 7) return null; board[rank * 8 + file] = c; file++; }
                else return null;
            }
            if (file != 8) return null;
        }
        return board;
    }

    private static bool SideToMove(string fen)
    {
        var parts = fen.Split(' ');
        return parts.Length < 2 || parts[1] != "b";  // Default Weiß
    }

    private static string Clean(string token)
    {
        var t = token.Trim().TrimEnd('!', '?', '+', '#');
        if (t is "--" or "*" or "1-0" or "0-1" or "1/2-1/2" or "1/2") return "";
        return t;
    }

    private static char PromoChar(string g, bool white) =>
        g.Length == 0 ? '\0' : (white ? char.ToUpperInvariant(g[0]) : char.ToLowerInvariant(g[0]));

    private static int la_(char file) => file - 'a';
    private static int SqIndex(string sq) => (sq[1] - '1') * 8 + (sq[0] - 'a');
    private static string Sq(int i) => $"{(char)('a' + i % 8)}{(char)('1' + i / 8)}";
}
