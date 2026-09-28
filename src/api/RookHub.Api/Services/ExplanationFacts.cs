using System.Globalization;
using Chess;

namespace RookHub.Api.Services;

/// <summary>
/// Konkrete, GEPRÜFTE Fakten für „Warum war das ein Fehler?" (0.572.0, gemeldet 2026-09-28 an Prod-Partie 34): das Modell
/// bekam bis dahin Gewinnchance, Bewertung und die Engine-Linien als SAN — und begründete jeden Fehler mit genau dem, was
/// es bekam: „ein Fehler, weil er seine Gewinnchancen senkt", „du kannst Druck aufbauen", bei 32.Sxe4 fxe4 sogar ein
/// „starker Bauernvorteil", obwohl eine Figur fällt. Hier steht deshalb, was in den Linien WIRKLICH passiert (Schachs, Matt,
/// Umwandlung, Materialbilanz, was der erste Zug angreift) und wie die Lage vorher und nachher in Worten ist — das Modell
/// soll erklären, nicht rechnen, und schon gar nicht raten.
/// </summary>
public static class ExplanationFacts
{
    /// <summary>Stufen der Bewertung aus Sicht einer Seite (Bauern), von oben: ab 2,5 auf Gewinn, ab 1 klar besser, ab
    /// 0,4 leicht besser, darunter ausgeglichen — gespiegelt nach unten. Die Grenzen nannte der Nutzer (2026-09-28: „auf
    /// Gewinn, alles ab 2.5", „von −1 auf −3 von einer schlechteren Stellung zu einer verlorenen", „von +0.5 auf −0.5 von
    /// besser zu schlechter").</summary>
    public const double WinningPawns = 2.5;
    public const double ClearlyPawns = 1.0;
    public const double SlightlyPawns = 0.4;

    /// <summary>Eine Bewertung aus Sicht einer Seite: Bauern, oder Matt (positiv = diese Seite setzt matt).</summary>
    public readonly record struct Eval(double Pawns, int? Mate)
    {
        public Eval Flip() => new(-Pawns, -Mate);

        /// <summary>−3 (verloren/wird matt) … +3 (auf Gewinn/setzt matt).</summary>
        public int Level => Mate is int m ? (m >= 0 ? 3 : -3)
            : Pawns >= WinningPawns ? 3 : Pawns >= ClearlyPawns ? 2 : Pawns >= SlightlyPawns ? 1
            : Pawns > -SlightlyPawns ? 0 : Pawns > -ClearlyPawns ? -1 : Pawns > -WinningPawns ? -2 : -3;

        public string Text => Mate is int m
            ? (m > 0 ? $"mate in {m}" : m < 0 ? $"gets mated in {-m}" : "mate")
            : Pawns.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>Liest <see cref="GameMistakes.EvalText"/> zurück („+1.35", „mate in 3", „gets mated in 2", „mate").</summary>
    public static Eval? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        if (t == "mate") return new Eval(0, 0);
        if (t.StartsWith("mate in ", StringComparison.Ordinal) && int.TryParse(t[8..], out var m)) return new Eval(0, m);
        if (t.StartsWith("gets mated in ", StringComparison.Ordinal) && int.TryParse(t[14..], out var g)) return new Eval(0, -g);
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? new Eval(p, null) : null;
    }

    private static string LevelName(Eval e) => e.Mate is int m
        ? (m >= 0 ? "winning (forced mate)" : "lost (getting mated)")
        : e.Level switch
        {
            3 => "winning", 2 => "clearly better", 1 => "slightly better", 0 => "about equal",
            -1 => "slightly worse", -2 => "clearly worse", _ => "lost",
        };

    /// <summary>
    /// Stand die ziehende Seite schon VOR dem Zug auf Verlust und steht sie danach weiter so? Dann ist der Fehler keine
    /// Erklärung wert (Nutzer 2026-09-28: „von −4 auf −6 muss das nicht kommentiert werden") — er ändert an der Partie nichts.
    /// </summary>
    public static bool AlreadyLost(GameMistakes.Flaw f)
        => Parse(f.EvalBefore) is { Level: -3 } && Parse(f.EvalAfter) is { Level: -3 };

    /// <summary>
    /// Die Lage in Worten — aus Sicht des LESERS (<paramref name="viewpoint"/> white/black), ohne bekannte Seite aus Sicht
    /// der ziehenden. Beschrieben wird der Übergang: „from clearly worse to lost", „still winning, but part of the advantage
    /// is gone", „the opponent made the win even easier". <c>null</c>, wenn eine Bewertung fehlt.
    /// </summary>
    public static string? Situation(GameMistakes.Flaw f, string viewpoint)
    {
        if (Parse(f.EvalBefore) is not { } before || Parse(f.EvalAfter) is not { } after) return null;
        var reader = viewpoint is "white" or "black" ? viewpoint == "white" : (bool?)null;
        var readerMoved = reader is null || reader == f.White;
        if (!readerMoved) { before = before.Flip(); after = after.Flip(); }
        var who = reader is null ? (f.White ? "White" : "Black") : "the reader";
        var numbers = $"(evaluation from {(reader is null ? who + "'s" : "the reader's")} view: {before.Text} before, {after.Text} after)";

        string change;
        if (before.Level != after.Level)
            change = $"for {who} the position went from {LevelName(before)} to {LevelName(after)}";
        else if (readerMoved)
            change = before.Level switch
            {
                >= 2 => $"{who} is still {LevelName(after)}, but gave away part of the advantage",
                <= -2 => $"{who} was already {LevelName(before)}, and it got worse",
                _ => $"it stays {LevelName(after)} for {who}, but the position got a little worse",
            };
        else
            change = before.Level switch
            {
                >= 3 => $"the reader was already {LevelName(before)} — the opponent's move made the win even easier",
                2 => $"the reader was already {LevelName(before)} — the opponent's move increased the reader's advantage",
                <= -2 => $"the reader is still {LevelName(after)}, but the opponent gave back part of the advantage",
                _ => $"it stays {LevelName(after)} for the reader, but the position improved a little",
            };
        return $"{change} {numbers}.";
    }

    /// <summary>
    /// Das ERGEBNIS einer Linie ab <paramref name="fen"/>: ein Schach im ersten Zug, Matt, Umwandlung und die
    /// Materialbilanz am Ende — nur, wenn danach jemand vorne liegt („White ends up ahead by 3: White took a knight, Black
    /// took nothing"). Einzelne Schlagfälle stehen bewusst NICHT da: im Probelauf an Prod-Partie 34 las das Modell aus
    /// „hxg4 … Kxg4" einen „materiellen Ausgleich" als Grund und verwechselte Schlagzüge der eigenen Seite in der Linie
    /// des Gegners. Leer, wenn nichts davon vorkommt oder die Linie nicht nachzuspielen ist.
    /// </summary>
    /// <param name="gainerWhite">Nur dann eine Materialbilanz, wenn DIESE Seite vorne liegt (<c>true</c> = Weiß) — in
    /// der Antwort-Linie der Gegner, in der besseren Linie der Ziehende. Holt die ziehende Seite in der Antwort-Linie
    /// selbst etwas, ist das kein Grund für den Fehler (Partie 34, 33…La6: „ahead by 1" in BEIDEN Linien, das Modell
    /// schrieb daraus einen verpassten Bauerngewinn). <c>null</c> = immer.</param>
    public static List<string> LineEvents(string fen, IReadOnlyList<string> sans, bool? gainerWhite = null)
    {
        var events = new List<string>();
        if (sans.Count == 0) return events;
        var gained = new Dictionary<bool, List<char>> { [true] = [], [false] = [] };
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            for (var i = 0; i < sans.Count; i++)
            {
                var san = sans[i];
                var move = Array.Find(board.Moves(generateSan: true), m => m.San == san);
                if (move is null) break;
                var white = move.Piece?.Color == PieceColor.White;
                if (move.CapturedPiece is { } captured) gained[white].Add(char.ToLowerInvariant(captured.Type.AsChar));
                if (move.IsMate) events.Add($"the line ends in checkmate ({san})");
                else if (i == 0 && move.IsCheck) events.Add($"{san} gives check");
                if (move.IsPromotion) events.Add($"{san}: {(white ? "White" : "Black")} promotes a pawn");
                board.Move(move);
            }
        }
        catch
        {
            // Ungültige FEN o. ä. — was bis hierher gesammelt ist, stimmt.
        }
        var net = gained[true].Sum(Value) - gained[false].Sum(Value);
        if (net != 0 && (gainerWhite is null || gainerWhite == net > 0))
            events.Add($"{(net > 0 ? "White" : "Black")} ends up ahead in material by {Math.Abs(net)} "
                       + $"(White took {Taken(gained[true])}, Black took {Taken(gained[false])})");
        return events;
    }

    /// <summary>
    /// Was der ERSTE Zug einer Linie angreift: die Figuren der Gegenseite, die die gezogene Figur danach schlagen könnte
    /// (ohne Bauern) — „f5 attacks the knight on g4". Dafür wird die Stellung nach dem Zug mit derselben Seite am Zug
    /// gelesen; geht das nicht (die Gegenseite steht im Schach), bleibt es leer — das Schach steht dann ohnehin da.
    /// </summary>
    public static List<string> FirstMoveAttacks(string fen, IReadOnlyList<string> sans)
    {
        var result = new List<string>();
        if (sans.Count == 0) return result;
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            var move = Array.Find(board.Moves(generateSan: true), m => m.San == sans[0]);
            if (move is null || move.IsCheck) return result;
            board.Move(move);
            var parts = board.ToFen().Split(' ');
            if (parts.Length < 4) return result;
            parts[1] = parts[1] == "w" ? "b" : "w";
            parts[3] = "-";
            var again = ChessBoard.LoadFromFen(string.Join(' ', parts));
            foreach (var m in again.Moves(move.NewPosition, generateSan: true))
            {
                if (m.CapturedPiece is not { } target) continue;
                var type = char.ToLowerInvariant(target.Type.AsChar);
                if (type is 'p' or 'k') continue;
                result.Add($"after {sans[0]} the {PieceName(char.ToLowerInvariant(move.Piece!.Type.AsChar))} on {move.NewPosition} "
                           + $"attacks the {PieceName(type)} on {m.NewPosition}");
            }
        }
        catch
        {
            // Stellung mit getauschtem Zugrecht ungültig — dann eben ohne.
        }
        return result;
    }

    private static int Value(char type) => type switch { 'p' => 1, 'n' => 3, 'b' => 3, 'r' => 5, 'q' => 9, _ => 0 };

    private static string PieceName(char type) => type switch
    {
        'p' => "pawn", 'n' => "knight", 'b' => "bishop", 'r' => "rook", 'q' => "queen", 'k' => "king", _ => "piece",
    };

    private static string Taken(List<char> pieces) => pieces.Count == 0 ? "nothing"
        : string.Join(", ", pieces.GroupBy(p => p).Select(g => g.Count() == 1 ? PieceName(g.Key) : $"{g.Count()} {PieceName(g.Key)}s"));
}
