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
    public const int MaxSearchPlies = 6;

    /// <summary>Vorgabe, wenn der Aufrufer nichts sagt.</summary>
    public const int DefaultMaxPlies = 4;

    /// <summary>So viele Knoten höchstens — danach bricht die Suche ehrlich ab.</summary>
    public const int DefaultNodeBudget = 300_000;

    /// <summary>Ein gefundener Weg.</summary>
    public record Solution(string San, int Plies);

    /// <summary>
    /// Das Ergebnis einer Suche.
    /// </summary>
    /// <param name="Solutions">Gefundene Wege, kürzeste zuerst.</param>
    /// <param name="Nodes">Besuchte Stellungen (fürs Protokoll und die Anzeige).</param>
    /// <param name="BudgetExhausted">Die Suche wurde abgebrochen — „nicht gefunden" ≠ „gibt es nicht".</param>
    /// <param name="Reason">Warum es keine Lösung gibt, wenn die Liste leer ist.</param>
    public record Result(IReadOnlyList<Solution> Solutions, int Nodes, bool BudgetExhausted, string? Reason);

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
    /// <c>int.MaxValue</c> = unerreichbar (mehr Figuren als vorher, unmögliche Umwandlung).
    /// </summary>
    public static int MinPlies(string from, string to)
    {
        var a = Census.Of(from);
        var b = Census.Of(to);
        if (a == null || b == null) return int.MaxValue;

        // Figuren können nur verschwinden — mehr werden nur Offiziere durch Umwandlung.
        var whiteGone = a.White - b.White;
        var blackGone = a.Black - b.Black;
        if (whiteGone < 0 || blackGone < 0) return int.MaxValue;

        var needWhiteMoves = blackGone;   // schwarze Figuren nimmt nur Weiß vom Brett
        var needBlackMoves = whiteGone;

        foreach (var (color, promotions) in new[] { ('w', Promotions(a, b, 'w')), ('b', Promotions(a, b, 'b')) })
        {
            if (promotions < 0) return int.MaxValue;   // Offizier dazu, aber kein Bauer weg
            if (color == 'w') needWhiteMoves = Math.Max(needWhiteMoves, promotions);
            else needBlackMoves = Math.Max(needBlackMoves, promotions);
        }

        // Ein Halbzug ändert höchstens vier Felder (Rochade: König und Turm, je von/nach).
        var byBoard = (a.DifferingSquares(b) + 3) / 4;

        // Die PARITÄT steht fest: jeder Halbzug wechselt die Seite am Zug. Steht in beiden Stellungen
        // dieselbe Seite am Zug, ist die Zahl der Halbzüge gerade, sonst ungerade. Ohne diese Bedingung
        // läge die Schranke auf der falschen Parität — und die Suche liefe genau die Tiefen ab, in denen
        // es die Lösung nicht geben kann.
        var parity = a.WhiteToMove == b.WhiteToMove ? 0 : 1;
        for (var n = Math.Max(byBoard, 0); n <= 64; n++)
        {
            if (n % 2 != parity) continue;
            var whiteMoves = a.WhiteToMove ? (n + 1) / 2 : n / 2;
            var blackMoves = n - whiteMoves;
            if (whiteMoves >= needWhiteMoves && blackMoves >= needBlackMoves) return n;
        }
        return int.MaxValue;
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
        int maxSolutions = 5, int nodeBudget = DefaultNodeBudget)
    {
        maxPlies = Math.Clamp(maxPlies, 1, MaxSearchPlies);
        if (!ReconstructionChain.IsLoadableFen(fromFen)) return Empty("invalid-from");
        if (!ReconstructionChain.IsLoadableFen(toFen)) return Empty("invalid-to");
        if (Matches(fromFen!, toFen!)) return Empty("same-position");

        var lower = MinPlies(fromFen!, toFen!);
        if (lower > maxPlies) return Empty(lower == int.MaxValue ? "unreachable" : "too-far");

        var target = toFen!;
        var board = ChessBoard.LoadFromFen(fromFen!);
        var solutions = new List<Solution>();
        var path = new List<string>();
        var dead = new HashSet<string>();
        var nodes = 0;
        var exhausted = false;

        bool Dfs(int remaining)
        {
            if (nodes >= nodeBudget) { exhausted = true; return false; }
            nodes++;

            var here = board.ToFen();
            if (remaining == 0) return Matches(here, target);

            var key = remaining + "|" + PositionKey(here);
            if (dead.Contains(key)) return false;
            if (MinPlies(here, toFen!) > remaining) return false;

            var found = false;
            foreach (var move in board.Moves(generateSan: true, allowAmbiguousCastle: false))
            {
                if (solutions.Count >= maxSolutions) break;
                // Am Budget ist Schluss — und das MUSS vermerkt werden: sonst gälte der Knoten unten
                // als ausgeschöpfte Sackgasse, und die Antwort sagte „gibt es nicht" statt „nicht gefunden".
                if (nodes >= nodeBudget) { exhausted = true; break; }
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
            Dfs(depth);
            // Ist eine Tiefe fündig geworden, ist Schluss: gesucht ist die KÜRZESTE Erklärung für die
            // Lücke. Zwei Halbzüge tiefer gäbe es ein Vielfaches an Wegen, und dass jemand sich an eine
            // längere Fortsetzung NICHT erinnert, obwohl eine kürzere passt, ist der seltenere Fall.
            if (solutions.Count > 0 || exhausted) break;
        }

        var reason = solutions.Count > 0 ? null : exhausted ? "budget" : "none";
        return new Result(solutions, nodes, exhausted, reason);
    }

    private static Result Empty(string reason) => new(Array.Empty<Solution>(), 0, false, reason);

    /// <summary>Figurenzählung einer Stellung — Grundlage der unteren Schranke.</summary>
    private sealed class Census
    {
        private readonly Dictionary<char, int> _counts = new();
        private string _boardField = string.Empty;
        public int White { get; private set; }
        public int Black { get; private set; }
        public bool WhiteToMove { get; private set; }

        public int Count(char color, char piece)
        {
            var key = color == 'w' ? char.ToUpperInvariant(piece) : char.ToLowerInvariant(piece);
            return _counts.TryGetValue(key, out var n) ? n : 0;
        }

        /// <summary>Wie viele Felder sind anders besetzt? (Beide Stellungen auf 64 Zeichen gebracht.)</summary>
        public int DifferingSquares(Census other)
        {
            var a = Expand(_boardField);
            var b = Expand(other._boardField);
            if (a.Length != 64 || b.Length != 64) return 0;
            var diff = 0;
            for (var i = 0; i < 64; i++) if (a[i] != b[i]) diff++;
            return diff;
        }

        private static string Expand(string boardField)
        {
            var sb = new System.Text.StringBuilder(64);
            foreach (var ch in boardField)
            {
                if (ch == '/') continue;
                if (char.IsDigit(ch)) sb.Append('.', ch - '0');
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        public static Census? Of(string fen)
        {
            var fields = fen.Split(' ');
            if (fields.Length < 2) return null;
            var census = new Census { _boardField = fields[0], WhiteToMove = fields[1] == "w" };
            foreach (var ch in fields[0])
            {
                if (ch == '/' || char.IsDigit(ch)) continue;
                census._counts[ch] = census.Count(char.IsUpper(ch) ? 'w' : 'b', ch) + 1;
                if (char.IsUpper(ch)) census.White++; else census.Black++;
            }
            return census;
        }
    }
}
