using System.Globalization;
using Chess;

namespace RookHub.Api.Services.Games;

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

    internal static string LevelName(Eval e) => e.Mate is int m
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
    /// <param name="lead">Ein Zug, der VOR der Linie ab <paramref name="fen"/> gespielt wird und NUR in die Materialbilanz
    /// eingeht — der Fehler selbst, wenn die Antwort-Linie ausgewertet wird. Ohne ihn zählte ein Zurückschlagen als Gewinn:
    /// Prod-Partie 35, 21…Tc4 22.Txc4 dxc4 — ab der Stellung NACH Txc4 gerechnet stand „Black ends up ahead by 5 (Black
    /// took a rook)", und das Modell schrieb „du gewinnst sofort einen Turm" (0.585.1).</param>
    /// <param name="leads">Wie <paramref name="lead"/>, aber mehrere Züge (nach diesem) — der Weg bis zu einer anderen
    /// Verteidigung in der Widerlegung: Fehlzug, Widerlegung bis dorthin, die andere Verteidigung (0.971.0).</param>
    public static List<string> LineEvents(string fen, IReadOnlyList<string> sans, bool? gainerWhite = null, string? lead = null,
        IReadOnlyList<string>? leads = null)
    {
        var events = new List<string>();
        if (sans.Count == 0) return events;
        var gained = new Dictionary<bool, List<char>> { [true] = [], [false] = [] };
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            List<string> before = lead is null ? [] : [lead];
            if (leads is not null) before.AddRange(leads);
            IReadOnlyList<string> moves = before.Count == 0 ? sans : [.. before, .. sans];
            var offset = before.Count;
            for (var j = 0; j < moves.Count; j++)
            {
                var san = moves[j];
                var i = j - offset;
                var move = Array.Find(board.Moves(generateSan: true), m => m.San == san);
                if (move is null) break;
                var white = move.Piece?.Color == PieceColor.White;
                if (move.CapturedPiece is { } captured) gained[white].Add(char.ToLowerInvariant(captured.Type.AsChar));
                if (i < 0)
                {
                    board.Move(move);
                    continue;
                }
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

    /// <summary>
    /// Schach mit Doppelangriff in einer Linie (0.971.0, Befund an Vereinspartie 245: nach 25...Lc8 26.Txc8+ Txc8 gabelt
    /// 27.Sd7+ König, Dame und Läufer — <see cref="FirstMoveAttacks"/> bleibt bei einem Schachgebot leer, und das Modell
    /// erzählte nur die Hauptlinie nach): jeder Zug der Seite, die die Linie beginnt, der Schach gibt, dessen gezogene Figur
    /// zugleich mindestens eine weitere Figur (ohne Bauern) angreift und die NICHT geschlagen werden kann (kein legaler
    /// Schlagzug auf ihr Feld) — „Nd7+ gives check and the knight on d7 also attacks the queen on b6 and the bishop on f6
    /// (a fork); the knight on d7 cannot be captured“. Gibt nicht die gezogene Figur selbst Schach, steht „a discovered
    /// check“ da. Nur die ersten <paramref name="maxPlies"/> Halbzüge — der Schwanz einer Engine-Linie ist kein Grund.
    /// </summary>
    public static List<string> CheckForks(string fen, IReadOnlyList<string> sans, int maxPlies = 6)
    {
        var result = new List<string>();
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            for (var i = 0; i < sans.Count && i < maxPlies; i++)
            {
                var san = sans[i];
                var move = Array.Find(board.Moves(generateSan: true), m => m.San == san);
                if (move is null) break;
                board.Move(move);
                if (i % 2 != 0 || !move.IsCheck || move.IsMate) continue;
                var square = move.NewPosition.ToString();
                if (BoardGeometry.FromFen(board.ToFen()) is not { } geo) continue;
                var piece = geo.PieceAt(square);
                if (piece == '\0') continue;
                var white = char.IsUpper(piece);
                var attacked = geo.AttackedFrom(square);
                var targets = attacked.Select(s => (Square: s, Piece: geo.PieceAt(s)))
                    .Where(t => t.Piece != '\0' && char.IsUpper(t.Piece) != white && char.ToLowerInvariant(t.Piece) is not ('p' or 'k'))
                    .OrderByDescending(t => Value(char.ToLowerInvariant(t.Piece))).ThenBy(t => t.Square, StringComparer.Ordinal)
                    .ToList();
                if (targets.Count == 0) continue;
                if (board.Moves().Any(m => m.CapturedPiece is not null && m.NewPosition.ToString() == square)) continue;
                var name = PieceName(char.ToLowerInvariant(piece));
                var direct = attacked.Any(s => geo.PieceAt(s) == (white ? 'k' : 'K'));
                result.Add($"{san} gives check and the {name} on {square} also attacks "
                           + JoinAnd(targets.Select(t => $"the {PieceName(char.ToLowerInvariant(t.Piece))} on {t.Square}").ToList())
                           + $" ({(direct ? "a fork" : "a discovered check")}); the {name} on {square} cannot be captured");
            }
        }
        catch
        {
            // Linie nicht nachzuspielen — was bis hierher gefunden ist, stimmt.
        }
        return result;
    }

    /// <summary>
    /// Abzugsangriff in einer Linie (0.974.0, Befund an Vereinspartie 251: nach 20...e4 21.Txe4 öffnet 21...dxc3 die d-Linie,
    /// die Dame d6 greift die Dame d1 an — in keinem Fakt stand es): ein Zug der Seite, die die Linie beginnt, nach dem
    /// eine ANDERE eigene Langschrittfigur (Dame, Turm, Läufer) eine gegnerische Figur (ohne Bauern und König) bestreicht,
    /// die sie davor nicht bestrich — „dxc3 opens the d-file for the queen on d6, which now attacks the queen on d1 (a
    /// discovered attack)“. Reine Brettgeometrie (<see cref="BoardGeometry"/>): ob die Figur gedeckt ist, steht nicht da.
    /// Ein Abzugsschach meldet <see cref="CheckForks"/> bzw. <see cref="LineEvents"/>. Nur die ersten
    /// <paramref name="maxPlies"/> Halbzüge.
    /// </summary>
    public static List<string> DiscoveredAttacks(string fen, IReadOnlyList<string> sans, int maxPlies = 4)
    {
        var result = new List<string>();
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            for (var i = 0; i < sans.Count && i < maxPlies; i++)
            {
                var san = sans[i];
                var move = Array.Find(board.Moves(generateSan: true), m => m.San == san);
                if (move is null) break;
                var before = i % 2 == 0 ? BoardGeometry.FromFen(board.ToFen()) : null;
                board.Move(move);
                if (before is null || BoardGeometry.FromFen(board.ToFen()) is not { } after) continue;
                var white = move.Piece.Color == PieceColor.White;
                var to = move.NewPosition.ToString();
                foreach (var (piece, square) in after.Pieces(white))
                {
                    // Nur Figuren, die stehen geblieben sind (nicht die gezogene, nicht der Rochade-Turm).
                    if (square == to || char.ToLowerInvariant(piece) is not ('q' or 'r' or 'b') || before.PieceAt(square) != piece) continue;
                    var seen = before.AttackedFrom(square);
                    foreach (var t in after.AttackedFrom(square).Where(s => !seen.Contains(s)))
                    {
                        var target = after.PieceAt(t);
                        if (target == '\0' || char.IsUpper(target) == white || char.ToLowerInvariant(target) is 'p' or 'k') continue;
                        result.Add($"{san} opens the {LineName(square, t)} for the {PieceName(char.ToLowerInvariant(piece))} on {square}, "
                                   + $"which now attacks the {PieceName(char.ToLowerInvariant(target))} on {t} (a discovered attack)");
                    }
                }
            }
        }
        catch
        {
            // Linie nicht nachzuspielen — was bis hierher gefunden ist, stimmt.
        }
        return result;
    }

    private static string LineName(string a, string b)
        => a[0] == b[0] ? $"{a[0]}-file" : a[1] == b[1] ? $"{a[1]}{(a[1] switch { '1' => "st", '2' => "nd", '3' => "rd", _ => "th" })} rank" : "diagonal";

    /// <summary>
    /// Eine Figur, die in einer Linie einfach verloren geht (0.974.0, Vereinspartie 251: 20...e4 21.Txe4 dxc3 22.bxc3 Dxa3 —
    /// „gewinnt den Springer“ stand nur als Summe in der Materialbilanz): ein Zug der Seite, die die Linie beginnt, schlägt
    /// eine Figur (ohne Bauern), und der Gegner kann auf dem Feld nicht zurückschlagen (kein legaler Schlagzug dorthin) —
    /// „Qxa3 takes the knight on a3 and the queen on a3 cannot be recaptured“. Stand die Figur schon zu Beginn der Linie
    /// dort, steht dabei, wer sie da deckte, wenn diese Deckung inzwischen weg ist („… was defended only by the pawn on b2,
    /// which is no longer there“) oder dass sie gar nicht gedeckt war — Brettgeometrie, Fesselungen bleiben außen vor.
    /// KEIN Fakt ist ein Zurückschlagen (der vorige Halbzug hat auf demselben Feld geschlagen): das ist ein Tausch, den
    /// die Materialbilanz zeigt. Nur die ersten <paramref name="maxPlies"/> Halbzüge.
    /// </summary>
    /// <param name="leads">Züge, die VOR der Linie ab <paramref name="fen"/> gespielt werden (der Weg bis zum Beginn der
    /// Linie) — sie liefern keinen Fakt, sagen aber, ob der erste Zug der Linie nur zurückschlägt.</param>
    public static List<string> HangingCaptures(string fen, IReadOnlyList<string> sans, IReadOnlyList<string>? leads = null, int maxPlies = 6)
    {
        var result = new List<string>();
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            string? takenOn = null;   // Feld, auf dem der vorige Halbzug geschlagen hat
            foreach (var lead in leads ?? [])
            {
                var m = Array.Find(board.Moves(generateSan: true), x => x.San == lead);
                if (m is null) return result;
                takenOn = m.CapturedPiece is not null ? m.NewPosition.ToString() : null;
                board.Move(m);
            }
            var start = BoardGeometry.FromFen(board.ToFen());
            for (var i = 0; i < sans.Count && i < maxPlies; i++)
            {
                var san = sans[i];
                var move = Array.Find(board.Moves(generateSan: true), m => m.San == san);
                if (move is null) break;
                var square = move.NewPosition.ToString();
                var captured = move.CapturedPiece is { } c && move.Parameter is not MoveEnPassant ? char.ToLowerInvariant(c.Type.AsChar) : '\0';
                var exchange = takenOn == square;
                takenOn = move.CapturedPiece is not null ? square : null;
                var white = move.Piece.Color == PieceColor.White;
                board.Move(move);
                if (i % 2 != 0 || exchange || captured is '\0' or 'p' or 'k') continue;
                if (board.Moves().Any(m => m.CapturedPiece is not null && m.NewPosition.ToString() == square)) continue;
                if (BoardGeometry.FromFen(board.ToFen()) is not { } now) continue;
                var name = PieceName(captured);
                var fact = $"{san} takes the {name} on {square} and the {PieceName(char.ToLowerInvariant(now.PieceAt(square)))} on {square} cannot be recaptured";
                if (start is not null && start.PieceAt(square) is var stood && char.ToLowerInvariant(stood) == captured && char.IsUpper(stood) != white)
                {
                    var covered = start.Attackers(square, !white);
                    if (covered.Count == 0) fact += $" (the {name} was not defended at the start of this line)";
                    else if (covered.All(p => now.PieceAt(p.Square) != p.Piece))
                        fact += $" (at the start of this line the {name} was defended only by "
                                + JoinAnd(covered.Select(p => $"the {PieceName(char.ToLowerInvariant(p.Piece))} on {p.Square}").ToList())
                                + $", which {(covered.Count == 1 ? "is" : "are")} no longer there)";
                }
                result.Add(fact);
            }
        }
        catch
        {
            // Linie nicht nachzuspielen — was bis hierher gefunden ist, stimmt.
        }
        return result;
    }

    internal static string JoinAnd(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    private static int Value(char type) => type switch { 'p' => 1, 'n' => 3, 'b' => 3, 'r' => 5, 'q' => 9, _ => 0 };

    internal static string PieceName(char type) => type switch
    {
        'p' => "pawn", 'n' => "knight", 'b' => "bishop", 'r' => "rook", 'q' => "queen", 'k' => "king", _ => "piece",
    };

    private static string Taken(List<char> pieces) => pieces.Count == 0 ? "nothing"
        : string.Join(", ", pieces.GroupBy(p => p).Select(g => g.Count() == 1 ? PieceName(g.Key) : $"{g.Count()} {PieceName(g.Key)}s"));
}
