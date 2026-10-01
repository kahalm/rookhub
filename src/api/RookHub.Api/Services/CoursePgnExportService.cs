using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// PGN-Export eines Kurses: ganzes Buch (<c>GET /api/courses/{id}/pgn</c>, auch Grundlage von
/// „Kurs → Repertoire"), ein Kapitel (<c>/chapter-pgn</c>) und eine Linie (<c>/lines/{id}/pgn</c>).
/// Herausgelöst aus <see cref="CourseService"/> (Codereview 2026-09-29, A7-010): dort lag jede
/// Export-Variante neben der vorigen und bekam ihre Sperren einzeln — der Buch-Download hatte die
/// Kalkulationssperre deshalb vergessen (A7-001). Hier gibt es für ALLE Wege genau EINE Pforte,
/// <see cref="EnsureExportAllowedAsync"/>.
/// </summary>
public class CoursePgnExportService
{
    private readonly AppDbContext _db;

    public CoursePgnExportService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Die EINE Pforte aller PGN-Exporte: kein Kurszugriff (<see cref="CourseAccess.CanAccessAsync"/>)
    /// ODER Kalkulationsbuch → <see cref="NotFoundException"/> (404), auch für Admins. Ein PGN-Export
    /// enthält die Züge — in einem Kalkulationsbuch die Lösung — und beim Buch/Kapitel alle Wochen samt
    /// künftig terminierter Ausgaben (siehe <see cref="CourseAccess.IsCalculationBookAsync"/>). Wer die
    /// Zugfolgen braucht, schaltet als Besitzer/Admin den Kalkulations-Modus aus.
    /// </summary>
    private async Task EnsureExportAllowedAsync(int userId, int bookId, bool isAdmin)
    {
        if (!await CourseAccess.CanAccessAsync(_db, userId, bookId, isAdmin)
            || await CourseAccess.IsCalculationBookAsync(_db, bookId))
            throw new NotFoundException("Book not found.");
    }

    /// <summary>Exportiert ein (zugängliches) Buch als PGN. Liefert PGN-Text + Dateiname.
    /// <para>Bevorzugt das gespeicherte Roh-PGN (<see cref="BookSource.SourcePgn"/>) — es enthält die
    /// vollständige Originalstruktur inkl. <b>Varianten und Kommentaren</b>. Nur für Altbestand ohne
    /// Quelle (JSON-Import / vor der SourcePgn-Pipeline) wird ersatzweise aus den gespeicherten
    /// <see cref="BookPuzzle"/> rekonstruiert (Hauptlinie + Zug-Kommentare, aber ohne Varianten —
    /// die liegen nicht in der DB).</para>
    /// <para>KALKULATIONSBÜCHER → 404 (<see cref="EnsureExportAllowedAsync"/>); „Kurs → Repertoire"
    /// (<see cref="CourseRepertoireConversionService"/>) erbt die Sperre.</para></summary>
    public async Task<(string Pgn, string FileName)> GetBookPgnAsync(int userId, int bookId, bool isAdmin)
    {
        await EnsureExportAllowedAsync(userId, bookId, isAdmin);
        // Roh-PGN wird hier gebraucht → Source explizit mitladen (Tabellensplitting, siehe BookSource).
        var book = await _db.Books.Include(b => b.Source).FirstAsync(b => b.Id == bookId);
        var sourcePgn = book.Source.SourcePgn;
        var safe = new string((book.DisplayName ?? "course").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        var fileName = $"{(string.IsNullOrWhiteSpace(safe) ? "course" : safe)}.pgn";

        var puzzles = await _db.BookPuzzles
            .Where(bp => bp.BookId == bookId)
            .InReadingOrder()
            .ToListAsync();

        // Fallback (Altbestand ohne Quelle): aus den BookPuzzles rekonstruieren (Round-Lesereihenfolge).
        if (string.IsNullOrWhiteSpace(sourcePgn))
            return (CoursePgnExporter.ToPgn(book.DisplayName, puzzles), fileName);

        // Roh-PGN vorhanden → verbatim ausliefern (Varianten + Kommentare bleiben erhalten). Linien OHNE
        // Gegenstück darin (von Hand hinzugefügte Stellungen, CourseAuthoringService.AddLinesAsync) werden
        // rekonstruiert angehängt — sonst gingen sie beim Download und bei „Kurs → Repertoire" verloren,
        // samt ihrer Info-Kennung. Zuordnung über Round wie in BuildLinesPgn.
        var rawRounds = PgnParser.SplitGameBlocks(sourcePgn)
            .Select(g => PgnParser.Truncate(g.Headers.GetValueOrDefault("Round", "").Trim(), 20))
            .Where(r => r.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var missing = puzzles.Where(p => !rawRounds.Contains(p.Round)).ToList();
        if (missing.Count == 0)
            return (sourcePgn, fileName);
        var appended = CoursePgnExporter.ToPgn(book.DisplayName, missing).Trim();
        return (appended.Length == 0 ? sourcePgn : sourcePgn.TrimEnd() + "\n\n" + appended + "\n", fileName);
    }

    /// <summary>PGN EINES Kapitels (<paramref name="chapter"/> leer = „ohne Kapitel") in Lesereihenfolge.
    /// Jede Linie kommt wie beim Kurs-Download bevorzugt unverändert aus dem Roh-PGN (Varianten + Kommentare
    /// bleiben erhalten); nur Linien ohne Gegenstück dort werden rekonstruiert. Kein Zugriff, leeres
    /// Kapitel oder KALKULATIONSBUCH (die Züge wären die Lösung, wie bei <see cref="CourseService.GetAllPuzzlesAsync"/>)
    /// → <see cref="NotFoundException"/>.</summary>
    public async Task<(string Pgn, string FileName)> GetChapterPgnAsync(int userId, int bookId, string? chapter, bool isAdmin)
    {
        await EnsureExportAllowedAsync(userId, bookId, isAdmin);
        var book = await _db.Books.Include(b => b.Source).FirstAsync(b => b.Id == bookId);   // BuildLinesPgn liest das Roh-PGN
        var wanted = ChapterOrder.NormalizeChapter(chapter?.Trim());
        var puzzles = (await _db.BookPuzzles
                .Where(bp => bp.BookId == bookId)
                .InReadingOrder()
                .ToListAsync())
            .Where(bp => ChapterOrder.NormalizeChapter(bp.Chapter?.Trim()) == wanted)
            .ToList();
        if (puzzles.Count == 0) throw new NotFoundException("Chapter not found.");
        var fileName = PgnFileName(book.DisplayName, wanted ?? "no_chapter");
        return (BuildLinesPgn(book, puzzles), fileName);
    }

    /// <summary>PGN EINER Linie (<paramref name="lineId"/> = <see cref="BookPuzzle.Id"/>), bevorzugt
    /// unverändert aus dem Roh-PGN. Kein Zugriff, Linie gehört nicht zum Buch oder Kalkulationsbuch
    /// → <see cref="NotFoundException"/>.</summary>
    public async Task<(string Pgn, string FileName)> GetLinePgnAsync(int userId, int bookId, int lineId, bool isAdmin)
    {
        await EnsureExportAllowedAsync(userId, bookId, isAdmin);
        var book = await _db.Books.Include(b => b.Source).FirstAsync(b => b.Id == bookId);   // BuildLinesPgn liest das Roh-PGN
        var puzzle = await _db.BookPuzzles.FirstOrDefaultAsync(bp => bp.Id == lineId && bp.BookId == bookId)
            ?? throw new NotFoundException("Line not found.");
        var fileName = PgnFileName(book.DisplayName, $"{puzzle.Round} {puzzle.Title}");
        return (BuildLinesPgn(book, [puzzle]), fileName);
    }

    /// <summary>
    /// Setzt das PGN der übergebenen Linien zusammen. Gegenstück im Roh-PGN ist das Spiel mit derselben
    /// <c>Round</c> — genau daraus hat der Import die <see cref="BookPuzzle.LineId"/> gebildet (bei doppelter
    /// Round zählt wie dort das erste Spiel). Linien ohne Gegenstück (Altbestand ohne Quelle, von Hand
    /// eingefügte Stellungen, aus getReview ergänzte Lücken) werden aus der gespeicherten Linie rekonstruiert.
    /// <para>Erwartet das Buch MIT geladenem <see cref="Book.Source"/> (<c>.Include(b =&gt; b.Source)</c>).</para>
    /// </summary>
    internal static string BuildLinesPgn(Book book, IReadOnlyList<BookPuzzle> puzzles)
    {
        var rawByRound = new Dictionary<string, string>(StringComparer.Ordinal);
        var sourcePgn = book.Source.SourcePgn;
        if (!string.IsNullOrWhiteSpace(sourcePgn))
        {
            foreach (var (headers, raw) in PgnParser.SplitGameBlocks(sourcePgn))
            {
                var round = PgnParser.Truncate(headers.GetValueOrDefault("Round", "").Trim(), 20);
                if (round.Length > 0) rawByRound.TryAdd(round, raw);
            }
        }

        var bookName = book.DisplayName ?? "course";
        var games = new List<string>();
        foreach (var p in puzzles)
        {
            if (rawByRound.TryGetValue(p.Round, out var raw))
            {
                games.Add(raw);
                continue;
            }
            var rebuilt = CoursePgnExporter.ToPgn(bookName, [p]).Trim();
            if (rebuilt.Length > 0) games.Add(rebuilt);
        }
        return string.Join("\n\n", games) + "\n";
    }

    /// <summary>Dateiname „Kurs_Zusatz.pgn": nur Buchstaben/Ziffern, alles andere als ein „_".</summary>
    private static string PgnFileName(string? courseName, string suffix)
    {
        static string Clean(string? s)
        {
            var chars = (s ?? "").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
            var joined = System.Text.RegularExpressions.Regex.Replace(new string(chars), "_+", "_").Trim('_');
            return joined.Length > 80 ? joined[..80].TrimEnd('_') : joined;
        }
        var parts = new[] { Clean(courseName), Clean(suffix) }.Where(x => x.Length > 0);
        var name = string.Join("_", parts);
        return $"{(name.Length == 0 ? "course" : name)}.pgn";
    }
}
