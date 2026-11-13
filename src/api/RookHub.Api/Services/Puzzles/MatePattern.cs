using System.Text;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.Puzzles;

/// <summary>
/// Mattbild einer Schlussstellung (0.960.0, Empfohlene Übungen der Partie-Zusammenfassung; Wunsch 2026-11-13: „M2/M3 mit so
/// motiven — Df7 Kf6 und Bauer auf f4, aber kann dann natürlich auch ein Springer sein, der das deckt“). Rein, ohne Datenbank,
/// über <see cref="TacticsBoard"/> (dasselbe kleine Brett wie die Zug-Klassen).
///
/// <para><b>Beschreibung relativ zum mattgesetzten König</b>, farbunabhängig (die Stellung wird so gedreht, dass der Mattgesetzte
/// „oben“ steht — Bauernrichtung bleibt richtig) und spiegelunabhängig (a↔h; von beiden Spiegelbildern zählt die kleinere
/// Zeichenkette). Bestandteile:</para>
/// <list type="bullet">
/// <item><b>Lage</b> des Königs: <c>corner</c>, <c>back</c> (eigene Grundreihe), <c>far</c> (gegnerische Grundreihe), <c>side</c>
/// (a-/h-Linie), sonst <c>open</c>.</item>
/// <item><b>Schachgebende Figur(en)</b>: Typ und Lage — streng mit Versatz (dx, dy), Familie nur Kontakt diagonal/gerade
/// (<c>cd</c>/<c>co</c>), Linie diagonal/gerade (<c>ld</c>/<c>lo</c>), Springer (<c>n</c>), Bauer (<c>p</c>). Bei Kontakt: wodurch
/// sie gedeckt ist — streng die Typen der Deckung, Familie nur gedeckt (<c>+</c>) oder nicht (<c>-</c>).</item>
/// <item><b>Die acht Nachbarfelder</b> in fester Reihenfolge: Rand <c>#</c>, schachgebende Figur <c>X</c>, eigener Stein <c>o</c>,
/// sonst vom Angreifer beherrscht — streng die Typen der beherrschenden Figuren (König dabei vom Brett genommen, damit eine Linie
/// durch ihn hindurch zählt; eine dort stehende Angreiferfigur mit vorangestelltem <c>x</c>), Familie nur <c>a</c>.</item>
/// </list>
/// Die Familie lässt also genau die Figurentypen der Deckung weg: „Dame g7 gedeckt vom Bauern f6“ und „… vom Springer h5“ sind
/// dieselbe Familie, aber verschiedene strenge Signaturen.
/// </summary>
public static class MatePattern
{
    /// <summary>Regelstand. Wer die Beschreibung ändert, zählt hoch — der Hintergrundlauf rechnet dann alles neu.</summary>
    public const short Version = 1;

    public sealed record Signature(string Strict, string Family)
    {
        public long StrictHash => Hash(Strict);
        public long FamilyHash => Hash(Family);
    }

    /// <summary>Schlussstellung einer Lichess-Aufgabe (FEN vor dem Zug des Gegners, dann ALLE Züge) bzw. einer Engine-Linie;
    /// <c>null</c>, wenn ein Zug nicht geht.</summary>
    public static TacticsBoard? Play(string fen, IEnumerable<string> ucis)
    {
        var b = TacticsBoard.TryParse(fen);
        foreach (var u in ucis)
        {
            if (b is null) return null;
            b = b.Apply(u);
        }
        return b;
    }

    /// <summary>Ist die Seite am Zug matt?</summary>
    public static bool IsMate(TacticsBoard b) => b.InCheck() && b.LegalMoves().Count == 0;

    /// <summary>Signatur einer Lichess-Aufgabe; <c>null</c>, wenn sie nicht mit Matt endet.</summary>
    public static Signature? OfPuzzle(string fen, string moves) =>
        Play(fen, moves.Split(' ', StringSplitOptions.RemoveEmptyEntries)) is { } b ? Of(b) : null;

    /// <summary>Signatur der Stellung, wenn die Seite am Zug matt ist; sonst <c>null</c>. Wirft nie.</summary>
    public static Signature? Of(TacticsBoard board)
    {
        try
        {
            if (!IsMate(board)) return null;
            var grid = Normalize(board);
            var a = Describe(grid);
            var b = Describe(Mirror(grid));
            if (a is null || b is null) return null;
            return new Signature(Min(a.Value.Strict, b.Value.Strict), Min(a.Value.Family, b.Value.Family));
        }
        catch (Exception) { return null; }
    }

    private static string Min(string x, string y) => string.CompareOrdinal(x, y) <= 0 ? x : y;

    /// <summary>64-Bit FNV-1a der UTF-8-Bytes — stabil über Läufe und Rechner (anders als <c>string.GetHashCode</c>).</summary>
    public static long Hash(string s)
    {
        unchecked
        {
            var h = 14695981039346656037UL;
            foreach (var by in Encoding.UTF8.GetBytes(s)) { h ^= by; h *= 1099511628211UL; }
            return (long)h;
        }
    }

    /// <summary>Brett so, dass die mattgesetzte Seite Schwarz ist (klein), der Angreifer Weiß (groß).</summary>
    private static char[] Normalize(TacticsBoard board)
    {
        var g = new char[64];
        var flip = board.WhiteToMove;   // Weiß ist matt → senkrecht drehen und Farben tauschen
        for (var sq = 0; sq < 64; sq++)
        {
            if (board.At(sq) is not char p) continue;
            if (!flip) { g[sq] = p; continue; }
            var to = (7 - sq / 8) * 8 + sq % 8;
            g[to] = char.IsUpper(p) ? char.ToLowerInvariant(p) : char.ToUpperInvariant(p);
        }
        return g;
    }

    private static char[] Mirror(char[] g)
    {
        var m = new char[64];
        for (var sq = 0; sq < 64; sq++) m[sq / 8 * 8 + (7 - sq % 8)] = g[sq];
        return m;
    }

    private static readonly (int Dx, int Dy)[] Around = [(-1, 1), (0, 1), (1, 1), (-1, 0), (1, 0), (-1, -1), (0, -1), (1, -1)];

    private static (string Strict, string Family)? Describe(char[] g)
    {
        var k = Array.IndexOf(g, 'k');
        if (k < 0) return null;
        int kf = k % 8, kr = k / 8;
        var back = kr == 7; var far = kr == 0; var side = kf is 0 or 7;
        var zone = (back || far) && side ? "corner" : back ? "back" : far ? "far" : side ? "side" : "open";

        var checkers = Attackers(g, k, white: true);
        if (checkers.Count == 0) return null;
        var cs = new List<string>(); var cf = new List<string>();
        foreach (var c in checkers)
        {
            var t = g[c];
            int dx = c % 8 - kf, dy = c / 8 - kr;
            var contact = Math.Max(Math.Abs(dx), Math.Abs(dy)) == 1;
            var rel = t == 'N' ? "n" : t == 'P' ? "p" : contact ? (dx != 0 && dy != 0 ? "cd" : "co") : dx != 0 && dy != 0 ? "ld" : "lo";
            var s = $"{t}{dx:+0;-0;0}{dy:+0;-0;0}";
            var f = $"{t}{rel}";
            if (contact)
            {
                var defenders = Types(g, Attackers(g, c, white: true).Where(x => x != c));
                s += "/" + (defenders.Length == 0 ? "-" : defenders);
                f += defenders.Length == 0 ? "-" : "+";
            }
            cs.Add(s); cf.Add(f);
        }
        cs.Sort(StringComparer.Ordinal); cf.Sort(StringComparer.Ordinal);

        var without = (char[])g.Clone();
        without[k] = '\0';
        var cellsS = new List<string>(); var cellsF = new List<string>();
        foreach (var (dx, dy) in Around)
        {
            int f = kf + dx, r = kr + dy;
            if (f is < 0 or > 7 || r is < 0 or > 7) { cellsS.Add("#"); cellsF.Add("#"); continue; }
            var sq = r * 8 + f;
            if (checkers.Contains(sq)) { cellsS.Add("X"); cellsF.Add("X"); continue; }
            var p = g[sq];
            if (p != '\0' && char.IsLower(p)) { cellsS.Add("o"); cellsF.Add("o"); continue; }
            var types = Types(without, Attackers(without, sq, white: true).Where(x => x != sq));
            var mark = p != '\0' ? "x" : "";
            cellsS.Add(types.Length == 0 ? mark + "?" : mark + types);
            cellsF.Add(types.Length == 0 ? "?" : "a");
        }
        var strict = $"M{Version}|{zone}|{string.Join(',', cs)}|{string.Join(',', cellsS)}";
        var family = $"MF{Version}|{zone}|{string.Join(',', cf)}|{string.Join(',', cellsF)}";
        return (strict, family);
    }

    /// <summary>Typen der Figuren auf <paramref name="squares"/>, sortiert, je Typ einmal (<c>"NP"</c>).</summary>
    private static string Types(char[] g, IEnumerable<int> squares) =>
        new string(squares.Select(s => char.ToUpperInvariant(g[s])).Distinct().OrderBy(c => c).ToArray());

    private static readonly (int Df, int Dr)[] Knight = [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];
    private static readonly (int Df, int Dr)[] Kings = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];
    private static readonly (int Df, int Dr)[] Diag = [(1, 1), (-1, 1), (-1, -1), (1, -1)];
    private static readonly (int Df, int Dr)[] Orth = [(1, 0), (0, 1), (-1, 0), (0, -1)];

    private static int? Step(int sq, int df, int dr)
    {
        int f = sq % 8 + df, r = sq / 8 + dr;
        return f is >= 0 and < 8 && r is >= 0 and < 8 ? r * 8 + f : null;
    }

    /// <summary>Felder der Farbe <paramref name="white"/>, die <paramref name="sq"/> angreifen (Pseudo-Angriffe, ohne Röntgen).</summary>
    internal static List<int> Attackers(char[] g, int sq, bool white)
    {
        var res = new List<int>();
        char P = white ? 'P' : 'p', N = white ? 'N' : 'n', K = white ? 'K' : 'k', B = white ? 'B' : 'b', R = white ? 'R' : 'r', Q = white ? 'Q' : 'q';
        foreach (var df in new[] { -1, 1 })
            if (Step(sq, df, white ? -1 : 1) is int p && g[p] == P) res.Add(p);
        foreach (var (df, dr) in Knight) if (Step(sq, df, dr) is int n && g[n] == N) res.Add(n);
        foreach (var (df, dr) in Kings) if (Step(sq, df, dr) is int k && g[k] == K) res.Add(k);
        Slide(g, sq, Diag, B, Q, res);
        Slide(g, sq, Orth, R, Q, res);
        return res;
    }

    private static void Slide(char[] g, int sq, (int Df, int Dr)[] dirs, char slider, char queen, List<int> into)
    {
        foreach (var (df, dr) in dirs)
        {
            var cur = sq;
            while (Step(cur, df, dr) is int next)
            {
                cur = next;
                if (g[cur] == '\0') continue;
                if (g[cur] == slider || g[cur] == queen) into.Add(cur);
                break;
            }
        }
    }
}
