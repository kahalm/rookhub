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
/// eindeutig einer Stellung zuordnen).
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
