using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Räumt alles ab, was an Linien (<see cref="BookPuzzle"/>) hängt, die gerade gelöscht werden — EINE Liste
/// für beide Löschpfade: Linie/Kapitel löschen (<see cref="CourseAuthoringService"/>) und Buch löschen
/// (<see cref="BookAdminService.DeleteBookAsync"/>, damit auch Kurs löschen, Kurs → Repertoire, Konto löschen).
/// Markiert nur zum Löschen; gespeichert wird im SaveChanges des Aufrufers, zusammen mit den Linien.
///
/// <para><b>Warum eine Liste</b>: vorher führte jeder Pfad seine eigene. Eine neue Tabelle mit Restrict-FK
/// auf <see cref="BookPuzzle"/>, die nur in einer davon landet, bleibt in den InMemory-Tests unsichtbar
/// (InMemory prüft keine Fremdschlüssel) und lässt „Linie löschen" bzw. „Kurs löschen" erst auf MariaDB mit
/// einem FK-Fehler scheitern (Codereview A9-007). <c>BookPuzzleDependentsTests</c> hält
/// <see cref="RestrictDependents"/> gegen das EF-Modell fest.</para>
///
/// <para>Dazu kommen Verweise OHNE Fremdschlüssel, die sonst niemand abräumt: „Track solves" geteilter
/// Einzel-Puzzles (<see cref="SharedPuzzleAttempt"/>), Favoriten auf Buch-Linien (polymorph,
/// <see cref="FavoritePuzzle"/> — die Liste ließ tote Einträge aus, die Dashboard-Kachel zählte sie mit) und die
/// Kurs-Übersetzungen (<see cref="CourseTranslationCleanup"/>). Herausforderungen an eine Buch-Linie bleiben
/// bewusst stehen: sie gehören zwei Nutzern und sind deren gemeinsame Historie.</para>
/// </summary>
public static class BookPuzzleDependents
{
    private delegate Task Remover(AppDbContext db, IQueryable<int> lineIds, CancellationToken ct);

    /// <summary>Je Tabelle mit Restrict-FK auf <see cref="BookPuzzle"/> ihr Abräumer — Typ und Löschung
    /// stehen absichtlich in EINER Zeile, damit die Liste, die der Wächtertest prüft, und die Löschung, die
    /// tatsächlich läuft, nicht auseinanderlaufen können.</summary>
    private static readonly (Type Entity, Remover Remove)[] Restrict =
    {
        (typeof(CoursePuzzleResult), async (db, ids, ct) => db.CoursePuzzleResults.RemoveRange(
            await db.CoursePuzzleResults.Where(x => ids.Contains(x.BookPuzzleId)).ToListAsync(ct))),
        (typeof(CourseAttempt), async (db, ids, ct) => db.CourseAttempts.RemoveRange(
            await db.CourseAttempts.Where(x => ids.Contains(x.BookPuzzleId)).ToListAsync(ct))),
        (typeof(CourseInfoView), async (db, ids, ct) => db.CourseInfoViews.RemoveRange(
            await db.CourseInfoViews.Where(x => ids.Contains(x.BookPuzzleId)).ToListAsync(ct))),
        (typeof(BookPuzzleAttempt), async (db, ids, ct) => db.BookPuzzleAttempts.RemoveRange(
            await db.BookPuzzleAttempts.Where(x => ids.Contains(x.BookPuzzleId)).ToListAsync(ct))),
        (typeof(DailyPuzzle), async (db, ids, ct) => db.DailyPuzzles.RemoveRange(
            await db.DailyPuzzles.Where(x => ids.Contains(x.BookPuzzleId)).ToListAsync(ct))),
        (typeof(CalculationTree), async (db, ids, ct) => db.CalculationTrees.RemoveRange(
            await db.CalculationTrees.Where(x => ids.Contains(x.BookPuzzleId)).ToListAsync(ct))),
        (typeof(CourseFlashcardMark), async (db, ids, ct) => db.CourseFlashcardMarks.RemoveRange(
            await db.CourseFlashcardMarks.Where(x => ids.Contains(x.BookPuzzleId)).ToListAsync(ct))),
    };

    /// <summary>Die Tabellen mit Restrict-FK auf <see cref="BookPuzzle"/>, die <see cref="RemoveForLinesAsync"/>
    /// abräumt (für den Wächtertest gegen das Modell).</summary>
    internal static IReadOnlyList<Type> RestrictDependents { get; } = Restrict.Select(r => r.Entity).ToList();

    /// <summary>Alle abhängigen Datensätze der Linien <paramref name="lineIds"/> zum Löschen markieren —
    /// als Unterabfrage (Buch: „alle Linien des Buchs", Linie/Kapitel: „diese Ids").</summary>
    public static async Task RemoveForLinesAsync(AppDbContext db, IQueryable<int> lineIds, CancellationToken ct = default)
    {
        foreach (var (_, remove) in Restrict)
            await remove(db, lineIds, ct);
        db.SharedPuzzleAttempts.RemoveRange(
            await db.SharedPuzzleAttempts.Where(a => lineIds.Contains(a.BookPuzzleId)).ToListAsync(ct));
        db.FavoritePuzzles.RemoveRange(await db.FavoritePuzzles
            .Where(f => f.Source == PuzzleSource.Book && lineIds.Contains(f.PuzzleId)).ToListAsync(ct));
        await CourseTranslationCleanup.RemoveForLinesAsync(db, lineIds, ct);
    }
}
