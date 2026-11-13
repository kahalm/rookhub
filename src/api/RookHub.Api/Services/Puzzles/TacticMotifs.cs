using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.Puzzles;

/// <summary>
/// Motiv-Erkenner für taktische Stellen (0.961.0, Empfohlene Übungen; Wunsch 2026-11-13: „auch andere Fehler taktischer Natur“). Liefert
/// dieselben Themen-Namen wie die Lichess-Aufgaben (<c>fork</c>, <c>pin</c>, <c>skewer</c>, <c>discoveredAttack</c>, <c>doubleCheck</c>,
/// <c>hangingPiece</c>, <c>promotion</c>), aber als EIGENE Umsetzung nach den
/// dokumentierten Themen-Definitionen (lichess.org/training/themes) — der Lichess-Tagger (<c>lichess-puzzler/tagger/cook.py</c>) steht
/// unter AGPL-3.0 und ist nicht kopiert. Bewusst nur, was sich sicher am ersten Zug der Lösung ablesen lässt UND (bei Gabel, Spieß,
/// Fesselung, Abzug) vom nächsten eigenen Zug der Lösung eingelöst wird; unscharfe Themen (Ablenkung, Hinlenkung, Verstellung, Räumung,
/// stiller Zug, Zugzwang, gefangene Figur) erkennt er nicht — lieber keine Übung als eine falsche („erzwinge keine motive“).
/// <c>capturingDefender</c> und <c>advancedPawn</c> waren angelegt, stimmten an 51 714 Prod-Aufgaben aber nur zu 23 % bzw. 55 % mit dem
/// Lichess-Thema überein und sind deshalb wieder raus; gemessen (13.11.): Gabel 87 %, Abzug 93 %, Doppelschach 100 %, Umwandlung 99 %,
/// Spieß 66 %, Fesselung 61 %, hängende Figur 61 % (Lichess etikettiert dort enger; die Regel selbst ist eindeutig).
///
/// <para>Je Motiv eine <b>strenge</b> Signatur (Thema + ziehende Figur + Zielfiguren, z. B. <c>fork|N|KR</c> = Springergabel König +
/// Turm) und die <b>Familie</b> = das Thema allein. Rein, ohne Datenbank, über <see cref="TacticsBoard"/>; wirft nie.</para>
/// </summary>
public static class TacticMotifs
{
    /// <summary>Regelstand; wer eine Regel ändert, zählt hoch (der Hintergrundlauf rechnet dann neu).</summary>
    public const short Version = 1;

    public sealed record Motif(string Theme, string Strict)
    {
        public string Family => $"T{Version}|{Theme}";
        public long StrictHash => MatePattern.Hash($"T{Version}|{Strict}");
        public long FamilyHash => MatePattern.Hash(Family);
    }

    /// <summary>Alle erkannten Themen, in fester Reihenfolge (wichtigstes zuerst).</summary>
    public static readonly string[] Themes =
        ["doubleCheck", "fork", "skewer", "pin", "discoveredAttack", "hangingPiece", "promotion"];

    /// <summary>Art der Signatur-Zeile je Thema.</summary>
    public static Models.PuzzleSignatureKind KindOf(string theme) => theme switch
    {
        "doubleCheck" => Models.PuzzleSignatureKind.DoubleCheck,
        "fork" => Models.PuzzleSignatureKind.Fork,
        "skewer" => Models.PuzzleSignatureKind.Skewer,
        "pin" => Models.PuzzleSignatureKind.Pin,
        "discoveredAttack" => Models.PuzzleSignatureKind.DiscoveredAttack,
        "hangingPiece" => Models.PuzzleSignatureKind.HangingPiece,
        _ => Models.PuzzleSignatureKind.Promotion,
    };

    private static int Value(char p) => char.ToLowerInvariant(p) switch
    {
        'p' => 1, 'n' => 3, 'b' => 3, 'r' => 5, 'q' => 9, 'k' => 100, _ => 0,
    };

    private static char Up(char p) => char.ToUpperInvariant(p);

    private static char[] Grid(TacticsBoard b)
    {
        var g = new char[64];
        for (var sq = 0; sq < 64; sq++) if (b.At(sq) is char p) g[sq] = p;
        return g;
    }

    private static bool Mine(char p, bool white) => p != '\0' && char.IsUpper(p) == white;
    private static bool Theirs(char p, bool white) => p != '\0' && char.IsUpper(p) != white;

    /// <summary>Motive einer Lichess-Aufgabe: <paramref name="fen"/> VOR dem Zug des Gegners, <paramref name="moves"/> = Gegnerzug + Lösung.</summary>
    public static List<Motif> OfPuzzle(string fen, string moves)
    {
        var list = moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (list.Length < 2) return [];
        var start = TacticsBoard.TryParse(fen)?.Apply(list[0]);
        return start is null ? [] : Detect(start.ToFen(), list.Skip(1).ToList());
    }

    /// <summary>Motive der Lösung <paramref name="line"/> (Löser zuerst, abwechselnd) ab <paramref name="fen"/> (Löser am Zug).</summary>
    public static List<Motif> Detect(string fen, IReadOnlyList<string> line)
    {
        try { return DetectCore(fen, line); }
        catch (Exception) { return []; }
    }

    private static List<Motif> DetectCore(string fen, IReadOnlyList<string> line)
    {
        var result = new List<Motif>();
        if (line.Count == 0 || TacticsBoard.TryParse(fen) is not { } b0) return result;
        var white = b0.WhiteToMove;
        var g0 = Grid(b0);
        var uci = line[0];
        if (TacticsBoard.SquareIndex(uci[..2]) is not int from || TacticsBoard.SquareIndex(uci[2..4]) is not int to) return result;
        var moved = g0[from];
        if (!Mine(moved, white)) return result;
        var captured = g0[to];
        if (b0.Apply(uci) is not { } b1) return result;
        var g1 = Grid(b1);
        var piece = g1[to];                       // nach einer Umwandlung die neue Figur
        var enemyKing = Array.IndexOf(g1, white ? 'k' : 'K');
        // Wohin der NÄCHSTE eigene Zug der Lösung zieht — die Motive gelten erst, wenn die Lösung das Ziel auch holt.
        int? nextTo = line.Count >= 3 && line[2].Length >= 4 ? TacticsBoard.SquareIndex(line[2][2..4]) : null;

        // Doppelschach: zwei eigene Figuren geben nach dem Zug Schach.
        if (enemyKing >= 0)
        {
            var checkers = MatePattern.Attackers(g1, enemyKing, white);
            if (checkers.Count >= 2)
                result.Add(new Motif("doubleCheck", $"doubleCheck|{Sorted(checkers.Select(c => Up(g1[c])))}"));
        }

        // Gabel: die gezogene Figur greift danach zwei Ziele an — König, wertvoller als sie selbst, oder ungedeckt — und steht
        // selbst nicht billiger zu schlagen, als sie gewinnt (kein Angreifer kleineren Werts auf ihrem Feld).
        if (Up(piece) != 'K')
        {
            var targets = AttackedBy(g1, to).Where(t => Theirs(g1[t], white)
                && (Up(g1[t]) == 'K' || Value(g1[t]) > Value(piece) || !Defended(g1, t, !white))).ToList();
            var cheapAttacker = MatePattern.Attackers(g1, to, !white).Any(a => Value(g1[a]) < Value(piece));
            if (targets.Count >= 2 && !cheapAttacker && nextTo is int nt && targets.Contains(nt))
                result.Add(new Motif("fork", $"fork|{Up(piece)}|{Sorted(targets.Select(t => Up(g1[t])))}"));
        }

        // Spieß und Fesselung: die gezogene Langschrittlerin trifft auf einer Linie zuerst X, dahinter Y (beide gegnerisch).
        if (Up(piece) is 'B' or 'R' or 'Q')
        {
            foreach (var (x, y) in Lines(g1, to, piece))
            {
                if (!Theirs(g1[x], white) || !Theirs(g1[y], white)) continue;
                var vx = Value(g1[x]); var vy = Value(g1[y]);
                if (Up(g1[x]) is 'K' or 'Q' && vx > vy && nextTo == y)
                    result.Add(new Motif("skewer", $"skewer|{Up(piece)}|{Up(g1[x])}>{Up(g1[y])}"));
                else if (vy > vx && vx >= 3 && Up(g1[x]) != 'K' && Up(g1[y]) is 'K' or 'Q' && nextTo == x)
                    result.Add(new Motif("pin", $"pin|{Up(piece)}|{Up(g1[x])}>{Up(g1[y])}"));
            }
        }

        // Abzugsangriff: die Figur gibt eine Linie frei, auf der jetzt eine eigene Langschrittlerin ein wertvolles Ziel trifft.
        foreach (var s in Enumerable.Range(0, 64).Where(s => s != to && Mine(g1[s], white) && Up(g1[s]) is 'B' or 'R' or 'Q'))
        {
            foreach (var x in FirstHits(g1, s, g1[s]))
            {
                if (!Theirs(g1[x], white) || (Up(g1[x]) != 'K' && Value(g1[x]) < 3)) continue;
                if (!Between(s, x).Contains(from)) continue;               // die gezogene Figur stand dazwischen
                if (Up(g1[x]) != 'K' && nextTo != x) continue;            // Abzugsschach zählt sofort, sonst muss die Lösung das Ziel holen
                result.Add(new Motif("discoveredAttack", $"discoveredAttack|{Up(g1[s])}|{Up(g1[x])}|{Up(moved)}"));
                break;
            }
            if (result.Any(m => m.Theme == "discoveredAttack")) break;
        }

        // Hängende Figur: kurze Lösung, die mit dem Schlagen einer ungedeckten Figur (ab Leichtfigur) beginnt.
        if (captured != '\0' && Theirs(captured, white) && Value(captured) >= 3 && line.Count <= 3 && !Defended(g0, to, !white))
            result.Add(new Motif("hangingPiece", $"hangingPiece|{Up(moved)}|{Up(captured)}"));

        // Umwandlung: irgendein Zug des Lösers wandelt um.
        for (var i = 0; i < line.Count; i += 2)
            if (line[i].Length == 5) { result.Add(new Motif("promotion", $"promotion|{char.ToUpperInvariant(line[i][4])}")); break; }

        return result.GroupBy(m => m.Theme).Select(g => g.First())
            .OrderBy(m => Array.IndexOf(Themes, m.Theme)).ToList();
    }

    private static string Sorted(IEnumerable<char> types) => new(types.OrderBy(c => c).ToArray());

    /// <summary>Gedeckt von Farbe <paramref name="byWhite"/>?</summary>
    private static bool Defended(char[] g, int sq, bool byWhite) => MatePattern.Attackers(g, sq, byWhite).Count > 0;

    private static readonly (int Df, int Dr)[] Diag = [(1, 1), (-1, 1), (-1, -1), (1, -1)];
    private static readonly (int Df, int Dr)[] Orth = [(1, 0), (0, 1), (-1, 0), (0, -1)];
    private static readonly (int Df, int Dr)[] Knight = [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];

    private static int? Step(int sq, int df, int dr)
    {
        int f = sq % 8 + df, r = sq / 8 + dr;
        return f is >= 0 and < 8 && r is >= 0 and < 8 ? r * 8 + f : null;
    }

    private static IEnumerable<(int Df, int Dr)> Dirs(char piece) => Up(piece) switch
    {
        'B' => Diag, 'R' => Orth, 'Q' => Diag.Concat(Orth), _ => [],
    };

    /// <summary>Je Richtung der Langschrittlerin: erstes besetztes Feld X und das nächste besetzte dahinter Y.</summary>
    private static IEnumerable<(int X, int Y)> Lines(char[] g, int sq, char piece)
    {
        foreach (var (df, dr) in Dirs(piece))
        {
            int? x = null; var cur = sq;
            while (Step(cur, df, dr) is int next)
            {
                cur = next;
                if (g[cur] == '\0') continue;
                if (x is null) { x = cur; continue; }
                yield return (x.Value, cur);
                break;
            }
        }
    }

    /// <summary>Je Richtung der Langschrittlerin das erste besetzte Feld.</summary>
    private static IEnumerable<int> FirstHits(char[] g, int sq, char piece)
    {
        foreach (var (df, dr) in Dirs(piece))
        {
            var cur = sq;
            while (Step(cur, df, dr) is int next)
            {
                cur = next;
                if (g[cur] == '\0') continue;
                yield return cur;
                break;
            }
        }
    }

    /// <summary>Felder, die die Figur auf <paramref name="sq"/> angreift (Pseudo-Angriffe).</summary>
    private static List<int> AttackedBy(char[] g, int sq)
    {
        var p = g[sq];
        var white = char.IsUpper(p);
        var res = new List<int>();
        switch (Up(p))
        {
            case 'P':
                foreach (var df in new[] { -1, 1 }) if (Step(sq, df, white ? 1 : -1) is int t) res.Add(t);
                break;
            case 'N':
                foreach (var (df, dr) in Knight) if (Step(sq, df, dr) is int t) res.Add(t);
                break;
            case 'K':
                foreach (var (df, dr) in Diag.Concat(Orth)) if (Step(sq, df, dr) is int t) res.Add(t);
                break;
            default:
                foreach (var (df, dr) in Dirs(p))
                {
                    var cur = sq;
                    while (Step(cur, df, dr) is int next) { cur = next; res.Add(cur); if (g[cur] != '\0') break; }
                }
                break;
        }
        return res;
    }

    /// <summary>Felder echt zwischen zwei Feldern auf einer Linie (leer, wenn sie keine gemeinsame Linie haben).</summary>
    private static List<int> Between(int a, int b)
    {
        int df = Math.Sign(b % 8 - a % 8), dr = Math.Sign(b / 8 - a / 8);
        int fd = Math.Abs(b % 8 - a % 8), rd = Math.Abs(b / 8 - a / 8);
        var res = new List<int>();
        if (!(fd == 0 || rd == 0 || fd == rd)) return res;
        var cur = a;
        while (Step(cur, df, dr) is int next && next != b) { res.Add(next); cur = next; }
        return res;
    }
}
