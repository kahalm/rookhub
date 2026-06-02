using System.Text;
using System.Text.RegularExpressions;
using Chess;
using Microsoft.EntityFrameworkCore;
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
    public PgnImportService(AppDbContext db) => _db = db;

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
        string? Title, string? Chapter, string? Comment);

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
    public static ParseResult ParsePgn(string fileName, string pgnText)
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
            var uci = TryExtractUciMainline(fen, moveText);
            if (uci == null || uci.Count == 0) { invalid++; continue; }

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
                if (IsStartPosition(fen)) { invalid++; continue; }
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
                Comment: comment));
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

    // ---- erster (nicht-leerer) Mainline-Kommentar -------------------------
    private static string? ExtractFirstComment(string moveText)
    {
        foreach (Match m in CommentRegex().Matches(moveText))
        {
            var inner = m.Value.Trim('{', '}');
            var cleaned = WhitespaceRegex().Replace(AnnotationRegex().Replace(inner, ""), " ").Trim();
            // import_books.py bricht beim ersten Kommentar ab, auch wenn er leer wird
            return string.IsNullOrEmpty(cleaned) ? null : Truncate(cleaned, 5000);
        }
        return null;
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
    /// <summary>Parst eine Datei und legt Book + BookPuzzles an (Dedup per LineId).</summary>
    public async Task<BookImportItemDto> ImportFileAsync(string fileName, string pgnText, CancellationToken ct)
    {
        var (parsed, invalid) = ParsePgn(fileName, pgnText);
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

        // Dedup nur gegen Linien DIESES Buchs/dieser Datei laden, nicht ALLE BookPuzzles
        // (LineIds sind dateiprefix-eindeutig -> Cross-Book-Kollisionen gibt es nicht).
        var existingLineIds = await _db.BookPuzzles
            .Where(bp => bp.BookId == book.Id || bp.BookFileName == fileName)
            .Select(bp => bp.LineId).ToHashSetAsync(ct);
        var toAdd = new List<BookPuzzle>();
        var skipped = 0;
        var seen = new HashSet<string>();

        foreach (var p in parsed)
        {
            if (existingLineIds.Contains(p.LineId) || !seen.Add(p.LineId)) { skipped++; continue; }
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
                Chapter = p.Chapter,
                Comment = p.Comment,
            });
        }

        if (toAdd.Count > 0)
        {
            _db.BookPuzzles.AddRange(toAdd);
            book.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
        }

        return new BookImportItemDto
        {
            BookId = book.Id,
            FileName = fileName,
            Imported = toAdd.Count,
            Skipped = skipped,
            Invalid = invalid,
        };
    }
}
