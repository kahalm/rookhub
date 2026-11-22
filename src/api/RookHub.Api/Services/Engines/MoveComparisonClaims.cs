using System.Text.RegularExpressions;
using Chess;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.Engines;

/// <summary>
/// Nachprüfung von Stellungsbehauptungen in einer Begründung (1.000.1, Befund Vereinspartie 260, Fehler 5, 21...Tb8 statt
/// 21...b6, 22.11.2026: „Angriff auf das ungedeckte Pferd auf d5“ — d5 war durch e6 und Td8 gedeckt, auch nach 22.Db3).
/// Nennt der Text eine Figur auf einem Feld „ungedeckt“, „hängt“, „ungeschützt“ (bzw. englisch „undefended“, „hanging“,
/// „unprotected“, „loose“), wird am Brett nachgesehen: in der Stellung vor dem Zug, nach den beiden Kandidaten und in
/// jeder Stellung der gegebenen Linien. Steht dort eine solche Figur (Art, wenn der Text sie nennt) auf dem Feld und ist
/// sie in KEINER dieser Stellungen ungedeckt, ist die Behauptung falsch — der Aufrufer fragt mit Hinweis neu an.
/// Wohlwollend: ist sie irgendwo ungedeckt oder steht dort nie eine, gilt die Behauptung (sie lässt sich nicht
/// eindeutig einer Stellung zuordnen). Seit 1.001.1 ebenso „nur von X gedeckt“/„einzige Deckung“
/// (<see cref="FalseSoleDefender"/>).
/// </summary>
internal static class MoveComparisonClaims
{
    private static readonly Regex Claim = new(
        @"ungedeckt\w*|ungeschützt\w*|hängt|hängend\w*|hängen(?:de|den|der|des)?\b|undefended|unprotected|hanging|hangs|loose",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Square = new(@"(?<![A-Za-z0-9])([a-h][1-8])(?![A-Za-z0-9])", RegexOptions.Compiled);

    private static readonly Regex SquareWord = new(@"(?:feld|felder|square|squares)\s+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly (Regex Word, char Type)[] PieceWords =
    [
        (new(@"springer|pferd|knight", RegexOptions.IgnoreCase | RegexOptions.Compiled), 'n'),
        (new(@"läufer|bishop", RegexOptions.IgnoreCase | RegexOptions.Compiled), 'b'),
        (new(@"turm|rook", RegexOptions.IgnoreCase | RegexOptions.Compiled), 'r'),
        (new(@"dame|queen", RegexOptions.IgnoreCase | RegexOptions.Compiled), 'q'),
        (new(@"bauer|pawn", RegexOptions.IgnoreCase | RegexOptions.Compiled), 'p'),
    ];

    /// <summary>Die Felder, deren Figur der Text fälschlich „ungedeckt“/„hängt“ nennt (leer = nichts zu beanstanden).</summary>
    internal static List<string> FalseUndefended(string text, MoveComparisonPrompts.PairFacts f)
    {
        var claims = ClaimedSquares(text);
        if (claims.Count == 0) return [];
        var positions = Positions(f);
        var wrong = new List<string>();
        foreach (var (square, type) in claims)
        {
            var seen = false;
            var undefended = false;
            foreach (var fen in positions)
            {
                if (BoardGeometry.FromFen(fen) is not { } geo) continue;
                var piece = geo.PieceAt(square);
                if (piece == '\0' || char.ToLowerInvariant(piece) == 'k') continue;
                if (type is char t && char.ToLowerInvariant(piece) != t) continue;
                seen = true;
                if (geo.Attackers(square, char.IsUpper(piece)).Count == 0) { undefended = true; break; }
            }
            if (seen && !undefended && !wrong.Contains(square)) wrong.Add(square);
        }
        return wrong;
    }

    /// <summary>Die Felder (samt Figurenart, falls genannt), die ein Satz mit einem der Schlüsselwörter nennt: im selben
    /// Satz das nächste Feld bis 25 Zeichen nach dem Wort, sonst das nächste bis 40 Zeichen davor; die Figurenart aus den
    /// 30 Zeichen vor dem Feld. „Feld e4“ zählt nicht (gemeint ist das Feld, keine Figur).</summary>
    internal static List<(string Square, char? Type)> ClaimedSquares(string text)
    {
        var result = new List<(string, char?)>();
        foreach (var sentence in Regex.Split(text, @"(?<=[.!?;:])\s+|\n"))
            foreach (Match k in Claim.Matches(sentence))
            {
                // Das nächste Feld NACH dem Wort („das ungedeckte Pferd auf d5“), sonst das nächste davor („the knight on
                // d5 is hanging“) — nicht jedes Feld des Satzes („Tempo gegen den Springer auf c5 und das ungedeckte …“).
                var squares = Square.Matches(sentence).Where(sq => !SquareWord.IsMatch(sentence[Math.Max(0, sq.Index - 30)..sq.Index])).ToList();
                var sq = squares.Where(x => x.Index >= k.Index + k.Length && x.Index - (k.Index + k.Length) <= 25).OrderBy(x => x.Index).FirstOrDefault()
                         ?? squares.Where(x => x.Index + x.Length <= k.Index && k.Index - (x.Index + x.Length) <= 40).OrderByDescending(x => x.Index).FirstOrDefault();
                if (sq is null) continue;
                var before = sentence[Math.Max(0, sq.Index - 30)..sq.Index];
                char? type = null;
                var at = -1;
                foreach (var (word, t) in PieceWords)
                    foreach (Match w in word.Matches(before))
                        if (w.Index > at) { at = w.Index; type = t; }
                result.Add((sq.Groups[1].Value, type));
            }
        return result.Distinct().ToList();
    }

    // ── „nur von X gedeckt“ (1.001.1) ───────────────────────────────────────────────────────────────

    /// <summary>„nur vom Läufer (selbst) gedeckt“, „defended only by the bishop (itself)“ — Gruppe <c>who</c> = die genannte
    /// Figur, <c>self</c> = „selbst/itself“; das Feld steht davor.</summary>
    private static readonly Regex OnlyBy = new(
        @"\bnur\s+(?:von|vom|durch)\s+(?:(?:de[nmrs]|die|das|eine[nmrs]?|seine[nmrs]?|ihre[nmrs]?)\s+)?(?<who>\w+)(?<self>\s+selbst)?(?<rest>[^.;:!?]{0,40}?)\b(?:gedeckt|geschützt|verteidigt)"
        + @"|\b(?:defended|covered|protected)\s+only\s+by\s+(?:(?:the|a|its)\s+)?(?<who>\w+)(?<self>\s+itself)?"
        + @"|\bonly\s+(?:defended|covered|protected)\s+by\s+(?:(?:the|a|its)\s+)?(?<who>\w+)(?<self>\s+itself)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>„die einzige Deckung des Bauern f4 ist der Turm“, „the only defender of …“ — das Feld steht danach.</summary>
    private static readonly Regex OnlyDefender = new(
        @"\beinzige[nrs]?\s+(?:Deckung|Verteidiger\w*|Schutz)|\b(?:only|sole)\s+(?:defender|protector|protection)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Die Felder, deren Figur der Text fälschlich „nur von X gedeckt“ (bzw. „einzige Deckung“, „defended only by“) nennt
    /// (Befund Vereinspartie 261, 22.11.2026: „das Feld f4 wurde nach 19...Le5 nur vom Läufer selbst gedeckt“ — nach 20.f4
    /// deckt der Läufer e3 den Bauern f4). Geprüft wird in denselben Stellungen wie bei <see cref="FalseUndefended"/>, wo auf
    /// dem Feld eine Figur (kein König) steht: die Behauptung gilt dort, wenn genau EINE Figur ihrer Farbe sie deckt und diese
    /// zur genannten Art (und zum genannten Feld) passt. „selbst/itself“ meint die Figur auf dem Feld — die deckt sich nie
    /// selbst. Gilt sie in keiner Stellung, ist sie falsch; steht dort nie eine Figur, bleibt sie unbeanstandet.
    /// </summary>
    internal static List<string> FalseSoleDefender(string text, MoveComparisonPrompts.PairFacts f)
    {
        var claims = SoleDefenderClaims(text);
        if (claims.Count == 0) return [];
        var positions = Positions(f);
        var wrong = new List<string>();
        foreach (var (square, type, at, self) in claims)
        {
            var seen = false;
            var holds = false;
            foreach (var fen in positions)
            {
                if (BoardGeometry.FromFen(fen) is not { } geo) continue;
                var piece = geo.PieceAt(square);
                if (piece == '\0' || char.ToLowerInvariant(piece) == 'k') continue;
                seen = true;
                if (self) continue;
                var defenders = geo.Attackers(square, char.IsUpper(piece));
                if (defenders.Count == 1 && (type is not char t || char.ToLowerInvariant(defenders[0].Piece) == t)
                    && (at is null || defenders[0].Square == at)) { holds = true; break; }
            }
            if (seen && !holds && !wrong.Contains(square)) wrong.Add(square);
        }
        return wrong;
    }

    /// <summary>Die „nur von X gedeckt“-Behauptungen eines Textes: Feld, Art und Feld der genannten deckenden Figur (falls
    /// genannt), „selbst“. Hier zählt auch „Feld f4“ — der Satz handelt vom Feld.</summary>
    internal static List<(string Square, char? Type, string? At, bool Self)> SoleDefenderClaims(string text)
    {
        var result = new List<(string, char?, string?, bool)>();
        foreach (var sentence in Regex.Split(text, @"(?<=[.!?;:])\s+|\n"))
        {
            var squares = Square.Matches(sentence).ToList();
            foreach (Match k in OnlyBy.Matches(sentence))
            {
                var sq = squares.Where(x => x.Index + x.Length <= k.Index && k.Index - (x.Index + x.Length) <= 60).OrderByDescending(x => x.Index).FirstOrDefault();
                if (sq is null) continue;
                var who = k.Groups["who"].Value;
                var type = TypeOf(who);
                // „nur vom Läufer auf e3 gedeckt“: das Feld der deckenden Figur direkt nach ihr.
                var at = squares.FirstOrDefault(x => x.Index > k.Groups["who"].Index && x.Index - (k.Groups["who"].Index + who.Length) <= 8);
                result.Add((sq.Groups[1].Value, type, at?.Groups[1].Value, k.Groups["self"].Success));
            }
            foreach (Match k in OnlyDefender.Matches(sentence))
            {
                var sq = squares.Where(x => x.Index >= k.Index + k.Length && x.Index - (k.Index + k.Length) <= 25).OrderBy(x => x.Index).FirstOrDefault();
                if (sq is null) continue;
                // Die deckende Figur: das erste Figurenwort nach dem Feld, ihr Feld direkt dahinter.
                var tail = sentence[(sq.Index + sq.Length)..];
                char? type = null;
                string? at = null;
                var first = PieceWords.SelectMany(p => p.Word.Matches(tail).Select(m => (m, p.Type))).OrderBy(x => x.m.Index).FirstOrDefault();
                if (first.m is { } w)
                {
                    type = first.Type;
                    var after = Square.Match(tail, w.Index + w.Length);
                    if (after.Success && after.Index - (w.Index + w.Length) <= 8) at = after.Groups[1].Value;
                }
                result.Add((sq.Groups[1].Value, type, at, false));
            }
        }
        return result.Distinct().ToList();
    }

    private static char? TypeOf(string word)
    {
        foreach (var (w, t) in PieceWords)
            if (w.IsMatch(word)) return t;
        return null;
    }

    // ── Bauernstruktur (1.001.1) ────────────────────────────────────────────────────────────────────

    private static readonly (Regex Word, Func<PawnStructure.Flags, bool> Holds)[] PawnClaims =
    [
        (new(@"isoliert\w*|Isolani\w*|isolated", RegexOptions.Compiled | RegexOptions.IgnoreCase), x => x.Isolated),
        (new(@"Freibauer\w*|passed\s+pawn\w*|\bpasser\w*|\bis\s+passed", RegexOptions.Compiled | RegexOptions.IgnoreCase), x => x.Passed),
        (new(@"Doppelbauer\w*|doubled", RegexOptions.Compiled | RegexOptions.IgnoreCase), x => x.Doubled),
        (new(@"rückständig\w*|backward", RegexOptions.Compiled | RegexOptions.IgnoreCase), x => x.Backward),
    ];

    private static readonly Regex Negation = new(@"(?:nicht|kein\w*|not|no|never)\s+(?:\w+\s+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Die Felder, deren Bauer der Text fälschlich „isoliert/Isolani“, „Freibauer“, „Doppelbauer“ oder „rückständig“ nennt
    /// (Befund Vereinspartie 261: Strukturaussagen nur aus Fakten, <see cref="PawnStructure"/>). Feld wie bei
    /// <see cref="ClaimedSquares"/> (nächstes danach, sonst davor); verneinte Aussagen („nicht isoliert“) zählen nicht.
    /// Steht in den Stellungen der Linien dort ein Bauer, der die Eigenschaft NIE hat, ist die Aussage falsch; steht dort nie
    /// ein Bauer, bleibt sie unbeanstandet.
    /// </summary>
    internal static List<string> FalsePawnClaims(string text, MoveComparisonPrompts.PairFacts f)
    {
        var claims = new List<(string Square, Func<PawnStructure.Flags, bool> Holds)>();
        foreach (var sentence in Regex.Split(text, @"(?<=[.!?;:])\s+|\n"))
        {
            var squares = Square.Matches(sentence).Where(sq => !SquareWord.IsMatch(sentence[Math.Max(0, sq.Index - 30)..sq.Index])).ToList();
            foreach (var (word, holds) in PawnClaims)
                foreach (Match k in word.Matches(sentence))
                {
                    if (Negation.IsMatch(sentence[Math.Max(0, k.Index - 20)..k.Index])) continue;
                    var sq = squares.Where(x => x.Index >= k.Index + k.Length && x.Index - (k.Index + k.Length) <= 25).OrderBy(x => x.Index).FirstOrDefault()
                             ?? squares.Where(x => x.Index + x.Length <= k.Index && k.Index - (x.Index + x.Length) <= 40).OrderByDescending(x => x.Index).FirstOrDefault();
                    if (sq is not null) claims.Add((sq.Groups[1].Value, holds));
                }
        }
        if (claims.Count == 0) return [];
        var geos = Positions(f).Select(BoardGeometry.FromFen).Where(g => g is not null).ToList();
        var wrong = new List<string>();
        foreach (var (square, holds) in claims)
        {
            var flags = geos.Select(g => PawnStructure.Of(g!, square)).Where(x => x is not null).ToList();
            if (flags.Count > 0 && !flags.Any(x => holds(x!)) && !wrong.Contains(square)) wrong.Add(square);
        }
        return wrong;
    }

    /// <summary>Alle drei Prüfungen zusammen: (ungedeckt, nur einfach gedeckt, Bauernstruktur).</summary>
    internal static (List<string> Undefended, List<string> Sole, List<string> Pawns) Check(string text, MoveComparisonPrompts.PairFacts f)
        => (FalseUndefended(text, f), FalseSoleDefender(text, f), FalsePawnClaims(text, f));

    /// <summary>Die Hinweiszeile für alle Prüfungen (ungedeckt, „nur von X gedeckt“, Bauernstruktur).</summary>
    internal static string Hint(IReadOnlyList<string> undefended, IReadOnlyList<string> sole, bool german, IReadOnlyList<string>? pawns = null)
    {
        var parts = new List<string>();
        if (pawns is { Count: > 0 })
            parts.Add(german
                ? $"WICHTIG: Deine letzte Antwort beschrieb den Bauern auf {ExplanationFacts.JoinAnd(pawns)} falsch (isoliert, Freibauer, "
                  + "Doppelbauer oder rückständig) — das stimmt in den Stellungen der Linien nicht. Strukturaussagen nur aus einem "
                  + "Faktum „pawn structure …“."
                : $"IMPORTANT: your previous answer described the pawn on {ExplanationFacts.JoinAnd(pawns)} wrongly (isolated, passed, "
                  + "doubled or backward). Describe the pawn structure only from a \"pawn structure\" fact.");
        if (undefended.Count > 0) parts.Add(Hint(undefended, german));
        if (sole.Count > 0)
            parts.Add(german
                ? $"WICHTIG: Deine letzte Antwort nannte die Figur auf {ExplanationFacts.JoinAnd(sole)} nur von einer Figur gedeckt — "
                  + "das stimmt in den Stellungen der Linien nicht (siehe die Fakten „defended by …“ und die Figurenliste). "
                  + "„Nur von … gedeckt“ oder „einzige Deckung“ nur, wenn ein Faktum genau diese eine Deckung nennt."
                : $"IMPORTANT: your previous answer said the piece on {ExplanationFacts.JoinAnd(sole)} is defended by only one piece — "
                  + "that is not true in the positions of the lines. Say \"defended only by\" only if a fact names exactly that defender.");
        return string.Join(' ', parts);
    }

    /// <summary>Die Hinweiszeile für die neue Anfrage (Deutsch für Fable, Englisch für das lokale Modell).</summary>
    internal static string Hint(IReadOnlyList<string> squares, bool german) => german
        ? $"WICHTIG: Deine letzte Antwort nannte die Figur auf {ExplanationFacts.JoinAnd(squares)} ungedeckt bzw. hängend — "
          + "sie ist in den Stellungen der Linien gedeckt (siehe die Fakten „defended by …“ und die Figurenliste). "
          + "„Ungedeckt“, „hängt“ oder „ungeschützt“ nur, wenn ein Faktum „defended by nothing“ es belegt."
        : $"IMPORTANT: your previous answer called the piece on {ExplanationFacts.JoinAnd(squares)} undefended or hanging — "
          + "it is defended in the positions of the lines. Say \"undefended\" or \"hanging\" only if a fact says \"defended by nothing\".";

    /// <summary>Vor dem Zug, nach beiden Kandidaten und jede Stellung der gegebenen Linien.</summary>
    private static List<string> Positions(MoveComparisonPrompts.PairFacts f)
    {
        var result = new List<string> { f.Fen, f.BestFen, f.WeakFen };
        void Walk(string? fen, IEnumerable<string> sans)
        {
            if (fen is null) return;
            try
            {
                var board = ChessBoard.LoadFromFen(fen);
                result.Add(fen);
                foreach (var san in sans)
                {
                    var move = Array.Find(board.Moves(generateSan: true), m => m.San == san);
                    if (move is null) break;
                    board.Move(move);
                    result.Add(board.ToFen());
                }
            }
            catch
            {
                // Linie nicht nachzuspielen — was bis hierher gesammelt ist, stimmt.
            }
        }
        foreach (var p in f.WeakReplies) Walk(f.WeakFen, p.Line);
        foreach (var p in f.BestReplies) Walk(f.BestFen, p.Line);
        foreach (var t in f.Tests) Walk(f.BestFen, t.Answer.Prepend(t.ReplySan));
        foreach (var d in f.Defences ?? [])
        {
            Walk(d.FenBefore, [d.MainSan]);
            foreach (var x in d.Defences) Walk(x.FenAfter, x.Line);
        }
        foreach (var t in f.Tries ?? []) Walk(t.FenAfter, t.Line);
        return result.Distinct().ToList();
    }
}
