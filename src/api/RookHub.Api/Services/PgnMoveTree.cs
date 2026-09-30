using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>Ein Zug im PGN samt der NACH ihm abzweigenden Varianten.</summary>
public sealed record PgnMove(string San, List<List<PgnMove>> Variations);

/// <summary>Ein PGN-Abschnitt (eine Partie): die Header, die hier überhaupt jemand liest, plus den
/// Zugbaum. <see cref="StartFen"/> ist <c>null</c> = Grundstellung, <see cref="White"/>/<see
/// cref="Black"/> sind <c>null</c>, wenn der Header fehlt.</summary>
public sealed record ParsedSection(string? White, string? Black, string? StartFen, List<PgnMove> Moves);

/// <summary>
/// DER PGN-Baumparser des Servers: Abschnitte trennen, Movetext herausschneiden, tokenisieren,
/// Züge mit Varianten aufbauen. Deckt dieselben Fälle ab wie <c>parsePgnText</c> im Client.
///
/// <para>Er lag bis 0.499.6 ZWEIMAL da — in <see cref="RepertoireAnalyzeService"/> und in
/// <see cref="RepertoireLineSource"/> —, und zwar als wörtliche Kopie bis in den Kommentar zur
/// ChessBase-Schreibweise „1.e4" hinein. Beide Fassungen tragen Bugfixes, die jemand teuer
/// gefunden hat (der <c>[FEN]</c>-Header aus v0.340.0, die angeklebte Zugnummer); der nächste
/// solche Fund wäre mit ziemlicher Sicherheit nur in EINER von beiden gelandet.</para>
///
/// <para>Was NICHT hierher gehört, weil es eigene Semantik hat: <see cref="PgnParser"/>
/// (<c>ExtractMainlineSans</c> behandelt u. a. „1/2" als Ergebnis-Token und kanonisiert die SAN),
/// <see cref="ChessableTrainedLineService.MainlineSans"/> (überspringt Varianten ganz und weist
/// jedes Token MIT Punkt ab — wegen „e.p.") und <see cref="ReconstructionChain.SplitMoves"/>
/// (behält Suffix-Annotationen, entfernt nur die innersten Klammerpaare).</para>
/// </summary>
public static class PgnMoveTree
{
    private static readonly Regex CommentRegex = new(@"\{[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex LineCommentRegex = new(@";[^\n]*", RegexOptions.Compiled);
    private static readonly Regex NagRegex = new(@"\$\d+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex MoveNumberRegex = new(@"^\d+\.+$", RegexOptions.Compiled);
    /// <summary>Zugnummern samt Punkten IM Text (auch direkt am Zug: „1.e4", „12...Nf6").</summary>
    private static readonly Regex InlineMoveNumberRegex = new(@"\d+\.{1,3}", RegexOptions.Compiled);
    private static readonly Regex EventHeaderSplit = new(@"(?=\[Event\s)", RegexOptions.Compiled);
    private static readonly Regex WhiteHeaderRegex = new(@"^\[White\s+""([^""]*)""\]", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex BlackHeaderRegex = new(@"^\[Black\s+""([^""]*)""\]", RegexOptions.Compiled | RegexOptions.Multiline);
    // Chessable-Importe (via piratechess) tragen je Linie die Startstellung der Variante im
    // [FEN]-Header. Ohne ihn beginnt der Walk in der Grundstellung: der erste Zug ist dort illegal
    // (Linie fehlt still im Positions-Set) oder zufällig legal — dann gelten FALSCHE Stellungen als
    // „im Repertoire". Gefunden und behoben in v0.340.0.
    private static readonly Regex FenHeaderRegex = new(@"^\[FEN\s+""([^""]*)""\]", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly HashSet<string> ResultTokens = new() { "1-0", "0-1", "1/2-1/2", "*" };

    /// <summary>
    /// Tiefste Varianten-Schachtelung, die der Parser aufbaut (echte Repertoires: unter 20). Tiefere
    /// Klammern überspringt er wie einen Kommentar. Ohne Deckel rief sich <see cref="ParseMoveTokens"/>
    /// je „(" selbst auf, und jeder Walker über den Baum (Positions-Set, Stellungssuche, Linienquelle,
    /// Explorer) rekursiert je Variante noch einmal: eine hochgeladene Datei „1. e4 (((…" mit
    /// 100 000 Klammern sprengte den Stack — ein StackOverflow ist in .NET nicht fangbar und beendet
    /// die ganze API, bei jedem weiteren Aufbau des Positions-Sets erneut (Codereview 2026-09-29, A6-001).
    /// </summary>
    public const int MaxVariationDepth = 64;

    /// <summary>
    /// Zerlegt ein PGN in seine Abschnitte (Trenner: der nächste <c>[Event </c>-Header) und parst
    /// jeden davon.
    /// <para>ZUG-LOSE Abschnitte bleiben in der Liste (könnten Kapitel-Intros sein): an der
    /// Position eines Abschnitts hängt der <c>gameIndex</c>, über den Client und Server dieselbe
    /// Linie meinen — ein stillschweigend übersprungener Abschnitt verschöbe alle folgenden. Wer
    /// sie nicht braucht, filtert selbst auf <c>Moves.Count &gt; 0</c>.</para>
    /// </summary>
    public static List<ParsedSection> ParseSections(string text)
    {
        var sections = new List<ParsedSection>();
        if (string.IsNullOrWhiteSpace(text)) return sections;
        foreach (var section in EventHeaderSplit.Split(text))
        {
            if (string.IsNullOrWhiteSpace(section)) continue;
            var movetext = ExtractMovetext(section);
            var moves = movetext.Length == 0 ? new List<PgnMove>() : ParseMoveTokens(Tokenize(movetext), 0).Moves;
            var white = WhiteHeaderRegex.Match(section);
            var black = BlackHeaderRegex.Match(section);
            sections.Add(new ParsedSection(
                white.Success ? white.Groups[1].Value.Trim() : null,
                black.Success ? black.Groups[1].Value.Trim() : null,
                StartFenOf(section),
                moves));
        }
        return sections;
    }

    /// <summary>Der Movetext eines Abschnitts: Headers sind Zeilen, die mit '[' beginnen und mit ']'
    /// enden; danach kommt der Rest.</summary>
    internal static string ExtractMovetext(string section)
    {
        var lines = section.Split('\n');
        var sb = new System.Text.StringBuilder();
        bool pastHeaders = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']') && !pastHeaders) continue;
            if (line.Length == 0 && !pastHeaders) { pastHeaders = true; continue; }
            if (pastHeaders || !line.StartsWith('['))
            {
                sb.Append(line).Append(' ');
                pastHeaders = true;
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>Movetext → Tokens; Klammern werden zu eigenen Tokens „(" und „)".</summary>
    internal static List<string> Tokenize(string movetext)
    {
        movetext = CommentRegex.Replace(movetext, " ");
        movetext = LineCommentRegex.Replace(movetext, " ");
        movetext = NagRegex.Replace(movetext, " ");
        // Zugnummern ERSETZEN, nicht nur als eigenes Token erkennen: ChessBase, Fritz und SCID
        // exportieren „1.e4" OHNE Leerzeichen. Ein solches Token fiel durch `IsMoveToken` (beginnt
        // mit einer Ziffer) und wurde STILL verworfen — damit fehlten ALLE Weißzüge der Datei, das
        // Nachspielen brach am ersten Halbzug ab und Stellungssuche, Baummodus und die
        // Abweichungs-Analyse fanden nichts, während derselbe Inhalt im Trainer einwandfrei lief
        // (der Client-Parser und der Kurs-Import machen genau diese Ersetzung schon).
        movetext = InlineMoveNumberRegex.Replace(movetext, " ");
        movetext = WhitespaceRegex.Replace(movetext, " ").Trim();

        var tokens = new List<string>();
        int i = 0;
        while (i < movetext.Length)
        {
            char c = movetext[i];
            if (c == '(') { tokens.Add("("); i++; }
            else if (c == ')') { tokens.Add(")"); i++; }
            else if (c == ' ') { i++; }
            else
            {
                int j = i;
                while (j < movetext.Length && movetext[j] != ' ' && movetext[j] != '(' && movetext[j] != ')') j++;
                tokens.Add(movetext.Substring(i, j - i));
                i = j;
            }
        }
        return tokens;
    }

    /// <summary>Tokens → Zugbaum ab <paramref name="pos"/>; eine Klammer hängt ihre Züge als
    /// Variante an den zuletzt gelesenen Zug. <paramref name="depth"/> = Schachtelungstiefe der
    /// gerade gelesenen Zugfolge (0 = Hauptlinie); Varianten tiefer als <see cref="MaxVariationDepth"/>
    /// fallen weg.</summary>
    internal static (List<PgnMove> Moves, int EndPos) ParseMoveTokens(List<string> tokens, int pos, int depth = 0)
    {
        var moves = new List<PgnMove>();
        while (pos < tokens.Count)
        {
            var token = tokens[pos];
            if (token == ")") return (moves, pos);
            if (token == "(")
            {
                pos++; // skip '('
                if (depth >= MaxVariationDepth)
                {
                    pos = SkipVariation(tokens, pos) + 1; // samt allem darin, OHNE Rekursion
                    continue;
                }
                var (varMoves, endPos) = ParseMoveTokens(tokens, pos, depth + 1);
                pos = endPos + 1; // skip ')'
                if (moves.Count > 0) moves[^1].Variations.Add(varMoves);
                continue;
            }
            if (IsMoveToken(token))
            {
                // Suffix-Annotationen am SAN entfernen, damit chess-lib parsen kann.
                var clean = token.TrimEnd('!', '?', '+', '#');
                if (clean.Length > 0)
                    moves.Add(new PgnMove(clean, new List<List<PgnMove>>()));
            }
            pos++;
        }
        return (moves, pos);
    }

    /// <summary>Überspringt eine Variante (ab dem Token NACH ihrer „(") samt allen darin geschachtelten,
    /// iterativ. Liefert die Position ihrer schließenden „)" bzw. <c>tokens.Count</c>, wenn sie fehlt —
    /// dieselbe EndPos, die <see cref="ParseMoveTokens"/> für diese Variante geliefert hätte.</summary>
    private static int SkipVariation(List<string> tokens, int pos)
    {
        int open = 0;
        for (; pos < tokens.Count; pos++)
        {
            if (tokens[pos] == "(") open++;
            else if (tokens[pos] == ")") { if (open == 0) return pos; open--; }
        }
        return pos;
    }

    /// <summary>Tiefste Varianten-Schachtelung im ganzen Text; Klammern in Kommentaren zählen nicht
    /// (dieselbe Zerlegung wie <see cref="Tokenize"/>). Für die Upload-Prüfung gegen
    /// <see cref="MaxVariationDepth"/>.</summary>
    public static int VariationDepthOf(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('(') < 0) return 0;
        int depth = 0, max = 0;
        foreach (var token in Tokenize(text))
        {
            if (token == "(") { if (++depth > max) max = depth; }
            else if (token == ")" && depth > 0) depth--;
        }
        return max;
    }

    /// <summary>Ist das Token ein Zug? Klammern, alleinstehende Zugnummern und Ergebnis-Tokens sind
    /// keiner; sonst entscheidet das erste Zeichen (Linie a–h, Figur oder die Rochade „O").
    /// <para>Bewusst OHNE die Regel „ein Punkt im Token = kein Zug", die
    /// <see cref="ChessableTrainedLineService"/> braucht: hier hat <see cref="Tokenize"/> die
    /// angeklebten Zugnummern schon ersetzt, und die Regel würde zusätzlich „e.p." aussieben —
    /// eine Änderung am Ergebnis, nicht bloss eine Aufräumung.</para></summary>
    internal static bool IsMoveToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token == "(" || token == ")") return false;
        if (IsMoveNumber(token)) return false;
        if (IsResultToken(token)) return false;
        char c = token[0];
        return (c >= 'a' && c <= 'h') || c == 'K' || c == 'Q' || c == 'R' || c == 'B' || c == 'N' || c == 'O';
    }

    /// <summary>Ein Token, das NUR aus einer Zugnummer besteht („1.", „12...").</summary>
    internal static bool IsMoveNumber(string token) => MoveNumberRegex.IsMatch(token);

    /// <summary>Ein Partie-Ergebnis am Ende des Movetexts.</summary>
    internal static bool IsResultToken(string token) => ResultTokens.Contains(token);

    /// <summary>Start-FEN eines PGN-Abschnitts (<c>[FEN "…"]</c>), sonst <c>null</c> =
    /// Grundstellung. Ein leerer Header zählt als KEINE Startstellung — sonst stürbe der Walk an
    /// einer leeren FEN, statt in der Grundstellung zu beginnen.</summary>
    public static string? StartFenOf(string? section)
    {
        var m = FenHeaderRegex.Match(section ?? string.Empty);
        var fen = m.Success ? m.Groups[1].Value.Trim() : null;
        return string.IsNullOrWhiteSpace(fen) ? null : fen;
    }
}
