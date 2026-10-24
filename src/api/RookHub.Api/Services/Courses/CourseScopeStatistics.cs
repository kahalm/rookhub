using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;

namespace RookHub.Api.Services.Courses;

/// <summary>Buch- und (optional) Kapitel-Statistik eines Kurses, wie sie <c>/next</c>, <c>/results</c> und
/// <c>/reset</c> mitliefern.</summary>
internal sealed record CourseStatsBundle(CourseScopeStatsDto Book, CourseScopeStatsDto? Chapter, string? ChapterName);

/// <summary>Die Versuche EINER Linie seit dem letzten Reset, verdichtet: Zeit gesamt und der erste Versuch.</summary>
internal sealed class PuzzleAttemptSummary
{
    public int PuzzleId { get; init; }
    public int Seconds { get; init; }
    public bool FirstSolved { get; init; }
    public int FirstHints { get; init; }
}

/// <summary>
/// Kurs-Statistik für den Solver: Fortschritt (gelöst/gesamt), akkumulierte Zeit und
/// Erst-Versuch-Trefferquote je Buch und Kapitel. Aus <see cref="CourseService"/> herausgelöst
/// (Codereview 2026-09-29, Z4): das Laden (<see cref="ComputeAsync"/>) und das reine Zählen
/// (<see cref="BuildScope"/>, ohne Datenbank testbar) sind getrennt.
/// </summary>
internal static class CourseScopeStatistics
{
    /// <summary>
    /// Berechnet Buch- und (optional) Kapitel-Statistik für den User: Fortschritt (gelöst/gesamt),
    /// akkumulierte Zeit und Erst-Versuch-Trefferquote. Zeit + Trefferquote zählen nur Versuche seit
    /// dem letzten Reset (<see cref="Models.CourseProgress.ResetAt"/>); die gelöste Menge wird beim Reset
    /// physisch entfernt und braucht daher keinen Zeitfilter. Aggregation im Speicher (Versuchszahl je
    /// User/Buch ist klein) — robust gegen Provider-Unterschiede (InMemory vs. MariaDB).
    /// Ein Kapitel-Block wird nur geliefert, wenn das Buch tatsächlich mehrere Kapitel hat.
    /// </summary>
    internal static async Task<CourseStatsBundle> ComputeAsync(AppDbContext db, int userId, int bookId,
        string? currentChapter, bool currentChapterKnown)
    {
        var resetAt = await db.CourseProgresses
            .Where(cp => cp.UserId == userId && cp.BookId == bookId)
            .Select(cp => cp.ResetAt)
            .FirstOrDefaultAsync();

        // Info-/Erklärlinien zählen nicht zum Fortschritt (kein Quiz).
        var puzzleQuery = db.BookPuzzles.Where(bp => bp.BookId == bookId && !bp.IsInfoOnly);

        // Frueher wurde hier die KOMPLETTE Puzzle-Liste des Buchs (Id + Kapitel) in den Speicher
        // geladen - bei JEDEM /next und /results, obwohl buchweit nur zwei Zahlen gebraucht werden.
        // Beide sind SQL-Aggregate. Die Normalisierung aus NormalizeChapter (leer/nur Leerzeichen
        // -> null) MUSS im Ausdruck mitwandern, sonst zaehlten "", "  " und NULL in der Datenbank
        // als drei Kapitel statt wie bisher als eines.
        var bookTotal = await puzzleQuery.CountAsync();
        var distinctChapters = await puzzleQuery
            .Select(bp => bp.Chapter == null || bp.Chapter.Trim() == "" ? null : bp.Chapter)
            .Distinct()
            .CountAsync();

        // Kapitel-Block nur, wenn das Buch wirklich mehrere Kapitel hat (sonst dupliziert er das Buch).
        var wantChapter = currentChapterKnown && distinctChapters > 1;
        // Puzzle-Ids braucht nur der Kapitel-Block - und dann auch nur die des EINEN Kapitels.
        var chapterPuzzleIds = wantChapter
            ? (await puzzleQuery
                .Where(bp => (bp.Chapter == null || bp.Chapter.Trim() == "" ? null : bp.Chapter) == currentChapter)
                .Select(bp => bp.Id)
                .ToListAsync()).ToHashSet()
            : new HashSet<int>();

        var solvedIds = (await CourseProgressRules.SolvedQuizResults(db, userId)
            .Where(cr => cr.BookId == bookId)
            .Select(cr => cr.BookPuzzleId)
            .ToListAsync()).ToHashSet();

        var perPuzzle = await LoadAttemptSummariesAsync(db, userId, bookId, resetAt ?? DateTime.MinValue);

        var book = BuildScope(solvedIds, perPuzzle, _ => true, bookTotal);
        CourseScopeStatsDto? chapter = null;
        string? chapterName = null;
        if (wantChapter)
        {
            chapterName = currentChapter;
            // Gleichbedeutend mit dem frueheren chapterOf-Lookup: das Woerterbuch enthielt
            // ausschliesslich die Nicht-Info-Puzzles des Buchs, der Treffer auf das aktuelle
            // Kapitel ist genau die Mitgliedschaft in dieser Menge.
            chapter = BuildScope(solvedIds, perPuzzle, chapterPuzzleIds.Contains, chapterPuzzleIds.Count);
        }
        return new CourseStatsBundle(book, chapter, chapterName);
    }

    /// <summary>
    /// EINE Zeile je PUZZLE statt je VERSUCH. Vorher wurde jeder einzelne Versuch des Users in
    /// diesem Buch geladen — bei jedem /next und /results, und wachsend mit der Historie. Der
    /// „erste Versuch" kommt jetzt aus einer korrelierten Unterabfrage
    /// (ORDER BY AttemptedAt, Id LIMIT 1), also aus GENAU derselben Reihenfolge wie zuvor im
    /// Speicher. Der Reset-Filter wandert dabei nachweislich in die Unterabfrage mit — ohne das
    /// stammte der „erste Versuch" von VOR dem Zurücksetzen und die Trefferquote wäre falsch.
    /// </summary>
    private static Task<List<PuzzleAttemptSummary>> LoadAttemptSummariesAsync(AppDbContext db, int userId, int bookId,
        DateTime resetAt)
        => CourseProgressRules.AttemptsSinceReset(db, userId, bookId, resetAt)
            .GroupBy(a => a.BookPuzzleId)
            .Select(g => new PuzzleAttemptSummary
            {
                PuzzleId = g.Key,
                Seconds = g.Sum(a => a.TimeSeconds),
                FirstSolved = g.OrderBy(a => a.AttemptedAt).ThenBy(a => a.Id).First().Solved,
                FirstHints = g.OrderBy(a => a.AttemptedAt).ThenBy(a => a.Id).First().HintsUsed,
            })
            .ToListAsync();

    /// <summary>
    /// Zählt einen Ausschnitt (Buch oder Kapitel, gegeben als <paramref name="inScope"/> + Gesamtzahl
    /// <paramref name="total"/>) aus der gelösten Menge und den verdichteten Versuchen — rein, ohne Datenbank.
    /// <para>Aufschlüsselung (Wunsch 2026-10-15): gelöst ohne Hilfe = erster Versuch seit Reset richtig und
    /// ohne Tipp (gelöst ohne Versuch seit Reset — Altbestand — zählt ebenfalls dazu); sonst „mit
    /// Tipp/Neustart". Aufgegeben = versucht, aber (noch) nicht gelöst.</para>
    /// </summary>
    internal static CourseScopeStatsDto BuildScope(IReadOnlySet<int> solvedIds,
        IReadOnlyCollection<PuzzleAttemptSummary> perPuzzle, Func<int, bool> inScope, int total)
    {
        var solved = solvedIds.Count(inScope);
        var scoped = perPuzzle.Where(p => inScope(p.PuzzleId)).ToList();
        var firstCorrect = scoped.Count(p => p.FirstSolved);
        var byId = scoped.ToDictionary(p => p.PuzzleId);
        var solvedInScope = solvedIds.Where(inScope).ToList();
        var clean = solvedInScope.Count(id => !byId.TryGetValue(id, out var p) || (p.FirstSolved && p.FirstHints == 0));
        var gaveUp = scoped.Count(p => !solvedIds.Contains(p.PuzzleId));
        return new CourseScopeStatsDto
        {
            CleanCount = Math.Min(clean, total),
            HelpedCount = Math.Min(solvedInScope.Count - clean, total),
            GaveUpCount = gaveUp,
            SolvedCount = Math.Min(solved, total),
            Total = total,
            ProgressPercent = CourseProgressRules.Percent(solved, total),
            TotalSeconds = scoped.Sum(p => p.Seconds),
            AttemptedCount = scoped.Count,
            FirstTryCorrect = firstCorrect,
            AccuracyPercent = CourseProgressRules.Percent(firstCorrect, scoped.Count),
        };
    }
}
