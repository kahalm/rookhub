using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// EINE Quelle für die Kurs-Zugriffsregel auf ein <see cref="Book"/> (strukturierter Kurs:
/// Kapitel, Fortschritt, Offline-Export, Kalkulations-Modus). Bewusst STRENGER als
/// <see cref="BookAccess"/>: die Pool-Flags (ForDaily/ForRandom/ForBlind) öffnen nur einzelne
/// Puzzles/Zufallsziehungen, nicht den Kurs.
///
/// <para>Herausgezogen aus <see cref="CourseService.CanAccessAsync"/> (das hierher delegiert),
/// damit weitere Dienste (z. B. <see cref="CalculationService"/>) dieselbe Regel nutzen können,
/// ohne den schwergewichtigen <see cref="CourseService"/> injizieren zu müssen.</para>
/// </summary>
public static class CourseAccess
{
    /// <summary>Darf der User dieses (existierende) Buch als Kurs sehen/bearbeiten?
    /// Admin: immer. Sonst: öffentlicher Kurs, eigenes Buch, direkt geteilt, oder über eine
    /// Gruppe (inkl. „Everyone") freigegeben. Unbekannte BookId → <c>false</c>.</summary>
    public static async Task<bool> CanAccessAsync(AppDbContext db, int userId, int bookId, bool isAdmin,
        CancellationToken ct = default)
    {
        if (!await db.Books.AnyAsync(b => b.Id == bookId, ct)) return false;
        if (isAdmin) return true;
        // Öffentlicher Kurs: für JEDEN (auch eingeloggt ohne Gruppen-Freigabe) über den Direkt-Link
        // nutzbar — der eingeloggte Nutzer bekommt dabei serverseitigen Fortschritt.
        if (await db.Books.AnyAsync(b => b.Id == bookId && b.IsPublic, ct)) return true;
        // Persönliches Buch des Users (z. B. eigener Chessable-Import) ist immer sichtbar.
        if (await db.Books.AnyAsync(b => b.Id == bookId && b.OwnerUserId == userId, ct)) return true;
        // Ein anderer Nutzer hat mir diesen Kurs direkt geteilt.
        if (await db.CourseShares.AnyAsync(cs => cs.BookId == bookId && cs.RecipientId == userId, ct)) return true;
        // Kalkulations-Serie (privater Verteiler): steht der Nutzer für dieses Buch im Verteiler,
        // sieht er den Kurs — auch wenn das Buch nicht (mehr) öffentlich ist. Trägt so das „privat"
        // der Serie: sobald IsPublic aus ist, gilt hier die Mitgliedschaft (siehe CalcSeriesMember).
        if (await db.CalcSeriesMembers.AnyAsync(m => m.BookId == bookId && m.UserId == userId, ct)) return true;
        var everyoneId = await db.Groups.Where(g => g.IsEveryone).Select(g => (int?)g.Id).FirstOrDefaultAsync(ct);
        return await db.BookGroupAccesses.AnyAsync(a => a.BookId == bookId &&
            (a.GroupId == everyoneId ||
             db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == a.GroupId)), ct);
    }

    /// <summary>
    /// Ist dieses Buch ein KALKULATIONSBUCH (<see cref="Book.IsCalculation"/>)? Unbekannte BookId
    /// → <c>false</c>.
    ///
    /// <para><b>Warum das eine Zugriffsfrage ist</b>: ein Kalkulationsbuch wird als Stellung OHNE
    /// Lösung serviert — <see cref="BookPuzzle.Moves"/> verlässt den Server bewusst nicht (siehe
    /// <see cref="CalculationService"/>). Die SOLVER-Pfade liefern dieselben Linien aber über
    /// <see cref="BookPuzzleService.MapToDto"/> samt <c>Moves</c> aus. Ein Kalkulationsbuch ist
    /// kein Solver-Kurs; diese Pfade behandeln es deshalb wie ein nicht vorhandenes Buch (404) —
    /// sonst zwänge schon das Freischalten eines Kalkulationskurses (nötig für <c>/{slug}</c>) zur
    /// Preisgabe der Lösung über den Nachbar-Endpoint.</para>
    ///
    /// <para>Der Schalter bleibt beim Besitzer/Admin (<c>PUT /api/courses/{bookId}/calculation</c>):
    /// wer die Zugfolgen wieder über die Kurs-Pfade braucht, schaltet den Kalkulations-Modus aus.</para>
    /// </summary>
    public static Task<bool> IsCalculationBookAsync(AppDbContext db, int bookId, CancellationToken ct = default)
        => db.Books.AnyAsync(b => b.Id == bookId && b.IsCalculation, ct);
}
