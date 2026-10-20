using System.Text;
using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// „Als Kurs anlegen“ (0.785.0): macht aus den erkannten Diagrammen eines Buchs einen PERSÖNLICHEN Kurs des Aufrufers.
/// Jedes Diagramm mit Lösung (aus dem Buch oder von Stockfish, <see cref="EbookDiagram.SolutionSan"/>) wird eine
/// Aufgabe; Diagramme ohne Lösung kommen als Info-Linie mit (nur Stellung + Bildunterschrift), damit der Kurs dem Buch
/// folgt. Der Weg läuft über den normalen PGN-Import (<see cref="CourseService.UploadPersonalCourseAsync"/>) — derselbe
/// Kurs, als hätte man das PGN hochgeladen.
/// </summary>
public class EbookCourseBuilder
{
    private readonly AppDbContext _db;
    private readonly CourseService _courses;
    private readonly EbookStorage? _storage;

    public EbookCourseBuilder(AppDbContext db, CourseService courses, EbookStorage? storage = null)
    {
        _db = db;
        _courses = courses;
        _storage = storage;
    }

    public async Task<CourseListItemDto> CreateCourseAsync(EbookCaller caller, bool manageAll, int ebookId, CancellationToken ct = default)
    {
        if (caller.IsAnonymous || caller.UserId == null) throw new ForbiddenException("Sign in to create a course.");
        var book = await EbookAccess.Visible(_db, caller, manageAll).Where(b => b.Id == ebookId)
            .Select(b => new { b.Title, b.Author, b.DiagramState }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("E-book not found.");
        if (book.DiagramState != EbookDiagramState.Done) throw new DomainValidationException("This book has no recognised diagrams yet.");
        var diagrams = await _db.EbookDiagrams.Where(d => d.EbookId == ebookId).Readable()
            .OrderBy(d => d.Page).ThenBy(d => d.Y).ThenBy(d => d.X).ToListAsync(ct);
        var pgn = BuildPgn(book.Title, diagrams, await ChaptersAsync(ebookId, diagrams, ct));
        if (pgn.Puzzles == 0) throw new DomainValidationException("No diagram has a solution yet — wait until Stockfish has checked the book.");
        return await _courses.UploadPersonalCourseAsync(caller.UserId.Value, book.Title + ".pgn", pgn.Text, book.Title, ct);
    }

    public sealed record Result(string Text, int Puzzles, int InfoLines);

    /// <summary>Kapitel je Diagramm aus dem Inhaltsverzeichnis des Buchs (0.805.0): PDF-Diagramme (mit Lage, W &gt; 0) über
    /// die Lesezeichen des Original-PDFs, EPUB-Diagramme über das Inhaltsverzeichnis des Original-EPUBs. Fehlt beides,
    /// bleibt der Kurs ungegliedert.</summary>
    private async Task<IReadOnlyDictionary<int, string>> ChaptersAsync(int ebookId, IReadOnlyList<EbookDiagram> diagrams, CancellationToken ct)
    {
        var result = new Dictionary<int, string>();
        if (_storage?.Root == null || diagrams.Count == 0) return result;
        var ebook = await _db.Ebooks.Include(b => b.Files).AsNoTracking().FirstOrDefaultAsync(b => b.Id == ebookId, ct);
        if (ebook == null) return result;
        IReadOnlyList<EbookChapterIndex.Entry> Load(string format, bool derived, Func<string, IReadOnlyList<EbookChapterIndex.Entry>> read)
        {
            var file = ebook.Files.FirstOrDefault(f => f.Format == format && f.Derived == derived);
            var path = file == null ? null : _storage.ResolveFile(ebook, file);
            return path != null && File.Exists(path) ? read(path) : [];
        }
        var pdf = diagrams.Any(d => d.W > 0) ? Load("pdf", false, EbookChapterIndex.FromPdf) : [];
        var epub = diagrams.Any(d => d.W <= 0 || (pdf.Count == 0 && d.EpubChapter != null)) ? Load("epub", false, EbookChapterIndex.FromEpub) : [];
        // PDF ohne Lesezeichen: die Diagramme kennen ihr Kapitel im EPUB (EpubChapter) — im echten EPUB des Buchs
        // (0.793.0) oder im Fließtext-EPUB (0.802.0), dessen Kapitel aus den Überschriften stammen.
        var reflow = pdf.Count == 0 && diagrams.Any(d => d.W > 0 && d.EpubChapter != null)
            ? (epub.Count > 0 ? epub : Load("epub", true, EbookChapterIndex.FromEpub)) : [];
        foreach (var d in diagrams)
        {
            var chapter = d.W <= 0 ? EbookChapterIndex.Pick(epub, d.Page)
                : pdf.Count > 0 ? EbookChapterIndex.Pick(pdf, d.Page)
                : d.EpubChapter is int ec ? EbookChapterIndex.Pick(reflow, ec) : null;
            if (chapter != null) result[d.Id] = chapter;
        }
        return result;
    }

    /// <summary>PGN im Kurs-Format: <c>[White]</c> = Titel der Linie, <c>[Black]</c> = Buchkapitel (falls bekannt), <c>[Round]</c> = laufende Nummer (Reihenfolge),
    /// <c>[FEN]</c> = Diagramm; Züge = Lösung. Unspielbare Stellungen (ohne König u. Ä.) fallen weg.</summary>
    public static Result BuildPgn(string bookTitle, IReadOnlyList<EbookDiagram> diagrams, IReadOnlyDictionary<int, string>? chapters = null)
    {
        var sb = new StringBuilder();
        int puzzles = 0, info = 0, n = 0;
        foreach (var d in diagrams)
        {
            if (!EbookSolutionChecker.IsLegal(d.Fen)) continue;
            var moves = MoveText(d.Fen, d.SolutionSan);
            var title = string.IsNullOrWhiteSpace(d.Label) ? $"S. {d.Page}" : $"Diagramm {d.Label}";
            var comment = string.Join(" — ", new[] { d.Caption, d.SolutionText }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (moves == null && comment.Length == 0) comment = title;
            n++;
            sb.Append("[Event \"").Append(Escape(bookTitle)).Append("\"]\n");
            sb.Append("[White \"").Append(Escape(title)).Append("\"]\n");
            // [Black] = Kapitel im Kurs-Format (PgnImportService) — gliedert den Kurs wie das Buch.
            sb.Append("[Black \"").Append(chapters != null && chapters.TryGetValue(d.Id, out var ch) ? Escape(ch) : "").Append("\"]\n");
            sb.Append("[Round \"").Append(n.ToString("D4")).Append("\"]\n");
            sb.Append("[SetUp \"1\"]\n[FEN \"").Append(d.Fen).Append("\"]\n\n");
            if (comment.Length > 0) sb.Append('{').Append(comment.Replace("{", "(").Replace("}", ")")).Append("} ");
            if (moves != null) { sb.Append(moves); puzzles++; }
            else { sb.Append("{[%info]}"); info++; }
            sb.Append(" *\n\n");
        }
        return new Result(sb.ToString(), puzzles, info);
    }

    /// <summary>Lösung als PGN-Zugtext mit Zugnummern (ab der Seite am Zug); <c>null</c>, wenn sie sich nicht nachspielen lässt.</summary>
    internal static string? MoveText(string fen, string? solutionSan)
    {
        if (string.IsNullOrWhiteSpace(solutionSan)) return null;
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            var parts = fen.Split(' ');
            var white = parts.Length < 2 || parts[1] == "w";
            var no = parts.Length >= 6 && int.TryParse(parts[5], out var f) && f > 0 ? f : 1;
            var sb = new StringBuilder();
            var first = true;
            foreach (var san in solutionSan.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!board.Move(san)) return null;
                if (white) sb.Append(no).Append(". ");
                else if (first) sb.Append(no).Append("... ");
                sb.Append(san).Append(' ');
                if (!white) no++;
                white = !white;
                first = false;
            }
            return sb.ToString().TrimEnd();
        }
        catch { return null; }
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
