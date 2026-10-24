using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Courses;

/// <summary>
/// Die gemeinsamen Zähl- und Zugriffsregeln der Kurs-Dienste — Kursliste (<see cref="CourseService"/>),
/// Solver (<see cref="CourseSolverService"/>), Statistik (<see cref="CourseScopeStatistics"/>) und Detailseite
/// (<see cref="CourseAuthoringService"/>). Bis zur Zerlegung (Codereview 2026-09-29, Z4/Z16) stand jede
/// dieser Regeln als private Methode im <see cref="CourseService"/>, <c>Percent</c> zusätzlich als Kopie in
/// der Detailseite; jetzt gibt es je Regel genau EINE Definition.
/// </summary>
internal static class CourseProgressRules
{
    /// <summary>Fortschritt in Prozent, gerundet; mehr Erledigtes als Gesamtzahl wird gedeckelt, 0 Gesamt = 0 %.</summary>
    internal static int Percent(int solved, int total) =>
        total <= 0 ? 0 : (int)Math.Round(100.0 * Math.Min(solved, total) / total);

    /// <summary>Die REIHENFOLGE, in der ein Kurs seine Linien ausgibt („sequential"/„random").
    /// <para>Hieß bis 0.499.11 <c>NormalizeMode</c> — direkt neben <see cref="SolveMode.Normalize"/>,
    /// das die SPIELWEISE („training"/„easy") normalisiert. In <see cref="CourseSolverService.RecordResultAsync"/> stehen
    /// beide Bedeutungen nebeneinander (<c>dto.Mode</c> gegen <c>dto.SolveMode</c>); zwei fast gleiche
    /// Namen für zwei verschiedene Dinge sind dort eine Falle und keine Kürze.</para></summary>
    internal static string NormalizeOrderMode(string? mode) =>
        (mode ?? string.Empty).Trim().ToLowerInvariant() == "random" ? "random" : "sequential";

    /// <summary>Gelöste QUIZ-Linien des Users — die Zählbasis jedes Kurs-Fortschritts. Info-/Erklärlinien
    /// sind kein Quiz und zählen nicht (wie in <see cref="CourseService.GetChaptersAsync"/>, im Solver-Fortschritt
    /// und auf der Detailseite). Ergebnisse an Info-Linien gibt es trotzdem: ein Re-Import stellt eine
    /// gelöste Linie auf <see cref="BookPuzzle.IsInfoOnly"/> um und lässt das Ergebnis stehen. Ohne diesen
    /// Filter zeigten Kursliste und Buch-Statistik dann mehr Gelöstes als Kapitel und Detailseite
    /// (Codereview A7-009).</summary>
    internal static IQueryable<CoursePuzzleResult> SolvedQuizResults(AppDbContext db, int userId) =>
        db.CoursePuzzleResults.Where(cr => cr.UserId == userId
            && cr.BookPuzzle != null && !cr.BookPuzzle.IsInfoOnly);

    /// <summary>
    /// Die Versuche eines Users in einem Buch, die seit dem letzten Reset zählen: nach dem buchweiten
    /// <see cref="CourseProgress.ResetAt"/> (<paramref name="bookResetAt"/>, <see cref="DateTime.MinValue"/> = nie)
    /// UND nach dem Kapitel-Reset der jeweiligen Linie (<see cref="CourseLineReset"/>). Die EINE Regel für den
    /// Aufgaben-Pool, die Statistik und den ✓/✗-Stand — der Kapitel-Reset lässt die Versuche als Zeit-Log stehen,
    /// ohne diesen Filter blieben die zurückgesetzten Linien aus dem Pool ausgeschlossen.
    /// </summary>
    internal static IQueryable<CourseAttempt> AttemptsSinceReset(AppDbContext db, int userId, int bookId, DateTime bookResetAt)
        => db.CourseAttempts.Where(a => a.UserId == userId && a.BookId == bookId && a.AttemptedAt >= bookResetAt
            && !db.CourseLineResets.Any(r => r.UserId == userId && r.BookPuzzleId == a.BookPuzzleId && r.ResetAt > a.AttemptedAt));

    /// <summary>Kein Kurszugriff (<see cref="CourseAccess.CanAccessAsync"/>) → <see cref="NotFoundException"/> (404).</summary>
    internal static async Task EnsureAccessAsync(AppDbContext db, int userId, int bookId, bool isAdmin)
    {
        if (!await CourseAccess.CanAccessAsync(db, userId, bookId, isAdmin))
            throw new NotFoundException("Book not found.");
    }

    /// <summary>Schränkt eine Puzzle-Query auf ein Kapitel ein (null = Sammel-„ohne Kapitel").</summary>
    internal static IQueryable<BookPuzzle> FilterByChapter(IQueryable<BookPuzzle> q, string? chapterName) =>
        chapterName == null
            ? q.Where(bp => bp.Chapter == null || bp.Chapter == "")
            : q.Where(bp => bp.Chapter == chapterName);

    /// <summary>Löst einen Kapitel-Index in den (normalisierten) Kapitelnamen auf. Ungültiger/keiner
    /// Index → <c>(null, false)</c> = buchweit (kein Kapitel-Scope). Die Indizes kommen aus
    /// <see cref="ChapterOrder.GetOrderedChapterNamesAsync"/> — derselbe Kontrakt wie
    /// <see cref="CourseService.GetChaptersAsync"/>.</summary>
    internal static async Task<(string? Name, bool Scoped)> ResolveChapterAsync(AppDbContext db, int bookId, int? chapterIndex)
    {
        if (!chapterIndex.HasValue) return (null, false);
        var names = await ChapterOrder.GetOrderedChapterNamesAsync(db, bookId);
        return chapterIndex.Value >= 0 && chapterIndex.Value < names.Count
            ? (names[chapterIndex.Value], true)
            : (null, false);
    }
}
