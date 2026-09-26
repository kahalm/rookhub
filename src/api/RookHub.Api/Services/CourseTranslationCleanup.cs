using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Raeumt die Kurs-Uebersetzungen (<see cref="Models.CommentSet.BookPuzzleId"/>) von Linien ab, die gerade
/// geloescht werden — gerufen von JEDEM Pfad, der <c>BookPuzzles</c> loescht
/// (<see cref="CourseAuthoringService"/> Linie/Kapitel, <see cref="BookAdminService.DeleteBookAsync"/> und damit
/// Kurs loeschen, Kurs → Repertoire, Konto loeschen; der Rueckbau in
/// <see cref="CourseService.UploadPersonalCourseAsync"/>). Markiert nur zum Loeschen; gespeichert wird im
/// SaveChanges des Aufrufers, zusammen mit den Linien.
///
/// <para><b>Die Saetze ausdruecklich, die Texte je nach Datenbank.</b> In MariaDB nimmt der Fremdschluessel
/// (Cascade) die Texte mit dem Satz in EINER Anweisung mit; sie vorher zu laden hiesse, jede Uebersetzung des
/// Kurses (LONGTEXT) zu lesen und Zeile fuer Zeile einzeln zu loeschen — bei einem uebersetzten Kurs zehntausende
/// Anweisungen. Die InMemory-Datenbank der Tests kaskadiert dagegen nicht; dort werden die Texte ausdruecklich
/// mitgeloescht.</para>
/// </summary>
public static class CourseTranslationCleanup
{
    /// <summary>Kurs-Saetze (und unter InMemory ihre Texte) der Linien <paramref name="lineIds"/> zum Loeschen
    /// markieren — als Unterabfrage (Buch loeschen: „alle Linien des Buchs").</summary>
    public static async Task RemoveForLinesAsync(AppDbContext db, IQueryable<int> lineIds, CancellationToken ct = default)
        => await RemoveAsync(db, await db.CommentSets
            .Where(s => s.BookPuzzleId != null && lineIds.Contains(s.BookPuzzleId.Value))
            .ToListAsync(ct), ct);

    /// <summary>Dasselbe fuer eine feste Liste von Linien-Ids (Linie/Kapitel loeschen).</summary>
    public static async Task RemoveForLinesAsync(AppDbContext db, IReadOnlyCollection<int> lineIds,
        CancellationToken ct = default)
    {
        if (lineIds.Count == 0) return;
        await RemoveAsync(db, await db.CommentSets
            .Where(s => s.BookPuzzleId != null && lineIds.Contains(s.BookPuzzleId.Value))
            .ToListAsync(ct), ct);
    }

    private static async Task RemoveAsync(AppDbContext db, List<Models.CommentSet> sets, CancellationToken ct)
    {
        if (sets.Count == 0) return;
        if (!db.Database.IsRelational())
        {
            var setIds = sets.Select(s => s.Id).ToList();
            db.CommentTexts.RemoveRange(await db.CommentTexts.Where(t => setIds.Contains(t.CommentSetId)).ToListAsync(ct));
        }
        db.CommentSets.RemoveRange(sets);
    }
}
