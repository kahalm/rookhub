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
        string LineId, string Round, string Fen, string Moves,
        string? Title, string? Chapter, string? Comment);

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
    /// Ungültige/unparsebare Einträge werden übersprungen, nicht geworfen.
    /// </summary>
    public static List<ParsedPuzzle> ParsePgn(string fileName, string pgnText)
    {
        var result = new List<ParsedPuzzle>();
        foreach (var (headers, moveText) in SplitGames(pgnText))
        {
            var fen = headers.GetValueOrDefault("FEN", "").Trim();
            var round = headers.GetValueOrDefault("Round", "").Trim();
            // Skip-Regeln wie import_books.py
            if (string.IsNullOrEmpty(fen) || fen == "?") continue;
            if (string.IsNullOrEmpty(round) || round == "?") continue;

            var comment = ExtractFirstComment(moveText);
            var uci = TryExtractUciMainline(fen, moveText);
            if (uci == null || uci.Count == 0) continue;

            var white = headers.GetValueOrDefault("White", "").Trim();
            var black = headers.GetValueOrDefault("Black", "").Trim();

            result.Add(new ParsedPuzzle(
                LineId: Truncate($"{fileName}:{round}", 300),
                Round: Truncate(round, 20),
                Fen: fen,
                Moves: string.Join(' ', uci),
                Title: string.IsNullOrEmpty(white) ? null : Truncate(white, 300),
                Chapter: string.IsNullOrEmpty(black) ? null : Truncate(black, 200),
                Comment: comment));
        }
        return result;
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
        var parsed = ParsePgn(fileName, pgnText);
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

        var existingLineIds = await _db.BookPuzzles.Select(bp => bp.LineId).ToHashSetAsync(ct);
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
        };
    }
}
