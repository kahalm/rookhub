using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Serverseitiges Parsen von ChessBase-/Standard-PGN-Dateien zu Buch-Puzzles.
/// Repliziert das Verhalten von rookhub/scripts/import_books.py:
/// FEN-Header + komplette Hauptvariante als UCI + erster Kommentar; SAN→UCI via Gera.Chess.
/// </summary>
public partial class PgnImportService
{
    private readonly AppDbContext _db;
    private readonly IBackgroundTaskQueue? _bgQueue;

    // bgQueue ist optional: per DI injiziert (reiht nach Import die Tipp-Generierung ein); bei direkter
    // Instanziierung (Tests) null → kein Enqueue.
    public PgnImportService(AppDbContext db, IBackgroundTaskQueue? bgQueue = null)
    {
        _db = db;
        _bgQueue = bgQueue;
    }

    // ---- regex helpers (vorkompiliert) -----------------------------------
    [GeneratedRegex(@"^\s*\[\s*([A-Za-z][A-Za-z0-9_]*)\s+""(.*)""\s*\]\s*$")]
    private static partial Regex HeaderLineRegex();
    [GeneratedRegex(@"\[%\w+[^\]]*\]")]            // [%tqu ...], [%cal ...], [%csl ...]
    private static partial Regex AnnotationRegex();
    [GeneratedRegex(@"\{[^}]*\}")]                 // Kommentare
    private static partial Regex CommentRegex();
    [GeneratedRegex(@"\$\d+")]                     // NAGs
    private static partial Regex NagRegex();
    [GeneratedRegex(@"\d+\.+")]                    // Zugnummern "12." / "12..."
    private static partial Regex MoveNumberRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private static readonly string[] ResultTokens = { "1-0", "0-1", "1/2-1/2", "1/2", "*" };

    /// <summary>Ein aus der PGN extrahiertes Puzzle (DB-frei, daher gut testbar).</summary>
    public record ParsedPuzzle(
        string LineId, string Round, string Fen, string Moves, int StartPly,
        string? Title, string? Chapter, string? Comment,
        Dictionary<int, string>? MoveComments = null, bool IsInfoOnly = false);

    /// <summary>
    /// Ergebnis eines PGN-Parses: extrahierte Puzzles + Anzahl der Spiele, die wegen
    /// fehlender/ungültiger Felder verworfen wurden (kein FEN/Round, keine spielbare
    /// Mainline, Grundstellung ohne Trainingsmarker etc.).
    /// </summary>
    public record ParseResult(List<ParsedPuzzle> Puzzles, int Invalid);

    /// <summary>Entfernt PGN-Suffixe für den Anzeigenamen (wie schach-bot _clean_book_name).</summary>
    public static string CleanDisplayName(string fileName)
    {
        if (fileName.EndsWith("_firstkey.pgn", StringComparison.OrdinalIgnoreCase))
            return fileName[..^"_firstkey.pgn".Length];
        if (fileName.EndsWith(".pgn", StringComparison.OrdinalIgnoreCase))
            return fileName[..^".pgn".Length];
        return fileName;
    }

    /// <summary>
    /// Parst einen PGN-Text in eine Liste von Puzzles. Reine Funktion (kein DB-Zugriff).
    /// Ungültige/unparsebare Einträge werden übersprungen und in <see cref="ParseResult.Invalid"/>
    /// gezählt, nicht geworfen.
    /// </summary>
    /// <summary>Standard-Grundstellung (für synthetische Info-Linien ohne eigene Züge).</summary>
    private const string StartPositionFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    /// <param name="keepCommentOnlyAsInfo">Wenn true (Buch-/Kurs-Import): zug-lose Linien MIT Erklärtext
    /// (Chessable-Intro-/Info-Seiten) werden nicht verworfen, sondern als Info-Linie behalten — Fake-Zug
    /// e4 ab Grundstellung, <c>IsInfoOnly</c>, damit der Text beim sequenziellen Durcharbeiten erscheint
    /// (kein Quiz, nicht in Random/Daily). Default false (z. B. Wochenpost = index-basiert, unverändert).</param>
    public static ParseResult ParsePgn(string fileName, string pgnText, bool keepCommentOnlyAsInfo = false)
    {
        var result = new List<ParsedPuzzle>();
        var invalid = 0;
        foreach (var (headers, moveText) in SplitGames(pgnText))
        {
            var fen = headers.GetValueOrDefault("FEN", "").Trim();
            var round = headers.GetValueOrDefault("Round", "").Trim();
            // Skip-Regeln wie import_books.py
            if (string.IsNullOrEmpty(fen) || fen == "?") { invalid++; continue; }
            if (string.IsNullOrEmpty(round) || round == "?") { invalid++; continue; }

            var comment = ExtractFirstComment(moveText);
            var moveComments = ExtractMoveComments(moveText);
            var uci = TryExtractUciMainline(fen, moveText);
            if (uci == null || uci.Count == 0)
            {
                // Zug-lose Linie mit Erklärtext (Chessable-Intro-/Info-Seite): nicht verwerfen, sondern
                // als Info-Linie behalten — Fake-Zug e4 ab Grundstellung, IsInfoOnly, nur sequenziell
                // zum Durchklicken (zeigt den Text). Ohne Text (leer) bleibt es ein Skip.
                if (keepCommentOnlyAsInfo && !string.IsNullOrEmpty(comment))
                {
                    var iw = headers.GetValueOrDefault("White", "").Trim();
                    var ib = headers.GetValueOrDefault("Black", "").Trim();
                    result.Add(new ParsedPuzzle(
                        LineId: Truncate($"{fileName}:{round}", 300),
                        Round: Truncate(round, 20),
                        Fen: StartPositionFen,
                        Moves: "e2e4",
                        StartPly: -1,
                        Title: iw.Length == 0 ? null : Truncate(iw, 300),
                        Chapter: ib.Length == 0 ? null : Truncate(ib, 200),
                        Comment: comment,
                        MoveComments: moveComments,
                        IsInfoOnly: true));
                    continue;
                }
                invalid++; continue;
            }

            // Info-/Erklärlinie? piratechess setzt [%info] für Chessable-IsInfo-Linien (kein [%tqu]).
            // Solche Linien werden nicht abgefragt, sondern nur durchgeklickt → IsInfoOnly markieren
            // und (anders als normale marker-lose Linien) auch aus der Grundstellung NICHT verwerfen.
            var isInfoOnly = moveText.Contains("[%info", StringComparison.OrdinalIgnoreCase);

            // Trainingsstart bestimmen. Zwei Buch-Typen kommen vor:
            //  (a) Mid-line-[%tqu]: ganze Partie ab Grundstellung, der Marker hängt an Zug k
            //      → StartPly = k, fen+moves bleiben die KOMPLETTE Partie, gelöst ab moves[k+1].
            //  (b) FEN ist bereits die Puzzle-Stellung (kein/Wurzel-Marker) → gelöst ab moves[0]
            //      → StartPly = -1.
            // Ausnahme: FEN = Grundstellung OHNE Mid-line-Marker = ganze Partie ohne definierten
            // Trainingsstart → kein Puzzle, überspringen (wie der Bot non-[%tqu]-Partien filtert).
            var tquIndex = FindTquMoveIndex(moveText);
            int startPly;
            if (tquIndex is int k && k >= 0 && k <= uci.Count - 2)
            {
                startPly = k;
            }
            else
            {
                // Grundstellung ohne Trainingsmarker = kein definierter Trainingsstart → verwerfen.
                // AUSNAHME: Info-Linien behalten wir (werden nicht abgefragt, nur durchgeklickt).
                if (IsStartPosition(fen) && !isInfoOnly) { invalid++; continue; }
                startPly = -1;
            }

            var white = headers.GetValueOrDefault("White", "").Trim();
            var black = headers.GetValueOrDefault("Black", "").Trim();

            result.Add(new ParsedPuzzle(
                LineId: Truncate($"{fileName}:{round}", 300),
                Round: Truncate(round, 20),
                Fen: fen,
                Moves: string.Join(' ', uci),
                StartPly: startPly,
                Title: string.IsNullOrEmpty(white) ? null : Truncate(white, 300),
                Chapter: string.IsNullOrEmpty(black) ? null : Truncate(black, 200),
                Comment: comment,
                MoveComments: moveComments,
                IsInfoOnly: isInfoOnly));
        }
        return new ParseResult(result, invalid);
    }

    // ---- Spiel-Splitting (Header-Block + Movetext) ------------------------
    private static IEnumerable<(Dictionary<string, string> Headers, string MoveText)> SplitGames(string pgnText)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var moves = new StringBuilder();
        bool inMoves = false;
        bool hasContent = false;

        foreach (var rawLine in pgnText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var m = HeaderLineRegex().Match(rawLine);
            if (m.Success)
            {
                // Neuer Header nach Movetext ⇒ vorheriges Spiel abschließen
                if (inMoves)
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
            else if (rawLine.TrimStart().StartsWith('['))
            {
                // Tag-artige Zeile, die nicht das Header-Muster trifft – ignorieren
                continue;
            }
            else if (!string.IsNullOrWhiteSpace(rawLine))
            {
                moves.Append(rawLine).Append(' ');
                inMoves = true;
                hasContent = true;
            }
        }
        if (hasContent)
            yield return (headers, moves.ToString());
    }

    /// <summary>Obergrenze für gespeicherte Kommentar-Texte (Einleitung + Pro-Zug-Kommentare). Großzügig,
    /// da Chessable-Erklär-/Intro-Linien mehrere Tausend Zeichen lang sein können (früher hart bei 5000
    /// gekappt → lange Intros abgeschnitten); nur als Missbrauchs-/Sanity-Schranke, Spalte ist LONGTEXT.</summary>
    private const int MaxCommentLength = 100_000;

    // ---- erster (nicht-leerer) Mainline-Kommentar -------------------------
    private static string? ExtractFirstComment(string moveText)
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

    // ---- Pro-Zug-Kommentare der Hauptlinie --------------------------------
    /// <summary>
    /// Sammelt alle Hauptlinien-Kommentare je Halbzug. Schlüssel = 0-basierter Halbzug-Index, NACH
    /// dessen Zug der Kommentar in der PGN steht; <c>-1</c> = Kommentar vor dem ersten Zug (Einleitung).
    /// Die Zählung läuft identisch zu <see cref="FindTquMoveIndex"/> / <see cref="TryExtractUciMainline"/>
    /// (nur Tiefe 0, Varianten <c>(…)</c> werden ignoriert), sodass die Schlüssel exakt zu den UCI-Zügen
    /// passen. <c>[%…]</c>-Annotationen werden entfernt, Whitespace normalisiert, leere Kommentare
    /// verworfen, mehrere Kommentare am selben Zug mit Leerzeichen verbunden. <c>null</c> = keine.
    /// </summary>
    private static Dictionary<int, string>? ExtractMoveComments(string moveText)
    {
        var map = new Dictionary<int, string>();
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
                    var inner = moveText.Substring(i + 1, Math.Min(j, n) - (i + 1));
                    var cleaned = WhitespaceRegex().Replace(AnnotationRegex().Replace(inner, ""), " ").Trim();
                    if (cleaned.Length > 0)
                    {
                        int key = sanCount - 1; // Kommentar gehört zum zuletzt gezählten Zug (-1 = Einleitung)
                        map[key] = map.TryGetValue(key, out var prev)
                            ? Truncate($"{prev} {cleaned}", MaxCommentLength)
                            : Truncate(cleaned, MaxCommentLength);
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
    private static List<string>? TryExtractUciMainline(string fen, string moveText)
    {
        // 1) Kommentare + Varianten + NAGs + Zugnummern + Ergebnis entfernen
        var s = CommentRegex().Replace(moveText, " ");
        s = RemoveVariations(s);
        s = NagRegex().Replace(s, " ");
        s = MoveNumberRegex().Replace(s, " ");

        var sanMoves = new List<string>();
        foreach (var tok in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = tok.Trim();
            if (t.Length == 0 || ResultTokens.Contains(t)) continue;
            t = t.Replace("0-0-0", "O-O-O").Replace("0-0", "O-O");
            t = t.TrimEnd('!', '?', '+', '#');
            if (t.Length == 0 || ResultTokens.Contains(t)) continue;
            sanMoves.Add(t);
        }
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

    private static string ToUci(Move m)
    {
        var u = m.OriginalPosition.ToString() + m.NewPosition.ToString();
        var ss = m.Parameter?.ShortStr;
        if (!string.IsNullOrEmpty(ss) && ss.StartsWith('=') && ss.Length >= 2)
            u += char.ToLowerInvariant(ss[1]);
        return u;
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
    private static int? FindTquMoveIndex(string moveText)
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
    private static bool IsStartPosition(string fen)
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

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    // ---- Persistenz -------------------------------------------------------
    /// <summary>
    /// Parst eine Datei und legt Book + BookPuzzles an. Neue Linien (per LineId) werden hinzugefügt,
    /// bereits vorhandene normalerweise übersprungen (idempotenter (Re-)Import / Resume).
    /// <para>AUSNAHME — Neu-Aufbereitung: Ist das Buch <b>veraltet</b> (<c>Book.ImportVersion &lt;
    /// <see cref="ImportPipeline.CurrentVersion"/></c>), werden bestehende Linien <b>in-place
    /// aktualisiert</b> (Moves/StartPly/Comment/MoveComments/Title/Chapter), statt sie zu überspringen
    /// — die BookPuzzle-Id bleibt erhalten, also auch aller Fortschritt/alle Statistiken, die darauf
    /// verweisen. So holt ein Re-Import eines Altbuchs die neuen abgeleiteten Felder nach.</para>
    /// Immer wird das Roh-PGN als <c>Book.SourcePgn</c> gespeichert und die Pipeline-Version
    /// hochgesetzt, damit das Buch künftig offline neu aufbereitbar ist.
    /// </summary>
    /// <summary>
    /// Erkennt eine Kapitel-Überschrift mit motivverratendem Titel („Chapter 2: Back-Rank Mates",
    /// „Kapitel 3: Abzugsschach") und behält nur das Label („Chapter 2"). Greift bewusst nur bei
    /// diesem Muster — freie Kapitelnamen ohne „Chapter/Kapitel N:"-Präfix bleiben unangetastet.
    /// </summary>
    private static readonly Regex ChapterSpoilerRx =
        new(@"^(\s*(?:chapter|kapitel|poglavlje)\s+\d+)\s*:\s*\S.*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Strippt den Spoiler-Teil eines Kapitelnamens (siehe <see cref="ChapterSpoilerRx"/>).</summary>
    public static string? StripChapterSpoiler(string? chapter)
    {
        if (string.IsNullOrEmpty(chapter)) return chapter;
        var m = ChapterSpoilerRx.Match(chapter);
        return m.Success ? m.Groups[1].Value.Trim() : chapter;
    }

    /// <summary>Kapitelname je Buchart: Puzzle-Bücher werden entschärft (Spoiler raus),
    /// Study-Bücher behalten ihre Kapitelnamen.</summary>
    private static string? ChapterForBook(BookKind kind, string? chapter)
        => kind == BookKind.Puzzle ? StripChapterSpoiler(chapter) : chapter;

    public async Task<BookImportItemDto> ImportFileAsync(string fileName, string pgnText, CancellationToken ct)
    {
        // Buch-/Kurs-Import: zug-lose Erklär-/Intro-Seiten als Info-Linien behalten (sequenziell durchklickbar).
        var (parsed, invalid) = ParsePgn(fileName, pgnText, keepCommentOnlyAsInfo: true);
        var now = DateTime.UtcNow;

        var book = await _db.Books.FirstOrDefaultAsync(b => b.FileName == fileName, ct);
        if (book == null)
        {
            book = new Book
            {
                FileName = Truncate(fileName, 200),
                DisplayName = Truncate(CleanDisplayName(fileName), 200),
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Books.Add(book);
            await _db.SaveChangesAsync(ct); // Id materialisieren
        }

        // Veraltetes Buch ⇒ bestehende Linien aktualisieren statt überspringen (Neu-Aufbereitung).
        var upgrade = book.ImportVersion < ImportPipeline.CurrentVersion;

        // Bestehende Linien laden: beim Upgrade als TRACKED Entities (zum Aktualisieren),
        // sonst nur die LineIds (leichtgewichtig, reines Dedup).
        var existing = upgrade
            ? await _db.BookPuzzles.Where(bp => bp.BookId == book.Id || bp.BookFileName == fileName)
                .ToDictionaryAsync(bp => bp.LineId, ct)
            : new Dictionary<string, BookPuzzle>();
        var existingLineIds = upgrade
            ? existing.Keys.ToHashSet()
            : await _db.BookPuzzles.Where(bp => bp.BookId == book.Id || bp.BookFileName == fileName)
                .Select(bp => bp.LineId).ToHashSetAsync(ct);

        var toAdd = new List<BookPuzzle>();
        var skipped = 0;
        var updated = 0;
        var seen = new HashSet<string>();

        foreach (var p in parsed)
        {
            if (!seen.Add(p.LineId)) { skipped++; continue; }      // Duplikat im selben Batch
            if (existingLineIds.Contains(p.LineId))
            {
                if (upgrade && existing.TryGetValue(p.LineId, out var bp))
                {
                    bp.Round = p.Round;
                    bp.Fen = p.Fen;
                    bp.Moves = p.Moves;
                    bp.StartPly = p.StartPly;
                    bp.Title = p.Title;
                    bp.Chapter = ChapterForBook(book.Kind, p.Chapter);
                    bp.Comment = p.Comment;
                    bp.MoveComments = p.MoveComments == null ? null : JsonSerializer.Serialize(p.MoveComments);
                    bp.IsInfoOnly = p.IsInfoOnly;
                    updated++;
                }
                else { skipped++; }
                continue;
            }
            toAdd.Add(new BookPuzzle
            {
                LineId = p.LineId,
                BookFileName = Truncate(fileName, 200),
                BookId = book.Id,
                Round = p.Round,
                Fen = p.Fen,
                Moves = p.Moves,
                StartPly = p.StartPly,
                Title = p.Title,
                Chapter = ChapterForBook(book.Kind, p.Chapter),
                Comment = p.Comment,
                MoveComments = p.MoveComments == null ? null : JsonSerializer.Serialize(p.MoveComments),
                IsInfoOnly = p.IsInfoOnly,
            });
        }

        if (toAdd.Count > 0) _db.BookPuzzles.AddRange(toAdd);

        // Roh-PGN als Reprocessing-Quelle merken + Pipeline-Version hochsetzen.
        book.SourcePgn = pgnText;
        book.ImportVersion = ImportPipeline.CurrentVersion;
        book.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        // Tipp-Generierung (LLM + Stockfish) asynchron anstoßen — blockiert den Import nicht.
        // HintGenerationService ist idempotent (überspringt aktuelle Tipps) und no-op ohne API-Key.
        if (_bgQueue != null && (toAdd.Count > 0 || updated > 0))
        {
            var puzzleIds = await _db.BookPuzzles.Where(bp => bp.BookId == book.Id)
                .Select(bp => bp.Id).ToListAsync(ct);
            await _bgQueue.EnqueueAsync(async (sp, token) =>
                await sp.GetRequiredService<HintGenerationService>().GenerateForPuzzlesAsync(puzzleIds, false, token));
        }

        return new BookImportItemDto
        {
            BookId = book.Id,
            FileName = fileName,
            Imported = toAdd.Count,
            Skipped = skipped,
            Updated = updated,
            Invalid = invalid,
        };
    }
}
