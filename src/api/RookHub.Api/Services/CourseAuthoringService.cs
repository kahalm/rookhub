using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Kurs-Detailseite + Inhaltspflege: Metadaten/Fortschritt/Kapitel für die Detailansicht sowie das
/// Anlegen von Kapiteln und Linien direkt in der Oberfläche (Stellungen als Text einfügen),
/// Umbenennen/Löschen von Kapiteln, Löschen einzelner Linien und der Einzel-Kapitel-Reset des
/// eigenen Fortschritts.
///
/// <para><b>Rechte</b>: LESEN wie überall im Kurs-Kontext (<see cref="CourseAccess"/>, kein Zugriff →
/// 404). <b>ÄNDERN</b> von Inhalten darf nur der Besitzer eines persönlichen Buchs oder ein Admin
/// (sonst 403) — analog <c>CourseService.SetBookThemesAsync</c>. Den eigenen Fortschritt
/// (Kapitel-Reset) darf hingegen jeder zurücksetzen, der den Kurs sehen darf.</para>
///
/// <para>Manuell angelegte Linien sind <see cref="BookPuzzle.IsInfoOnly"/> = true: sie tragen keine
/// Lösung und dürfen darum nie abgefragt werden (auch nicht in Daily-/Random-Pools). Genau diese
/// Linien sind die Stellungen des Kalkulations-Modus.</para>
/// </summary>
public class CourseAuthoringService
{
    private readonly AppDbContext _db;

    public CourseAuthoringService(AppDbContext db) => _db = db;

    private const string NoChapterKey = "\0__none__";

    private static string? Normalize(string? chapter) => ChapterOrder.NormalizeChapter(chapter?.Trim());

    private static int Percent(int done, int total) =>
        total <= 0 ? 0 : (int)Math.Round(100.0 * Math.Min(done, total) / total);

    /// <summary>Buch laden + Lese-Zugriff erzwingen (kein Zugriff → 404).</summary>
    private async Task<Book> LoadReadableAsync(int userId, int bookId, bool isAdmin, CancellationToken ct)
    {
        if (!await CourseAccess.CanAccessAsync(_db, userId, bookId, isAdmin, ct))
            throw new KeyNotFoundException("Book not found.");
        return await _db.Books.FirstAsync(b => b.Id == bookId, ct);
    }

    /// <summary>Buch laden + Schreib-Recht erzwingen: Besitzer oder Admin (sonst 403).</summary>
    private async Task<Book> LoadManageableAsync(int userId, int bookId, bool isAdmin, CancellationToken ct)
    {
        var book = await LoadReadableAsync(userId, bookId, isAdmin, ct);
        if (!isAdmin && book.OwnerUserId != userId)
            throw new UnauthorizedAccessException("Only the owner or an admin may edit this course.");
        return book;
    }

    // ===== Detailseite =======================================================

    public async Task<CourseDetailDto> GetDetailAsync(int userId, int bookId, bool isAdmin,
        CancellationToken ct = default)
    {
        var book = await LoadReadableAsync(userId, bookId, isAdmin, ct);

        var lines = await _db.BookPuzzles
            .Where(bp => bp.BookId == bookId)
            .OrderBy(bp => bp.Round).ThenBy(bp => bp.Id)
            .Select(bp => new { bp.Id, bp.Chapter, bp.IsInfoOnly })
            .ToListAsync(ct);

        var solvedIds = (await _db.CoursePuzzleResults
            .Where(cr => cr.UserId == userId && cr.BookId == bookId)
            .Select(cr => cr.BookPuzzleId)
            .ToListAsync(ct)).ToHashSet();
        var treeIds = (await _db.CalculationTrees
            .Where(t => t.UserId == userId && t.BookId == bookId)
            .Select(t => t.BookPuzzleId)
            .ToListAsync(ct)).ToHashSet();

        // Beim Kalkulationsbuch gibt es kein „gelöst": Fortschritt = Stellungen mit eigenem Baum,
        // Gesamtzahl = ALLE Linien (identisch zur Zählung in der Kursübersicht).
        var isCalc = book.IsCalculation;
        var doneIds = isCalc ? treeIds : solvedIds;

        // Kapitel in Lesereihenfolge — hier ALLE (auch reine Stellungs-/Info-Kapitel).
        var order = new List<string?>();
        var seen = new HashSet<string>();
        var byKey = new Dictionary<string, (int Lines, int Quiz, int Done, int FirstId)>();
        foreach (var line in lines)
        {
            var name = Normalize(line.Chapter);
            var key = name ?? NoChapterKey;
            if (seen.Add(key)) { order.Add(name); byKey[key] = (0, 0, 0, line.Id); }
            var cur = byKey[key];
            var counts = isCalc || !line.IsInfoOnly;      // was im Kapitel als „zählbar" gilt
            byKey[key] = (
                cur.Lines + 1,
                cur.Quiz + (line.IsInfoOnly ? 0 : 1),
                cur.Done + (counts && doneIds.Contains(line.Id) ? 1 : 0),
                cur.FirstId);
        }

        // Solver-Kapitelindex (nur Quiz-Linien) — für „Kapitel starten" im Solver.
        var solverNames = await ChapterOrder.GetOrderedChapterNamesAsync(_db, bookId);
        var solverIndexByKey = solverNames
            .Select((name, index) => (Key: name ?? NoChapterKey, Index: index))
            .ToDictionary(x => x.Key, x => x.Index);

        var chapters = order.Select(name =>
        {
            var key = name ?? NoChapterKey;
            var c = byKey[key];
            var total = isCalc ? c.Lines : c.Quiz;
            return new CourseManageChapterDto
            {
                Name = name,
                LineCount = c.Lines,
                QuizCount = c.Quiz,
                SolvedCount = Math.Min(c.Done, total),
                ProgressPercent = Percent(c.Done, total),
                SolverIndex = solverIndexByKey.TryGetValue(key, out var si) ? si : null,
                FirstLineId = c.FirstId,
            };
        }).ToList();

        var progress = await _db.CourseProgresses
            .Where(cp => cp.UserId == userId && cp.BookId == bookId)
            .Select(cp => new { cp.LastMode, cp.UpdatedAt })
            .FirstOrDefaultAsync(ct);
        var sharedBy = await _db.CourseShares
            .Where(cs => cs.BookId == bookId && cs.RecipientId == userId)
            .Select(cs => cs.Owner!.Username)
            .FirstOrDefaultAsync(ct);
        var link = await _db.CourseLinks
            .Where(l => l.UserId == userId && l.BookId == bookId)
            .Select(l => l.LinkedBookId)
            .FirstOrDefaultAsync(ct);
        var linkedName = link == 0 ? null : await _db.Books
            .Where(b => b.Id == link).Select(b => b.DisplayName).FirstOrDefaultAsync(ct);

        var quizCount = lines.Count(l => !l.IsInfoOnly);
        var total = isCalc ? lines.Count : quizCount;
        var done = lines.Count(l => (isCalc || !l.IsInfoOnly) && doneIds.Contains(l.Id));

        return new CourseDetailDto
        {
            BookId = book.Id,
            FileName = book.FileName,
            DisplayName = book.DisplayName,
            Description = book.Description,
            Difficulty = book.Difficulty,
            Rating = book.Rating,
            MinElo = book.MinElo,
            MaxElo = book.MaxElo,
            Tags = book.Tags,
            Themes = BookThemeTags.ParseKeys(book.Themes),
            Kind = book.Kind,
            IsCalculation = isCalc,
            IsPublic = book.IsPublic,
            PublicSlug = book.PublicSlug,
            IsOwned = book.OwnerUserId == userId,
            IsShared = sharedBy != null,
            SharedByUsername = sharedBy,
            IsPinned = await _db.CoursePins.AnyAsync(p => p.UserId == userId && p.BookId == bookId, ct),
            CanManage = isAdmin || book.OwnerUserId == userId,
            PuzzleCount = total,
            SolvedCount = Math.Min(done, total),
            ProgressPercent = Percent(done, total),
            TotalLines = lines.Count,
            InfoLineCount = lines.Count - quizCount,
            LastMode = progress?.LastMode,
            LastActivityAt = progress?.UpdatedAt,
            LinkedBookId = link == 0 ? null : link,
            LinkedDisplayName = linkedName,
            Chapters = chapters,
            CreatedAt = book.CreatedAt,
            UpdatedAt = book.UpdatedAt,
        };
    }

    /// <summary>Linien EINES Kapitels (Verwaltungssicht). Liefert bewusst keine Zugfolge, nur deren Länge.</summary>
    public async Task<List<CourseLineDto>> GetChapterLinesAsync(int userId, int bookId, string? chapter,
        bool isAdmin, CancellationToken ct = default)
    {
        await LoadReadableAsync(userId, bookId, isAdmin, ct);
        var wanted = Normalize(chapter);
        var lines = await _db.BookPuzzles
            .Where(bp => bp.BookId == bookId)
            .OrderBy(bp => bp.Round).ThenBy(bp => bp.Id)
            .Select(bp => new
            {
                bp.Id, bp.LineId, bp.Round, bp.Title, bp.Chapter, bp.Fen, bp.Comment, bp.IsInfoOnly, bp.Moves,
            })
            .ToListAsync(ct);
        return lines
            .Where(l => Normalize(l.Chapter) == wanted)
            .Select(l => new CourseLineDto
            {
                Id = l.Id,
                LineId = l.LineId,
                Round = l.Round,
                Title = l.Title,
                Chapter = Normalize(l.Chapter),
                Fen = l.Fen,
                Comment = l.Comment,
                IsInfoOnly = l.IsInfoOnly,
                MoveCount = string.IsNullOrWhiteSpace(l.Moves)
                    ? 0
                    : l.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            })
            .ToList();
    }

    // ===== Inhalte pflegen ===================================================

    /// <summary>
    /// Fügt Stellungen aus dem Memo-Text als neue Linien ins (ggf. neue) Kapitel ein. Jede Zeile wird
    /// zu einer Stellungs-Linie ohne Lösung (<see cref="BookPuzzle.IsInfoOnly"/>); Stellungen, die im
    /// Buch schon vorkommen, werden mit Grund <c>duplicate</c> übersprungen.
    /// </summary>
    public async Task<AddCourseLinesResultDto> AddLinesAsync(int userId, int bookId, AddCourseLinesDto dto,
        bool isAdmin, CancellationToken ct = default)
    {
        var book = await LoadManageableAsync(userId, bookId, isAdmin, ct);
        var parsed = FenListParser.Parse(dto.Text);
        var issues = parsed.Errors
            .Select(e => new CourseLineIssueDto { LineNumber = e.LineNumber, Text = e.Text, Reason = e.Reason })
            .ToList();
        if (parsed.Positions.Count == 0 && issues.Count == 0)
            throw new ArgumentException("No positions found.");

        var chapter = Normalize(dto.Chapter);
        if (chapter is { Length: > 200 }) chapter = chapter[..200];

        var existing = await _db.BookPuzzles
            .Where(bp => bp.BookId == bookId)
            .Select(bp => new { bp.Id, bp.LineId, bp.Round, bp.Fen })
            .ToListAsync(ct);
        var existingFens = existing.Select(e => e.Fen).ToHashSet(StringComparer.Ordinal);
        var existingLineIds = existing.Select(e => e.LineId).ToHashSet(StringComparer.Ordinal);

        var (nextRound, width) = NextRound(existing.Select(e => e.Round));
        var added = 0;
        foreach (var pos in parsed.Positions)
        {
            if (!existingFens.Add(pos.Fen))
            {
                issues.Add(new CourseLineIssueDto
                {
                    LineNumber = pos.LineNumber, Text = pos.Fen, Reason = "duplicate",
                });
                continue;
            }

            // LineId ist global eindeutig → bei (unwahrscheinlicher) Kollision die Rundennummer vorrücken.
            string round, lineId;
            do
            {
                round = nextRound.ToString(new string('0', width));
                lineId = $"{book.FileName}:{round}";
                nextRound++;
            } while (!existingLineIds.Add(lineId));

            _db.BookPuzzles.Add(new BookPuzzle
            {
                LineId = PgnParser.Truncate(lineId, 300),
                BookFileName = book.FileName,
                BookId = book.Id,
                Round = PgnParser.Truncate(round, 20),
                Fen = pos.Fen,
                Moves = string.Empty,
                StartPly = -1,
                Chapter = chapter,
                Comment = pos.Comment,
                IsInfoOnly = true,
            });
            added++;
        }

        if (added > 0)
        {
            book.UpdatedAt = DateTime.UtcNow;
            // Handgepflegte Bücher haben kein Quell-PGN — sie sollen nicht als „veraltet"
            // im Aufbereitungs-Banner auftauchen (dort wären sie ohnehin nicht reprozessierbar).
            book.ImportVersion = ImportPipeline.CurrentVersion;
            await _db.SaveChangesAsync(ct);
        }

        return new AddCourseLinesResultDto
        {
            Added = added,
            Chapter = chapter,
            Issues = issues,
            TotalLines = existing.Count + added,
        };
    }

    /// <summary>
    /// Nächste Rundennummer + Ziffernbreite der bestehenden Runden. Die Lesereihenfolge sortiert
    /// <see cref="BookPuzzle.Round"/> als TEXT — deshalb übernimmt eine neue Runde die Breite der
    /// bestehenden (aus „0007" wird „0008", aus „7" wird „8"), sonst käme „0010" vor „9".
    /// </summary>
    internal static (int Next, int Width) NextRound(IEnumerable<string> rounds)
    {
        var max = 0;
        var width = 1;
        foreach (var raw in rounds)
        {
            var text = (raw ?? string.Empty).Trim();
            if (text.Length == 0 || !int.TryParse(text, out var value)) continue;
            if (value > max) max = value;
            if (text.Length > width) width = text.Length;
        }
        return (max + 1, width);
    }

    /// <summary>Kapitel umbenennen (leerer neuer Name = „ohne Kapitel"). Ziel darf nicht schon existieren.</summary>
    public async Task<int> RenameChapterAsync(int userId, int bookId, RenameCourseChapterDto dto, bool isAdmin,
        CancellationToken ct = default)
    {
        var book = await LoadManageableAsync(userId, bookId, isAdmin, ct);
        var from = Normalize(dto.Chapter);
        var to = Normalize(dto.NewName);
        if (to is { Length: > 200 }) to = to[..200];
        if (from == to) return 0;

        var all = await _db.BookPuzzles.Where(bp => bp.BookId == bookId).ToListAsync(ct);
        if (to != null && all.Any(bp => Normalize(bp.Chapter) == to))
            throw new ArgumentException("A chapter with that name already exists.");

        var affected = all.Where(bp => Normalize(bp.Chapter) == from).ToList();
        if (affected.Count == 0) throw new KeyNotFoundException("Chapter not found.");
        foreach (var bp in affected) bp.Chapter = to;
        book.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return affected.Count;
    }

    /// <summary>Löscht ein ganzes Kapitel = alle seine Linien (inkl. abhängiger Nutzerdaten).</summary>
    public async Task<int> DeleteChapterAsync(int userId, int bookId, string? chapter, bool isAdmin,
        CancellationToken ct = default)
    {
        await LoadManageableAsync(userId, bookId, isAdmin, ct);
        var wanted = Normalize(chapter);
        var all = await _db.BookPuzzles.Where(bp => bp.BookId == bookId).ToListAsync(ct);
        var doomed = all.Where(bp => Normalize(bp.Chapter) == wanted).ToList();
        if (doomed.Count == 0) throw new KeyNotFoundException("Chapter not found.");
        await RemoveLinesAsync(bookId, doomed, ct);
        return doomed.Count;
    }

    /// <summary>Löscht eine einzelne Linie des Buchs (inkl. abhängiger Nutzerdaten).</summary>
    public async Task DeleteLineAsync(int userId, int bookId, int bookPuzzleId, bool isAdmin,
        CancellationToken ct = default)
    {
        await LoadManageableAsync(userId, bookId, isAdmin, ct);
        var line = await _db.BookPuzzles.FirstOrDefaultAsync(bp => bp.Id == bookPuzzleId && bp.BookId == bookId, ct)
            ?? throw new KeyNotFoundException("Line not found.");
        await RemoveLinesAsync(bookId, new List<BookPuzzle> { line }, ct);
    }

    /// <summary>
    /// Entfernt Linien samt aller Datensätze, die per Restrict-FK daran hängen (sonst blockt der
    /// DB-Constraint): Kurs-Ergebnisse/Versuche/Info-Ansichten, Buch-Puzzle-Versuche, Tagespuzzle-
    /// Zuordnungen und die Analysebäume des Kalkulations-Modus.
    /// </summary>
    private async Task RemoveLinesAsync(int bookId, List<BookPuzzle> lines, CancellationToken ct)
    {
        var ids = lines.Select(l => l.Id).ToList();
        _db.CoursePuzzleResults.RemoveRange(
            await _db.CoursePuzzleResults.Where(cr => ids.Contains(cr.BookPuzzleId)).ToListAsync(ct));
        _db.CourseAttempts.RemoveRange(
            await _db.CourseAttempts.Where(a => ids.Contains(a.BookPuzzleId)).ToListAsync(ct));
        _db.CourseInfoViews.RemoveRange(
            await _db.CourseInfoViews.Where(iv => ids.Contains(iv.BookPuzzleId)).ToListAsync(ct));
        _db.BookPuzzleAttempts.RemoveRange(
            await _db.BookPuzzleAttempts.Where(a => ids.Contains(a.BookPuzzleId)).ToListAsync(ct));
        _db.DailyPuzzles.RemoveRange(
            await _db.DailyPuzzles.Where(d => ids.Contains(d.BookPuzzleId)).ToListAsync(ct));
        _db.CalculationTrees.RemoveRange(
            await _db.CalculationTrees.Where(t => ids.Contains(t.BookPuzzleId)).ToListAsync(ct));
        _db.BookPuzzles.RemoveRange(lines);

        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId, ct);
        if (book != null) book.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ===== Eigener Fortschritt ===============================================

    /// <summary>
    /// Setzt den EIGENEN Fortschritt eines Kapitels zurück: gelöste Linien, Zeit-/Versuchs-Log und
    /// gesehene Info-Linien dieses Kapitels. <c>Book</c>-weites <c>CourseProgress.ResetAt</c> bleibt
    /// unangetastet (das ist buchweit), ebenso die eigenen Analysebäume des Kalkulations-Modus —
    /// die sind Arbeit des Nutzers und werden nur einzeln im Modus selbst verworfen.
    /// <para>Braucht kein Schreibrecht am Buch: jeder, der den Kurs sehen darf, darf seinen eigenen
    /// Fortschritt zurücksetzen.</para>
    /// </summary>
    public async Task<int> ResetChapterProgressAsync(int userId, int bookId, string? chapter, bool isAdmin,
        CancellationToken ct = default)
    {
        await LoadReadableAsync(userId, bookId, isAdmin, ct);
        var wanted = Normalize(chapter);
        var all = await _db.BookPuzzles
            .Where(bp => bp.BookId == bookId)
            .Select(bp => new { bp.Id, bp.Chapter })
            .ToListAsync(ct);
        var ids = all.Where(bp => Normalize(bp.Chapter) == wanted).Select(bp => bp.Id).ToList();
        if (ids.Count == 0) throw new KeyNotFoundException("Chapter not found.");

        var results = await _db.CoursePuzzleResults
            .Where(cr => cr.UserId == userId && ids.Contains(cr.BookPuzzleId)).ToListAsync(ct);
        var attempts = await _db.CourseAttempts
            .Where(a => a.UserId == userId && ids.Contains(a.BookPuzzleId)).ToListAsync(ct);
        var views = await _db.CourseInfoViews
            .Where(iv => iv.UserId == userId && ids.Contains(iv.BookPuzzleId)).ToListAsync(ct);

        _db.CoursePuzzleResults.RemoveRange(results);
        _db.CourseAttempts.RemoveRange(attempts);
        _db.CourseInfoViews.RemoveRange(views);
        await _db.SaveChangesAsync(ct);
        return results.Count;
    }
}
