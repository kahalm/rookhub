using Chess;

namespace RookHub.Api.Services;

/// <summary>
/// Sucht die ZÜGE ZWISCHEN zwei bekannten Stellungen — der zweite Schritt der Rekonstruktion:
/// „hier stand das, später stand das, was war dazwischen?"
///
/// <para>Das ist das klassische Beweispartie-Problem und im Allgemeinen teuer: bei ~30 Zügen je
/// Halbzug wächst der Baum mit 30^n. Deshalb drei Dinge:</para>
/// <list type="number">
///   <item><b>Iterative Vertiefung</b> — die kürzeste Lösung zuerst; eine Lücke von zwei Halbzügen
///     kostet dann auch nur zwei Halbzüge Suche.</item>
///   <item><b>Zulässige untere Schranke</b> (<see cref="MinPlies"/>): geschlagene Figuren kann nur
///     die andere Seite wegnehmen, jede Umwandlung kostet einen eigenen Zug, und ein Halbzug ändert
///     höchstens vier Felder (Rochade). Liegt die Schranke über der Resttiefe, ist der ganze Ast tot.</item>
///   <item><b>Totstellungs-Gedächtnis</b>: ein vollständig durchsuchter Knoten ohne Treffer wird
///     gemerkt (Stellung + Resttiefe) — Zugumstellungen führen sonst immer wieder dorthin.</item>
/// </list>
///
/// <para><b>Was der Löser NICHT tut: raten.</b> Er liefert alle gefundenen Wege (bis
/// <c>maxSolutions</c>) und sagt, ob er den Raum ausgeschöpft hat. Mehrere Wege sind der Normalfall
/// — welcher gespielt wurde, weiß nur der Mensch. Und wenn das Budget nicht reichte, heißt das
/// „nicht gefunden", nicht „gibt es nicht"; genau diese Unterscheidung trägt
/// <see cref="Result.BudgetExhausted"/>.</para>
/// </summary>
public static class GapSolver
{
    /// <summary>Weiter als so darf keine Anfrage suchen (der Baum wächst exponentiell).</summary>
    public const int MaxSearchPlies = 12;

    /// <summary>Vorgabe, wenn der Aufrufer nichts sagt.</summary>
    public const int DefaultMaxPlies = 4;

    /// <summary>So viele Knoten höchstens — danach bricht die Suche ehrlich ab.</summary>
    public const int DefaultNodeBudget = 3_000_000;

    /// <summary>Und so lange höchstens: eine Anfrage darf keinen Request-Thread festhalten.</summary>
    public static readonly TimeSpan DefaultTimeBudget = TimeSpan.FromSeconds(12);

    /// <summary>Ein gefundener Weg.</summary>
    public record Solution(string San, int Plies);

    /// <summary>
    /// Das Ergebnis einer Suche.
    /// </summary>
    /// <param name="Solutions">Gefundene Wege, kürzeste zuerst.</param>
    /// <param name="Nodes">Besuchte Stellungen (fürs Protokoll und die Anzeige).</param>
    /// <param name="BudgetExhausted">Die Suche wurde abgebrochen — „nicht gefunden" ≠ „gibt es nicht".</param>
    /// <param name="Reason">Warum es keine Lösung gibt, wenn die Liste leer ist.</param>
    /// <param name="DeepestSearched">Bis zu wie vielen Halbzügen wurde wirklich gesucht? Bei einem
    /// Abbruch am Budget ist das die ehrliche Auskunft „so weit kam ich".</param>
    public record Result(IReadOnlyList<Solution> Solutions, int Nodes, bool BudgetExhausted, string? Reason,
        int DeepestSearched = 0);

    /// <summary>Stellung ohne Zähler (Brett, Seite am Zug, Rochade, en passant) — der Schlüssel fürs
    /// Totstellungs-Gedächtnis. Zum VERGLEICHEN mit der Zielstellung dient <see cref="Matches"/>.</summary>
    public static string PositionKey(string fen)
    {
        var f = fen.Split(' ');
        return f.Length < 4 ? fen.Trim() : string.Join(' ', f[0], f[1], f[2], f[3]);
    }

    /// <summary>
    /// Ist <paramref name="fen"/> die gesuchte Stellung <paramref name="target"/>? Verglichen werden
    /// Brett, Seite am Zug und Rochaderechte.
    ///
    /// <para><b>Das en-passant-Feld zählt nur, wenn BEIDE Seiten eines nennen.</b> Der
    /// Stellungs-Editor schreibt dort immer „-" (er kennt den Zug davor nicht), die Zug-Erzeugung
    /// dagegen setzt nach jedem Doppelschritt eines („e6"). Verlangte man Gleichheit, fiele genau
    /// die häufigste Erinnerung durch das Raster: „und dann ging der Bauer nach e5" — jede Lösung,
    /// deren letzter Halbzug ein Doppelschritt ist, wäre unsichtbar.</para>
    /// </summary>
    public static bool Matches(string fen, string target)
    {
        var a = fen.Split(' ');
        var b = target.Split(' ');
        if (a.Length < 4 || b.Length < 4) return fen.Trim() == target.Trim();
        if (a[0] != b[0] || a[1] != b[1] || a[2] != b[2]) return false;
        return a[3] == b[3] || a[3] == "-" || b[3] == "-";
    }

    /// <summary>
    /// Untere Schranke für die Zahl der Halbzüge von <paramref name="from"/> nach <paramref name="to"/>.
    /// <c>int.MaxValue</c> = unerreichbar (mehr Figuren als vorher, unmögliche Umwandlung, ein Bauer
    /// müsste rückwärts).
    ///
    /// <para>Sie ist die halbe Miete der Suche: je schärfer sie ist, desto mehr Äste fallen weg,
    /// bevor sie überhaupt betreten werden. Gerechnet wird je Seite das MAXIMUM aus drei Gründen,
    /// Züge zu machen — Schlagfälle, Umwandlungen und <b>Verschiebung</b>: was in der Zielstellung
    /// wo steht, muss von irgendwo dorthin gelaufen sein, und für jede Figurenart ist die
    /// Mindest-Zugzahl zwischen zwei Feldern bekannt (Springer per Distanztabelle, Läufer 1 oder 2,
    /// Bauern nur vorwärts und Seitwärts nur per Schlag). Je Zielfigur wird das billigste passende
    /// Ausgangsfeld genommen; die Summe bleibt damit eine ZULÄSSIGE Schranke (sie unterschätzt eher,
    /// weil dieselbe Ausgangsfigur mehrfach genommen werden darf).</para>
    ///
    /// <para>Vorher stand hier „veränderte Felder ÷ 4" — bei einer Stellung acht Halbzüge später
    /// waren das 2 statt 8, also praktisch keine Bremse. Deshalb ging die Suche nur sechs Halbzüge
    /// weit, bevor sie im Knotenbudget erstickte.</para>
    /// </summary>
    public static int MinPlies(string from, string to)
    {
        var a = Census.Of(from);
        var b = Census.Of(to);
        return a == null || b == null ? int.MaxValue : MinPlies(a, b);
    }

    private static int MinPlies(Census a, Census b)
    {
        // Figuren können nur verschwinden — mehr werden nur Offiziere durch Umwandlung.
        var whiteGone = a.White - b.White;
        var blackGone = a.Black - b.Black;
        if (whiteGone < 0 || blackGone < 0) return int.MaxValue;

        var needWhiteMoves = blackGone;   // schwarze Figuren nimmt nur Weiß vom Brett
        var needBlackMoves = whiteGone;

        var whitePromotions = Promotions(a, b, 'w');
        var blackPromotions = Promotions(a, b, 'b');
        if (whitePromotions < 0 || blackPromotions < 0) return int.MaxValue;
        needWhiteMoves = Math.Max(needWhiteMoves, whitePromotions);
        needBlackMoves = Math.Max(needBlackMoves, blackPromotions);

        var whiteDisplacement = Displacement(a, b, white: true);
        var blackDisplacement = Displacement(a, b, white: false);
        if (whiteDisplacement < 0 || blackDisplacement < 0) return int.MaxValue;
        needWhiteMoves = Math.Max(needWhiteMoves, whiteDisplacement);
        needBlackMoves = Math.Max(needBlackMoves, blackDisplacement);

        // Die PARITÄT steht fest: jeder Halbzug wechselt die Seite am Zug. Steht in beiden Stellungen
        // dieselbe Seite am Zug, ist die Zahl der Halbzüge gerade, sonst ungerade.
        var parity = a.WhiteToMove == b.WhiteToMove ? 0 : 1;
        for (var n = 0; n <= 128; n++)
        {
            if (n % 2 != parity) continue;
            var whiteMoves = a.WhiteToMove ? (n + 1) / 2 : n / 2;
            var blackMoves = n - whiteMoves;
            if (whiteMoves >= needWhiteMoves && blackMoves >= needBlackMoves) return n;
        }
        return int.MaxValue;
    }

    /// <summary>
    /// Wie viele Züge braucht diese Seite MINDESTENS, damit ihre Figuren dort stehen, wo die
    /// Zielstellung sie haben will? &lt; 0 = geht gar nicht.
    /// </summary>
    private static int Displacement(Census a, Census b, bool white)
    {
        var sum = 0;
        foreach (var (targetPiece, targetSquare) in b.Pieces)
        {
            if (char.IsUpper(targetPiece) != white) continue;
            var best = int.MaxValue;
            foreach (var (sourcePiece, sourceSquare) in a.Pieces)
            {
                if (char.IsUpper(sourcePiece) != white) continue;
                var cost = MoveCost(sourcePiece, sourceSquare, targetPiece, targetSquare);
                if (cost >= 0 && cost < best) best = cost;
                if (best == 0) break;
            }
            if (best == int.MaxValue) return -1;   // diese Figur kann von nirgendwo dorthin kommen
            sum += best;
        }

        // ROCHADE zählt als EIN Zug, bewegt aber König UND Turm — nach der Geometrie wären es bis zu
        // vier. Ohne diesen Abzug wäre die Schranke zu groß und die Suche verwürfe echte Lösungen.
        if (sum > 0 && a.CanCastle(white)) sum = Math.Max(1, sum - 3);
        return sum;
    }

    /// <summary>Mindestzahl der Züge, mit denen <paramref name="source"/> zur Zielfigur auf
    /// <paramref name="targetSquare"/> werden kann (Hindernisse ignoriert). &lt; 0 = unmöglich.</summary>
    private static int MoveCost(char source, int sourceSquare, char target, int targetSquare)
    {
        var s = char.ToLowerInvariant(source);
        var t = char.ToLowerInvariant(target);
        var white = char.IsUpper(source);
        if (s == t) return GeometricCost(s, sourceSquare, targetSquare, white);

        // Aus einem Bauern kann ein Offizier werden — aber nichts wird je zum Bauern, und der König
        // ist einmalig.
        if (s != 'p' || t == 'p' || t == 'k') return -1;
        var rank = Rank(sourceSquare);
        return white ? 7 - rank : rank;   // so viele Schritte bis zur Umwandlungsreihe
    }

    private static int File(int square) => square % 8;
    private static int Rank(int square) => 7 - square / 8;   // FEN beginnt bei Reihe 8

    private static int GeometricCost(char piece, int s, int d, bool white)
    {
        if (s == d) return 0;
        int fs = File(s), rs = Rank(s), fd = File(d), rd = Rank(d);
        var df = Math.Abs(fd - fs);
        var dr = Math.Abs(rd - rs);
        switch (piece)
        {
            case 'k': return Math.Max(df, dr);
            case 'q': return df == 0 || dr == 0 || df == dr ? 1 : 2;
            case 'r': return df == 0 || dr == 0 ? 1 : 2;
            case 'b': return (fs + rs) % 2 != (fd + rd) % 2 ? -1 : df == dr ? 1 : 2;
            case 'n': return KnightDistance[s * 64 + d];
            case 'p':
                var forward = white ? rd - rs : rs - rd;
                // Bauern gehen nur vorwärts, und zur Seite nur beim Schlagen — jeder Schlag ist
                // zugleich ein Schritt vorwärts. Aus der GRUNDREIHE gibt es dazu den Doppelschritt:
                // zwei Reihen in EINEM Zug. Ohne diese Ausnahme schätzte die Schranke jedes „1.e4"
                // als zwei Züge und verwarf damit echte Lösungen (Zufalls-Test, 2026-09-20).
                if (forward <= 0 || df > forward) return -1;
                var onHomeRank = white ? rs == 1 : rs == 6;
                return onHomeRank && df < forward ? Math.Max(1, Math.Max(df, forward - 1)) : forward;
            default: return -1;
        }
    }

    /// <summary>Springer-Distanzen zwischen allen Feldern — einmal per Breitensuche gerechnet.</summary>
    private static readonly int[] KnightDistance = BuildKnightDistances();

    private static int[] BuildKnightDistances()
    {
        var table = new int[64 * 64];
        int[] dFile = { 1, 2, 2, 1, -1, -2, -2, -1 };
        int[] dRank = { 2, 1, -1, -2, -2, -1, 1, 2 };
        for (var start = 0; start < 64; start++)
        {
            var dist = new int[64];
            Array.Fill(dist, -1);
            dist[start] = 0;
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var square = queue.Dequeue();
                int f = File(square), r = Rank(square);
                for (var i = 0; i < 8; i++)
                {
                    int nf = f + dFile[i], nr = r + dRank[i];
                    if (nf < 0 || nf > 7 || nr < 0 || nr > 7) continue;
                    var next = (7 - nr) * 8 + nf;
                    if (dist[next] >= 0) continue;
                    dist[next] = dist[square] + 1;
                    queue.Enqueue(next);
                }
            }
            Array.Copy(dist, 0, table, start * 64, 64);
        }
        return table;
    }

    /// <summary>Wie viele Umwandlungen braucht diese Seite mindestens? &lt; 0 = unmöglich.</summary>
    private static int Promotions(Census a, Census b, char color)
    {
        var gained = 0;
        foreach (var piece in "qrbn")
        {
            var diff = b.Count(color, piece) - a.Count(color, piece);
            if (diff > 0) gained += diff;
        }
        if (gained == 0) return 0;
        var pawnsGone = a.Count(color, 'p') - b.Count(color, 'p');
        return pawnsGone >= gained ? gained : -1;
    }

    /// <summary>Sucht die Züge zwischen zwei Stellungen.</summary>
    public static Result Solve(string? fromFen, string? toFen, int maxPlies = DefaultMaxPlies,
        int maxSolutions = 5, int nodeBudget = DefaultNodeBudget, TimeSpan? timeBudget = null)
    {
        maxPlies = Math.Clamp(maxPlies, 1, MaxSearchPlies);
        if (!ReconstructionChain.IsLoadableFen(fromFen)) return Empty("invalid-from");
        if (!ReconstructionChain.IsLoadableFen(toFen)) return Empty("invalid-to");
        if (Matches(fromFen!, toFen!)) return Empty("same-position");

        var targetCensus = Census.Of(toFen!);
        var startCensus = Census.Of(fromFen!);
        if (targetCensus == null || startCensus == null) return Empty("invalid-to");

        var lower = MinPlies(startCensus, targetCensus);
        if (lower > maxPlies) return Empty(lower == int.MaxValue ? "unreachable" : "too-far");

        var target = toFen!;
        var board = ChessBoard.LoadFromFen(fromFen!);
        var solutions = new List<Solution>();
        var path = new List<string>();
        var dead = new HashSet<string>();
        var nodes = 0;
        var exhausted = false;
        var deepest = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var deadline = timeBudget ?? DefaultTimeBudget;

        bool OutOfBudget()
        {
            // Die Uhr nur alle paar tausend Knoten lesen — `Elapsed` ist nicht umsonst.
            if (nodes >= nodeBudget) return true;
            return (nodes & 0x3FF) == 0 && clock.Elapsed > deadline;
        }

        bool Dfs(int remaining)
        {
            if (OutOfBudget()) { exhausted = true; return false; }
            nodes++;

            var here = board.ToFen();
            if (remaining == 0) return Matches(here, target);

            var key = remaining + "|" + PositionKey(here);
            if (dead.Contains(key)) return false;
            var census = Census.Of(here);
            if (census == null || MinPlies(census, targetCensus) > remaining) return false;

            var found = false;
            foreach (var move in Ordered(board, targetCensus))
            {
                if (solutions.Count >= maxSolutions) break;
                // Am Budget ist Schluss — und das MUSS vermerkt werden: sonst gälte der Knoten unten
                // als ausgeschöpfte Sackgasse, und die Antwort sagte „gibt es nicht" statt „nicht gefunden".
                if (OutOfBudget()) { exhausted = true; break; }
                var san = string.IsNullOrEmpty(move.San) ? GamePlies.ToUci(move) : move.San;
                try { if (!board.Move(move)) continue; }
                catch { continue; }

                path.Add(san);
                if (Dfs(remaining - 1))
                {
                    found = true;
                    if (remaining == 1) solutions.Add(new Solution(string.Join(' ', path), path.Count));
                }
                path.RemoveAt(path.Count - 1);
                board.Cancel();
            }

            // Nur AUSGESCHÖPFTE Sackgassen merken: ein Abbruch am Budget sagt nichts über den Ast.
            if (!found && !exhausted) dead.Add(key);
            return found;
        }

        // Kürzeste zuerst; die Parität liegt fest (wer am Zug ist, wechselt mit jedem Halbzug) und
        // steckt schon in der Schranke — MinPlies zählt nur Halbzug-Zahlen der richtigen Parität.
        var parity = lower % 2;
        for (var depth = Math.Max(lower, parity == 0 ? 2 : 1); depth <= maxPlies; depth++)
        {
            if (depth % 2 != parity) continue;
            deepest = depth;
            Dfs(depth);
            // Ist eine Tiefe fündig geworden, ist Schluss: gesucht ist die KÜRZESTE Erklärung für die
            // Lücke. Zwei Halbzüge tiefer gäbe es ein Vielfaches an Wegen, und dass jemand sich an eine
            // längere Fortsetzung NICHT erinnert, obwohl eine kürzere passt, ist der seltenere Fall.
            if (solutions.Count > 0 || exhausted) break;
        }

        var reason = solutions.Count > 0 ? null : exhausted ? "budget" : "none";
        return new Result(solutions, nodes, exhausted, reason, deepest);
    }

    /// <summary>
    /// Die Züge eines Knotens in der Reihenfolge, in der sie sich lohnen: zuerst die, die eine Figur
    /// auf ein Feld stellen, auf dem die ZIELSTELLUNG genau diese Figur haben will, und die ein Feld
    /// räumen, auf dem sie dort nichts zu suchen hat.
    ///
    /// <para>Das ändert an der Vollständigkeit nichts (alle Züge kommen dran), aber die Lösung steht
    /// früher fest — und weil die Suche bei der ersten fündigen Tiefe aufhört, spart das den Löwenanteil.</para>
    /// </summary>
    private static IEnumerable<Move> Ordered(ChessBoard board, Census target)
    {
        var moves = board.Moves(generateSan: true, allowAmbiguousCastle: false);
        var wanted = new char[64];
        foreach (var (piece, square) in target.Pieces) wanted[square] = piece;

        return moves
            .Select(m => (move: m, score: Score(m, wanted)))
            .OrderByDescending(x => x.score)
            .Select(x => x.move);
    }

    private static int Score(Move move, char[] wanted)
    {
        var from = SquareOf(move.OriginalPosition.ToString());
        var to = SquareOf(move.NewPosition.ToString());
        if (from < 0 || to < 0) return 0;
        // Figurenzeichen wie in der FEN: Typ als Buchstabe, Grossschreibung = Weiss.
        var type = move.Piece?.Type.AsChar ?? '\0';
        var piece = type == '\0' ? '\0'
            : move.Piece!.Color == PieceColor.White ? char.ToUpperInvariant(type) : char.ToLowerInvariant(type);
        var score = 0;
        if (piece != '\0' && wanted[to] == piece) score += 2;   // landet genau dort, wo sie hinsoll
        if (wanted[from] == '\0') score += 1;                   // räumt ein Feld, das leer sein soll
        else if (wanted[from] == piece) score -= 2;             // läuft von seinem Zielfeld weg
        return score;
    }

    /// <summary>„e4" → Feldnummer in FEN-Reihenfolge (0 = a8).</summary>
    private static int SquareOf(string algebraic)
    {
        if (algebraic.Length < 2) return -1;
        var file = algebraic[0] - 'a';
        var rank = algebraic[1] - '1';
        if (file < 0 || file > 7 || rank < 0 || rank > 7) return -1;
        return (7 - rank) * 8 + file;
    }

    private static Result Empty(string reason) => new(Array.Empty<Solution>(), 0, false, reason);

    /// <summary>Figurenzählung einer Stellung — Grundlage der unteren Schranke.</summary>
    /// <summary>Was in einer Stellung steht — Grundlage der unteren Schranke.</summary>
    private sealed class Census
    {
        private readonly Dictionary<char, int> _counts = new();
        public int White { get; private set; }
        public int Black { get; private set; }
        public bool WhiteToMove { get; private set; }
        private bool _whiteCastle;
        private bool _blackCastle;

        /// <summary>Alle Figuren als (Zeichen, Feld) — Feld 0 = a8, 63 = h1 (Reihenfolge der FEN).</summary>
        public List<(char Piece, int Square)> Pieces { get; } = new(32);

        public bool CanCastle(bool white) => white ? _whiteCastle : _blackCastle;

        public int Count(char color, char piece)
        {
            var key = color == 'w' ? char.ToUpperInvariant(piece) : char.ToLowerInvariant(piece);
            return _counts.TryGetValue(key, out var n) ? n : 0;
        }

        public static Census? Of(string fen)
        {
            var fields = fen.Split(' ');
            if (fields.Length < 2) return null;
            var census = new Census { WhiteToMove = fields[1] == "w" };
            if (fields.Length >= 3)
            {
                census._whiteCastle = fields[2].Contains('K') || fields[2].Contains('Q');
                census._blackCastle = fields[2].Contains('k') || fields[2].Contains('q');
            }

            var square = 0;
            foreach (var ch in fields[0])
            {
                if (ch == '/') continue;
                if (char.IsDigit(ch)) { square += ch - '0'; continue; }
                if (square > 63) return null;
                census.Pieces.Add((ch, square));
                census._counts[ch] = census.Count(char.IsUpper(ch) ? 'w' : 'b', ch) + 1;
                if (char.IsUpper(ch)) census.White++; else census.Black++;
                square++;
            }
            return square == 64 ? census : null;
        }
    }
}
