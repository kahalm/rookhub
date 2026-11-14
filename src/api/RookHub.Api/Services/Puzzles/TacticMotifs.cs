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
/// <para><b>Partie-Erkennung</b> (0.976.0, <see cref="DetectInGame"/>): für die Engine-Linien einer Partie gelten dieselben Regeln,
/// dazu vier Erweiterungen, die die Signaturen der Aufgaben NICHT berühren (deshalb bleibt <see cref="Version"/>): das Motiv darf nach
/// forcierender Einleitung (Schach oder Schlagen) auch erst im zweiten oder dritten eigenen Zug stehen; eine Gabel gilt auch als
/// eingelöst, wenn eine gegabelte Figur sich verkauft und dort geschlagen wird (Desperado); die hängende Figur zählt nach
/// Materialbilanz statt nach Linienlänge (Engine-Linien sind immer lang); und die <b>Grundreihe</b> (<see cref="BackRank"/>):
/// Turm- oder Damenschach auf der Grundreihe eines Königs ohne Zug, das Material gewinnt.</para>
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

    /// <summary>Grundreihe (0.976.0, nur <see cref="DetectInGame"/>): Schach von Turm oder Dame auf der Grundreihe des Königs, der
    /// keinen Zug hat, und die Linie gewinnt Material. Lichess kennt das Thema nur als Matt (<c>backRankMate</c>) — unter diesem
    /// Namen läuft es hier, damit die Aufgaben des Lichess-Themas dazu passen. Ohne eigene Signatur-Zeilen (<see cref="HasSignatures"/>).</summary>
    public const string BackRank = "backRankMate";

    /// <summary>So viele Halbzüge einer Engine-Linie sieht die Partie-Erkennung an (0.976.0: 8 statt 6 — Beleg Vereinspartie 245,
    /// 26.Txc8+ Kxc8 27.Tc1+ Kd8 28.Sf7+ Kd7 29.Sxh6: die Gabel steht im 5. Halbzug und wird im 7. eingelöst).</summary>
    public const int GamePlies = 8;
    /// <summary>Umwandlungen zählen in der Partie weiter nur in den ersten sechs Halbzügen (Stand vor 0.976.0).</summary>
    private const int GamePromotionPlies = 6;
    /// <summary>Ab diesem Materialgewinn (Bauerneinheiten) über die angesehene Linie zählt eine hängende Figur bzw. die Grundreihe.</summary>
    public const int MinGain = 2;

    /// <summary>Reihenfolge der Partie-Motive (wichtigstes zuerst).</summary>
    private static readonly string[] GameOrder =
        ["doubleCheck", "fork", "skewer", "pin", "discoveredAttack", BackRank, "hangingPiece", "promotion"];

    /// <summary>Gibt es zu diesem Thema Signatur-Zeilen der Aufgaben und Buchdiagramme? (Die Grundreihe nicht — dort trägt das Lichess-Thema.)</summary>
    public static bool HasSignatures(string theme) => Array.IndexOf(Themes, theme) >= 0;

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

    internal static int Value(char p) => char.ToLowerInvariant(p) switch
    {
        'p' => 1, 'n' => 3, 'b' => 3, 'r' => 5, 'q' => 9, 'k' => 100, _ => 0,
    };

    private static char Up(char p) => char.ToUpperInvariant(p);

    internal static char[] Grid(TacticsBoard b)
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
        try { return TacticsBoard.TryParse(fen) is { } b ? DetectCore(b, line, game: false, null, int.MaxValue, 0) : []; }
        catch (Exception) { return []; }
    }

    /// <summary>
    /// Motive einer Engine-Linie der PARTIE (0.976.0; Regeln in der Klassenbeschreibung): die ersten <see cref="GamePlies"/> Halbzüge,
    /// das Motiv am ersten eigenen Zug — oder, wenn dort keines steht und die eigenen Züge davor Schach geben oder schlagen, am
    /// zweiten bzw. dritten. Wirft nie.
    /// </summary>
    /// <param name="recapture">Hat der Zug VOR dieser Stellung auf einem Feld geschlagen: Feld und Wert der dort geschlagenen Figur —
    /// ein Zurückschlagen ist keine hängende Figur, außer es gewinnt mindestens <see cref="MinGain"/> mehr, als dort verloren ging.</param>
    public static List<Motif> DetectInGame(string fen, IReadOnlyList<string> line, (int Square, int Value)? recapture = null)
    {
        try
        {
            var pv = line.Take(GamePlies).ToList();
            var first = TacticsBoard.TryParse(fen);
            var b = first;
            for (var start = 0; start <= 4 && start < pv.Count && b is not null && first is not null; start += 2)
            {
                // Was die Einleitung gekostet oder gebracht hat, zählt in der Bilanz mit (ein Opfer vorweg ist kein Gewinn).
                var found = DetectCore(b, pv.Skip(start).ToList(), game: true, recapture, GamePromotionPlies - start, NetGain(first, pv, start));
                if (found.Count > 0) return found;
                // Weiter nur nach forcierender Einleitung: der eigene Zug schlägt oder gibt Schach, dann die Antwort der Linie.
                if (start + 1 >= pv.Count || pv[start].Length < 4 || TacticsBoard.SquareIndex(pv[start][2..4]) is not int to) break;
                var captures = b.At(to) is char c && TacticsBoard.IsWhite(c) != b.WhiteToMove;
                if (b.Apply(pv[start]) is not { } after || (!captures && !after.InCheck())) break;
                // Schlägt die Antwort, ist der nächste eigene Schlag auf demselben Feld ein Zurückschlagen.
                var reply = pv[start + 1];
                recapture = reply.Length >= 4 && TacticsBoard.SquareIndex(reply[2..4]) is int rt && after.At(rt) is char taken
                    && TacticsBoard.IsWhite(taken) != after.WhiteToMove ? (rt, Value(taken)) : null;
                b = after.Apply(reply);
            }
            return [];
        }
        catch (Exception) { return []; }
    }

    /// <summary>
    /// Materialbilanz der ersten <paramref name="plies"/> Halbzüge von <paramref name="line"/> aus Sicht der Seite am Zug
    /// (Bauerneinheiten; Schlagen, en passant, Umwandlung). Bricht an einem unspielbaren Zug ab.
    /// </summary>
    internal static int NetGain(TacticsBoard start, IReadOnlyList<string> line, int plies)
    {
        var b = start;
        var net = 0;
        for (var i = 0; i < line.Count && i < plies; i++)
        {
            var u = line[i];
            if (u.Length < 4 || TacticsBoard.SquareIndex(u[..2]) is not int from || TacticsBoard.SquareIndex(u[2..4]) is not int to
                || b.At(from) is not char p) break;
            var gain = 0;
            if (b.At(to) is char c) { if (TacticsBoard.IsWhite(c) != TacticsBoard.IsWhite(p)) gain += Value(c); }   // sonst Rochade als König-schlägt-Turm
            else if (Up(p) == 'P' && from % 8 != to % 8) gain += 1;                                                  // en passant
            if (u.Length >= 5 && Up(p) == 'P') gain += Math.Max(0, Value(u[4]) - 1);
            net += TacticsBoard.IsWhite(p) == start.WhiteToMove ? gain : -gain;
            if (b.Apply(u) is not { } next) break;
            b = next;
        }
        return net;
    }

    /// <param name="baseGain">Partie: Materialbilanz der Einleitung vor dieser Stellung (Sicht des Lösers).</param>
    private static List<Motif> DetectCore(TacticsBoard b0, IReadOnlyList<string> line, bool game, (int Square, int Value)? recapture, int promotionPlies,
        int baseGain)
    {
        var result = new List<Motif>();
        if (line.Count == 0) return result;
        var white = b0.WhiteToMove;
        var g0 = Grid(b0);
        var uci = line[0];
        if (uci.Length < 4 || TacticsBoard.SquareIndex(uci[..2]) is not int from || TacticsBoard.SquareIndex(uci[2..4]) is not int to) return result;
        var moved = g0[from];
        if (!Mine(moved, white)) return result;
        var captured = g0[to];
        if (b0.Apply(uci) is not { } b1) return result;
        var g1 = Grid(b1);
        var piece = g1[to];                       // nach einer Umwandlung die neue Figur
        var enemyKing = Array.IndexOf(g1, white ? 'k' : 'K');
        // Wohin der NÄCHSTE eigene Zug der Lösung zieht — die Motive gelten erst, wenn die Lösung das Ziel auch holt.
        int? nextTo = line.Count >= 3 && line[2].Length >= 4 ? TacticsBoard.SquareIndex(line[2][2..4]) : null;
        // Partie (0.976.0): die Antwort der Linie — verkauft sich eine gegabelte Figur (Desperado) und wird dort geschlagen, ist die
        // Gabel ebenfalls eingelöst (Vereinspartie 251: 37.Sd6 Txg4+ 38.Kxg4, der andere Turm zieht weg).
        int? replyFrom = game && line.Count >= 3 && line[1].Length >= 4 ? TacticsBoard.SquareIndex(line[1][..2]) : null;
        int? replyTo = game && line.Count >= 3 && line[1].Length >= 4 ? TacticsBoard.SquareIndex(line[1][2..4]) : null;

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
            var cashed = nextTo is int nt && (targets.Contains(nt)
                || (replyFrom is int rf && replyTo == nt && targets.Contains(rf) && Up(g1[rf]) != 'K'));
            if (targets.Count >= 2 && !cheapAttacker && cashed)
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

        // Grundreihe (nur Partie): Turm oder Dame gibt auf der Grundreihe des Königs Schach, der König hat keinen Zug (es bleibt nur
        // Dazwischenziehen oder Schlagen), und die Linie gewinnt Material. Matt ist hier nicht gemeint — das trägt das Mattbild.
        if (game && Up(piece) is 'R' or 'Q' && enemyKing >= 0 && enemyKing / 8 == (white ? 7 : 0) && to / 8 == enemyKing / 8
            && MatePattern.Attackers(g1, enemyKing, white).Contains(to))
        {
            var replies = b1.LegalMoves();
            if (replies.Count > 0 && replies.All(m => Up(m.Piece) != 'K') && baseGain + NetGain(b0, line, GamePlies) >= MinGain)
                result.Add(new Motif(BackRank, $"backRank|{Up(piece)}"));
        }

        // Hängende Figur: kurze Lösung, die mit dem Schlagen einer ungedeckten Figur (ab Leichtfigur) beginnt. In der Partie sind
        // die Engine-Linien immer lang — dort zählt stattdessen die Bilanz: nach der Antwort und am Ende der angesehenen Linie
        // mindestens MinGain im Plus; ein bloßes Zurückschlagen (recapture) nur, wenn es so viel MEHR holt, als dort verloren ging.
        if (captured != '\0' && Theirs(captured, white) && Value(captured) >= 3 && !Defended(g0, to, !white)
            && (game
                ? baseGain + NetGain(b0, line, 2) >= MinGain && baseGain + NetGain(b0, line, GamePlies) >= MinGain
                    && !(recapture is { } r && r.Square == to && Value(captured) - r.Value < MinGain)
                : line.Count <= 3))
            result.Add(new Motif("hangingPiece", $"hangingPiece|{Up(moved)}|{Up(captured)}"));

        // Umwandlung: irgendein Zug des Lösers wandelt um.
        for (var i = 0; i < line.Count && i < promotionPlies; i += 2)
            if (line[i].Length == 5) { result.Add(new Motif("promotion", $"promotion|{char.ToUpperInvariant(line[i][4])}")); break; }

        return result.GroupBy(m => m.Theme).Select(g => g.First())
            .OrderBy(m => Array.IndexOf(GameOrder, m.Theme)).ToList();
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
    internal static List<int> AttackedBy(char[] g, int sq)
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
