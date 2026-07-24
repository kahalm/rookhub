using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Chess;

namespace RookHub.Api.Services;

/// <summary>
/// Reine, DB-freie PGN-Parsing-Engine (ChessBase-/Standard-PGN). Enthält das Spiel-Splitting,
/// die SAN→UCI-Umwandlung der Hauptvariante (via Gera.Chess), die Kommentar-Extraktion sowie das
/// Parsen der ChessBase-/Chessable-Marker (<c>[%tqu]</c> Trainingsstart, <c>[%cal]</c>/<c>[%csl]</c>
/// Board-Annotationen). Von <see cref="PgnImportService"/> genutzt und unabhängig testbar.
/// </summary>
public static partial class PgnParser
{
    // ---- regex helpers (vorkompiliert) -----------------------------------
    [GeneratedRegex(@"^\s*\[\s*([A-Za-z][A-Za-z0-9_]*)\s+""(.*)""\s*\]\s*$")]
    private static partial Regex HeaderLineRegex();
    [GeneratedRegex(@"\[%\w+[^\]]*\]")]            // [%tqu ...], [%cal ...], [%csl ...]
    private static partial Regex AnnotationRegex();
    [GeneratedRegex(@"\[%(cal|csl)\s+([^\]]*)\]")] // farbige Pfeile / Feld-Markierungen (Chessable)
    private static partial Regex CalCslRegex();
    [GeneratedRegex(@"\[%alt\s+([^\]]*)\]")]       // von Chessable geduldete Alternativzüge (softFail)
    private static partial Regex AltRegex();
    [GeneratedRegex(@"\{[^}]*\}")]                 // Kommentare
    private static partial Regex CommentRegex();
    [GeneratedRegex(@"\$\d+")]                     // NAGs
    private static partial Regex NagRegex();
    [GeneratedRegex(@"\d+\.+")]                    // Zugnummern "12." / "12..."
    private static partial Regex MoveNumberRegex();
    [GeneratedRegex(@"^([a-h]?x?[a-h][18])=?([nbrqNBRQ])$")] // Umwandlung "a1Q"/"a1q"/"a1=q" → kanonisch "a1=Q"
    private static partial Regex PromotionRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private static readonly string[] ResultTokens = { "1-0", "0-1", "1/2-1/2", "1/2", "*" };

    /// <summary>Standard-Grundstellung (für synthetische Info-Linien ohne eigene Züge).</summary>
    public const string StartPositionFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    /// <summary>Obergrenze für gespeicherte Kommentar-Texte (Einleitung + Pro-Zug-Kommentare). Großzügig,
    /// da Chessable-Erklär-/Intro-Linien mehrere Tausend Zeichen lang sein können (früher hart bei 5000
    /// gekappt → lange Intros abgeschnitten); nur als Missbrauchs-/Sanity-Schranke, Spalte ist LONGTEXT.</summary>
    public const int MaxCommentLength = 100_000;

    /// <summary>Kürzt einen String auf <paramref name="max"/> Zeichen (Sanity-/Spalten-Schranke).</summary>
    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    // ---- Spiel-Splitting (Header-Block + Movetext) ------------------------
    /// <summary>Zerlegt einen PGN-Text in (Header-Dictionary, Movetext)-Paare je Spiel.</summary>
    public static IEnumerable<(Dictionary<string, string> Headers, string MoveText)> SplitGames(string pgnText)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var moves = new StringBuilder();
        bool inMoves = false;
        bool hasContent = false;
        // Offene {…}-Kommentar-Klammertiefe über Zeilenumbrüche hinweg: eine umbruch-bedingte
        // Fortsetzungszeile eines Kommentars kann mit '[' beginnen ([%cal ...], [%tqu ...]) oder
        // sogar wie ein Header aussehen — solche Zeilen sind KOMMENTAR-INHALT, kein Tag/Header.
        int openComments = 0;

        foreach (var rawLine in pgnText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var m = openComments == 0 ? HeaderLineRegex().Match(rawLine) : Match.Empty;
            if (m.Success)
            {
                // Neuer Header nach Movetext ⇒ vorheriges Spiel abschließen. Ebenso bei einem
                // WIEDERHOLTEN Header-Key ohne Movetext (Header-only-Spiel, z. B. verwaiste Tags):
                // sonst würden die Header des vorherigen Spiels (etwa dessen FEN) still in das
                // nächste Spiel hineingemischt.
                if (inMoves || (hasContent && headers.ContainsKey(m.Groups[1].Value)))
                {
                    yield return (headers, moves.ToString());
                    headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    moves = new StringBuilder();
                    inMoves = false;
                    hasContent = false;
                }
                headers[m.Groups[1].Value] = m.Groups[2].Value;
                hasContent = true;
            }
            else if (openComments == 0 && rawLine.TrimStart().StartsWith('['))
            {
                // Tag-artige Zeile AUSSERHALB eines Kommentars, die nicht das Header-Muster trifft —
                // ignorieren ([%…]-Annotationen stehen laut Spez nur INNERHALB von {…}-Kommentaren).
                continue;
            }
            else if (!string.IsNullOrWhiteSpace(rawLine))
            {
                moves.Append(rawLine).Append(' ');
                inMoves = true;
                hasContent = true;
                // Klammertiefe fortschreiben ({…} nestet in PGN nicht; '}' darf laut Spez nicht im
                // Kommentartext vorkommen) — nie unter 0 (verirrte '}' nicht verschleppen).
                foreach (var ch in rawLine)
                {
                    if (ch == '{') openComments++;
                    else if (ch == '}' && openComments > 0) openComments--;
                }
            }
        }
        if (hasContent)
            yield return (headers, moves.ToString());
    }

    // ---- erster (nicht-leerer) Mainline-Kommentar -------------------------
    /// <summary>Erster Mainline-Kommentar (bricht wie import_books.py beim ersten ab, auch wenn leer).</summary>
    public static string? ExtractFirstComment(string moveText)
    {
        foreach (Match m in CommentRegex().Matches(moveText))
        {
            var inner = m.Value.Trim('{', '}');
            var cleaned = WhitespaceRegex().Replace(AnnotationRegex().Replace(inner, ""), " ").Trim();
            // import_books.py bricht beim ersten Kommentar ab, auch wenn er leer wird
            return string.IsNullOrEmpty(cleaned) ? null : Truncate(cleaned, MaxCommentLength);
        }
        return null;
    }

    // ---- erster NICHT-leerer Kommentar (über alle Kommentare hinweg) ------
    /// <summary>Wie <see cref="ExtractFirstComment"/>, bricht aber NICHT beim ersten (evtl. leeren)
    /// Kommentar ab, sondern liefert den ersten Kommentar mit echtem Text. Nur für Info-Linien:
    /// Chessable-Kapitel-Intros haben als ersten Kommentar bloß den leeren <c>{[%info]}</c>-Marker,
    /// der Erklärtext folgt erst im Zug-Kommentar nach dem NULL-Zug <c>1. --</c>.</summary>
    public static string? FirstNonEmptyComment(string moveText)
    {
        foreach (Match m in CommentRegex().Matches(moveText))
        {
            var inner = m.Value.Trim('{', '}');
            var cleaned = WhitespaceRegex().Replace(AnnotationRegex().Replace(inner, ""), " ").Trim();
            if (!string.IsNullOrEmpty(cleaned)) return Truncate(cleaned, MaxCommentLength);
        }
        return null;
    }

    // ---- Pro-Zug-Kommentare der Hauptlinie --------------------------------
    /// <summary>
    /// Sammelt alle Hauptlinien-Kommentare je Halbzug. Schlüssel = 0-basierter Halbzug-Index, NACH
    /// dessen Zug der Kommentar in der PGN steht; <c>-1</c> = Kommentar vor dem ersten Zug (Einleitung).
    /// Die Zählung läuft identisch zu <see cref="FindTquMoveIndex"/> / <see cref="TryExtractUciMainline"/>
    /// (nur Tiefe 0, Varianten <c>(…)</c> werden ignoriert), sodass die Schlüssel exakt zu den UCI-Zügen
    /// passen. <c>[%…]</c>-Annotationen werden entfernt, Whitespace normalisiert, leere Kommentare
    /// verworfen, mehrere Kommentare am selben Zug mit Leerzeichen verbunden. <c>null</c> = keine.
    /// </summary>
    public static Dictionary<int, string>? ExtractMoveComments(string moveText)
    {
        var map = new Dictionary<int, string>();
        int depth = 0;       // Variantentiefe
        int sanCount = 0;    // gezählte Hauptlinien-Züge
        int i = 0, n = moveText.Length;
        var cur = new StringBuilder();

        // Ein Hauptlinien-Kommentar, der (auf Chessable-Art) mit einem Verweis auf eine Fortsetzung
        // endet („…the continuation would have been", „better was …"), wird im PGN unmittelbar von
        // einer Variante fortgesetzt. Ohne Faltung endet der gespeicherte Kommentar mitten im Satz.
        // Daher: folgt einer NICHT-leeren Hauptlinien-Kommentar direkt eine Variante, hängen wir deren
        // kompakt gerenderten Inhalt (Züge + Zug-Kommentare) an den Kommentar an.
        bool commentTrailing = false;   // letzter depth-0-Token war ein nicht-leerer Kommentar
        int lastCommentKey = -1;

        void Flush()
        {
            if (cur.Length == 0) return;
            if (IsSanMove(cur.ToString())) { sanCount++; commentTrailing = false; }
            cur.Clear();
        }

        while (i < n)
        {
            char c = moveText[i];
            if (c == '{')
            {
                Flush();
                int j = moveText.IndexOf('}', i + 1);
                if (j < 0) j = n;
                if (depth == 0)
                {
                    var inner = moveText.Substring(i + 1, Math.Min(j, n) - (i + 1));
                    var cleaned = WhitespaceRegex().Replace(AnnotationRegex().Replace(inner, ""), " ").Trim();
                    if (cleaned.Length > 0)
                    {
                        int key = sanCount - 1; // Kommentar gehört zum zuletzt gezählten Zug (-1 = Einleitung)
                        map[key] = map.TryGetValue(key, out var prev)
                            ? Truncate($"{prev} {cleaned}", MaxCommentLength)
                            : Truncate(cleaned, MaxCommentLength);
                        commentTrailing = true;
                        lastCommentKey = key;
                    }
                }
                i = (j < n) ? j + 1 : n;
            }
            else if (c == '(' && depth == 0 && commentTrailing)
            {
                // Fortsetzungs-Variante direkt nach einem Kommentar → in den Kommentar falten.
                int end = MatchParen(moveText, i);
                var rendered = RenderVariation(moveText.Substring(i + 1, end - (i + 1)));
                if (rendered.Length > 0 && map.TryGetValue(lastCommentKey, out var prev))
                    map[lastCommentKey] = Truncate($"{prev} {rendered}", MaxCommentLength);
                i = end + 1; // ganze Variante konsumiert; commentTrailing bleibt (Folge-Varianten falten mit)
            }
            else if (c == '(') { Flush(); depth++; i++; }
            else if (c == ')') { Flush(); if (depth > 0) depth--; i++; }
            else if (char.IsWhiteSpace(c)) { Flush(); i++; }
            else { if (depth == 0) cur.Append(c); i++; }
        }
        Flush();
        return map.Count == 0 ? null : map;
    }

    /// <summary>Index der zu <paramref name="open"/> (einem '(') gehörenden schließenden ')'; bei
    /// unbalanciertem PGN das Textende.</summary>
    private static int MatchParen(string s, int open)
    {
        int d = 0;
        for (int k = open; k < s.Length; k++)
        {
            if (s[k] == '(') d++;
            else if (s[k] == ')') { d--; if (d == 0) return k; }
        }
        return s.Length - 1;
    }

    /// <summary>Rendert den Inhalt einer Varianten-Fortsetzung kompakt als Fließtext: Züge bleiben stehen,
    /// Kommentar-Klammern <c>{…}</c> werden entfernt (Text inline), <c>[%…]</c>-Marker und NAGs raus,
    /// Whitespace kollabiert.</summary>
    private static string RenderVariation(string inner)
    {
        var noAnn = AnnotationRegex().Replace(inner, "");
        var noNag = NagRegex().Replace(noAnn, "");
        var noBraces = noNag.Replace("{", " ").Replace("}", " ").Replace("(", " ").Replace(")", " ");
        return WhitespaceRegex().Replace(noBraces, " ").Trim();
    }

    /// <summary>Eine Board-Annotation aus <c>[%cal]</c>/<c>[%csl]</c>: Pfeil (o→d) bzw. Feld-Markierung
    /// (nur o). <c>b</c> = chessground-Brush (green/red/blue/yellow). Kompakte JSON-Feldnamen o/d/b.</summary>
    public record MoveShape(
        [property: JsonPropertyName("o")] string O,
        [property: JsonPropertyName("d")] string? D,
        [property: JsonPropertyName("b")] string B);

    private static readonly Dictionary<char, string> BrushByColor = new()
    { ['G'] = "green", ['R'] = "red", ['B'] = "blue", ['Y'] = "yellow" };

    /// <summary>Parst die <c>[%cal …]</c>-Pfeile und <c>[%csl …]</c>-Feld-Markierungen eines Kommentars
    /// (kommagetrennte Tokens „Farbe+Feld[+Feld]", z. B. <c>Gd8g8</c> = grüner Pfeil d8→g8, <c>Rg8</c> =
    /// rotes Feld g8).</summary>
    private static List<MoveShape> ParseShapes(string inner)
    {
        var list = new List<MoveShape>();
        foreach (Match m in CalCslRegex().Matches(inner))
        {
            bool arrow = m.Groups[1].Value.Equals("cal", StringComparison.OrdinalIgnoreCase);
            foreach (var tok in m.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (tok.Length < 3) continue;
                var brush = BrushByColor.TryGetValue(char.ToUpperInvariant(tok[0]), out var b) ? b : "green";
                var coords = tok[1..];
                if (arrow && coords.Length >= 4) list.Add(new MoveShape(coords[..2], coords.Substring(2, 2), brush));
                else if (!arrow && coords.Length >= 2) list.Add(new MoveShape(coords[..2], null, brush));
            }
        }
        return list;
    }

    /// <summary>Sammelt je Halbzug die Board-Annotationen (Pfeile/Feld-Markierungen) — Schlüssel-Konvention
    /// identisch zu <see cref="ExtractMoveComments"/> (<c>-1</c> = vor dem ersten Zug). <c>null</c> = keine.</summary>
    public static Dictionary<int, List<MoveShape>>? ExtractMoveShapes(string moveText)
    {
        var map = new Dictionary<int, List<MoveShape>>();
        int depth = 0, sanCount = 0, i = 0, n = moveText.Length;
        var cur = new StringBuilder();
        void Flush() { if (cur.Length == 0) return; if (IsSanMove(cur.ToString())) sanCount++; cur.Clear(); }
        while (i < n)
        {
            char c = moveText[i];
            if (c == '{')
            {
                Flush();
                int j = moveText.IndexOf('}', i + 1);
                if (j < 0) j = n;
                if (depth == 0)
                {
                    var shapes = ParseShapes(moveText.Substring(i + 1, Math.Min(j, n) - (i + 1)));
                    if (shapes.Count > 0)
                    {
                        int key = sanCount - 1;
                        if (map.TryGetValue(key, out var existing)) existing.AddRange(shapes);
                        else map[key] = shapes;
                    }
                }
                i = (j < n) ? j + 1 : n;
            }
            else if (c == '(') { Flush(); depth++; i++; }
            else if (c == ')') { Flush(); if (depth > 0) depth--; i++; }
            else if (char.IsWhiteSpace(c)) { Flush(); i++; }
            else { if (depth == 0) cur.Append(c); i++; }
        }
        Flush();
        return map.Count == 0 ? null : map;
    }

    // ---- Hauptvariante als UCI --------------------------------------------
    /// <summary>Extrahiert die Hauptvariante als UCI-Zugliste (Kommentare/Varianten/NAGs/Zugnummern/
    /// Ergebnis entfernt, SAN→UCI via Gera.Chess). <c>null</c> bei ungültiger FEN / nicht spielbarem SAN.</summary>
    public static List<string>? TryExtractUciMainline(string fen, string moveText)
    {
        var sanMoves = ExtractMainlineSans(moveText);
        if (sanMoves.Count == 0) return null;

        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            foreach (var san in sanMoves)
            {
                if (!board.Move(san)) return null;
            }
            var uci = new List<string>(board.ExecutedMoves.Count);
            foreach (var mv in board.ExecutedMoves)
                uci.Add(ToUci(mv));
            return uci;
        }
        catch
        {
            return null; // ungültige FEN oder nicht spielbarer SAN ⇒ Eintrag überspringen
        }
    }

    /// <summary>
    /// Fallback-Zugauflösung NUR für <b>illegale</b> Diagramm-FENs (Chessable-Muster-/Info-Seiten ohne
    /// König o. Ä.), die Gera.Chess ablehnt. Für lade-bare (legale) FENs bewusst <c>null</c> — dort ist
    /// der strikte <see cref="TryExtractUciMainline"/> zuständig (scheitert er dort, wird NICHT geraten).
    /// Ergebnis dient nur dem Durchklicken der Info-Linie (siehe <see cref="PermissiveSan"/>).
    /// </summary>
    public static List<string>? TryExtractUciMainlinePermissive(string fen, string moveText)
    {
        try { ChessBoard.LoadFromFen(fen); return null; }   // legale FEN → nicht hier auflösen
        catch { /* illegale FEN → weiter unten permissiv auflösen */ }
        var sanMoves = ExtractMainlineSans(moveText);
        if (sanMoves.Count == 0) return null;
        return PermissiveSan.TryResolve(fen, sanMoves);
    }

    private static string ToUci(Move m)
    {
        var u = m.OriginalPosition.ToString() + m.NewPosition.ToString();
        var ss = m.Parameter?.ShortStr;
        if (!string.IsNullOrEmpty(ss) && ss.StartsWith('=') && ss.Length >= 2)
            u += char.ToLowerInvariant(ss[1]);
        return u;
    }

    /// <summary>SAN einzeln von Zug-Dekorationen bereinigen (0-0→O-O, Suffixe !?+# weg,
    /// Umwandlung → kanonisch "=Q"). Leer/Ergebnis → "".</summary>
    private static string CleanSan(string token)
    {
        var t = token.Trim();
        if (t.Length == 0 || ResultTokens.Contains(t)) return "";
        t = t.Replace("0-0-0", "O-O-O").Replace("0-0", "O-O").TrimEnd('!', '?', '+', '#');
        // Chessable/piratechess schreiben Umwandlungen ohne "=" (bzw. mit kleinem Figurbuchstaben),
        // z. B. "a1Q+"/"exd8n" → nach dem Suffix-Strip "a1Q"/"exd8n". Gera.Chess akzeptiert SAN aber
        // NUR in der Form "a1=Q" (großer Figurbuchstabe, mit "="); sonst wirft board.Move → die ganze
        // Linie fällt in den zug-losen Info-Zweig (Puzzle wird still zu einer statischen Info-Seite).
        var pm = PromotionRegex().Match(t);
        if (pm.Success) t = pm.Groups[1].Value + "=" + char.ToUpperInvariant(pm.Groups[2].Value[0]);
        return ResultTokens.Contains(t) ? "" : t;
    }

    /// <summary>Hauptvariante als gereinigte SAN-Liste (Kommentare/Varianten/NAGs/Zugnummern/Ergebnis raus).</summary>
    private static List<string> ExtractMainlineSans(string moveText)
    {
        var s = CommentRegex().Replace(moveText, " ");
        s = RemoveVariations(s);
        s = NagRegex().Replace(s, " ");
        s = MoveNumberRegex().Replace(s, " ");

        var sanMoves = new List<string>();
        foreach (var tok in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = CleanSan(tok);
            if (t.Length > 0) sanMoves.Add(t);
        }
        return sanMoves;
    }

    // ---- Geduldete Alternativzüge ([%alt]) --------------------------------
    /// <summary>
    /// Sammelt je Halbzug die von Chessable geduldeten Alternativzüge (softFail → <c>[%alt …]</c>) und
    /// setzt sie SAN→UCI um. Anknüpfpunkt ist die Stellung VOR dem Hauptzug dieses Halbzugs (die Alternative
    /// ersetzt IHN). Schlüssel-Konvention identisch zu <see cref="ExtractMoveComments"/> (0-basierter Halbzug
    /// der Hauptlinie = Index in der UCI-Zugliste von <see cref="TryExtractUciMainline"/>). <c>null</c> = keine.
    /// </summary>
    public static Dictionary<int, List<string>>? ExtractAltMoves(string fen, string moveText)
    {
        // 1) [%alt …]-SANs je Halbzug einsammeln (gleicher Scanner-Aufbau wie ExtractMoveShapes).
        var altSanByPly = new Dictionary<int, List<string>>();
        {
            int depth = 0, sanCount = 0, i = 0, n = moveText.Length;
            var cur = new StringBuilder();
            void Flush() { if (cur.Length == 0) return; if (IsSanMove(cur.ToString())) sanCount++; cur.Clear(); }
            while (i < n)
            {
                char c = moveText[i];
                if (c == '{')
                {
                    Flush();
                    int j = moveText.IndexOf('}', i + 1);
                    if (j < 0) j = n;
                    if (depth == 0)
                    {
                        var inner = moveText.Substring(i + 1, Math.Min(j, n) - (i + 1));
                        foreach (Match m in AltRegex().Matches(inner))
                        {
                            int key = sanCount - 1;             // Alt gehört zum zuletzt gezählten Zug
                            if (key < 0) continue;
                            var sans = m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (sans.Length == 0) continue;
                            if (!altSanByPly.TryGetValue(key, out var l)) { l = []; altSanByPly[key] = l; }
                            l.AddRange(sans);
                        }
                    }
                    i = (j < n) ? j + 1 : n;
                }
                else if (c == '(') { Flush(); depth++; i++; }
                else if (c == ')') { Flush(); if (depth > 0) depth--; i++; }
                else if (char.IsWhiteSpace(c)) { Flush(); i++; }
                else { if (depth == 0) cur.Append(c); i++; }
            }
            Flush();
        }
        if (altSanByPly.Count == 0) return null;

        // 2) SAN→UCI: Hauptlinie bis VOR den Zielhalbzug nachspielen, dann die Alt-SAN aus dieser Stellung.
        var mainSans = ExtractMainlineSans(moveText);
        if (mainSans.Count == 0) return null;

        var result = new Dictionary<int, List<string>>();
        foreach (var (key, sans) in altSanByPly)
        {
            if (key >= mainSans.Count) continue;
            var ucis = new List<string>();
            foreach (var rawSan in sans)
            {
                var altSan = CleanSan(rawSan);
                if (altSan.Length == 0) continue;
                try
                {
                    var board = ChessBoard.LoadFromFen(fen);
                    bool ok = true;
                    for (int p = 0; p < key; p++) { if (!board.Move(mainSans[p])) { ok = false; break; } }
                    if (!ok) break;                              // Hauptlinie nicht spielbar → Key überspringen
                    if (board.Move(altSan))
                    {
                        var u = ToUci(board.ExecutedMoves[^1]);
                        if (!ucis.Contains(u)) ucis.Add(u);
                    }
                }
                catch { /* ungültige FEN / nicht spielbarer Alt-SAN ⇒ diesen Zug überspringen */ }
            }
            if (ucis.Count > 0) result[key] = ucis;
        }
        return result.Count == 0 ? null : result;
    }

    // ---- Trainingsstart ([%tqu]) finden -----------------------------------
    /// <summary>
    /// Liefert den 0-basierten Index des Hauptlinien-Zugs, an dessen Folgekommentar das erste
    /// ChessBase-[%tqu] hängt (= Setup-Zug der Trainingsstellung). Sonderfälle:
    /// <list type="bullet">
    /// <item><c>null</c> = kein [%tqu] in der Hauptlinie (klassisches Verhalten).</item>
    /// <item><c>-1</c> = [%tqu] steht vor dem ersten Zug (Wurzel) → FEN ist bereits die
    /// Trainingsstellung, gelöst wird ab moves[0] ohne Setup-Zug.</item>
    /// </list>
    /// Varianten (…) und Kommentar-Inhalte werden übersprungen, sodass der Index exakt zur
    /// SAN-/UCI-Hauptliniensequenz von <see cref="TryExtractUciMainline"/> passt.
    /// </summary>
    public static int? FindTquMoveIndex(string moveText)
    {
        int depth = 0;       // Variantentiefe
        int sanCount = 0;    // gezählte Hauptlinien-Züge
        int i = 0, n = moveText.Length;
        var cur = new StringBuilder();

        void Flush()
        {
            if (cur.Length == 0) return;
            if (IsSanMove(cur.ToString())) sanCount++;
            cur.Clear();
        }

        while (i < n)
        {
            char c = moveText[i];
            if (c == '{')
            {
                Flush();
                int j = moveText.IndexOf('}', i + 1);
                if (j < 0) j = n;
                if (depth == 0)
                {
                    var content = moveText.Substring(i + 1, Math.Min(j, n) - (i + 1));
                    if (content.Contains("[%tqu", StringComparison.OrdinalIgnoreCase))
                        return sanCount - 1; // Kommentar gehört zum zuletzt gezählten Zug (-1 = Wurzel)
                }
                i = (j < n) ? j + 1 : n;
            }
            else if (c == '(') { Flush(); depth++; i++; }
            else if (c == ')') { Flush(); if (depth > 0) depth--; i++; }
            else if (char.IsWhiteSpace(c)) { Flush(); i++; }
            else { if (depth == 0) cur.Append(c); i++; }
        }
        Flush();
        return null;
    }

    /// <summary>Ist die FEN die Standard-Grundstellung (nur Brettfeld verglichen, ohne Zähler)?</summary>
    public static bool IsStartPosition(string fen)
        => fen.Split(' ', 2)[0] == "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";

    /// <summary>Ist das Token ein SAN-Zug (kein Zugnummern-, NAG- oder Ergebnis-Token)?</summary>
    private static bool IsSanMove(string token)
    {
        var t = MoveNumberRegex().Replace(token.Trim(), ""); // führende "12." / "12..." entfernen
        if (t.Length == 0 || t.StartsWith('$')) return false; // leer oder NAG
        t = t.Replace("0-0-0", "O-O-O").Replace("0-0", "O-O").TrimEnd('!', '?', '+', '#');
        if (t.Length == 0 || ResultTokens.Contains(t)) return false;
        return true;
    }

    private static string RemoveVariations(string s)
    {
        var sb = new StringBuilder(s.Length);
        int depth = 0;
        foreach (char c in s)
        {
            if (c == '(') depth++;
            else if (c == ')') { if (depth > 0) depth--; }
            else if (depth == 0) sb.Append(c);
        }
        return sb.ToString();
    }
}
