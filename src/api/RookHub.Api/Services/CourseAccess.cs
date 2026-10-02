using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
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
    /// <summary>Darf der User dieses (existierende) Buch als Kurs SEHEN (öffnen, lösen, exportieren)?
    /// Admin: immer. Sonst: öffentlicher Kurs, eigenes Buch, direkt geteilt, im Verteiler einer
    /// Kalkulations-Serie, oder über eine Gruppe (inkl. „Everyone") freigegeben. Unbekannte BookId →
    /// <c>false</c>. Bearbeiten braucht zusätzlich Besitzer oder Admin (<see cref="LoadManageableAsync"/>);
    /// die Kursliste ist nur eine Teilmenge (öffentlich nur, wenn angepinnt — <c>CourseService.ListedCourses</c>).</summary>
    public static Task<bool> CanAccessAsync(AppDbContext db, int userId, int bookId, bool isAdmin,
        CancellationToken ct = default)
    {
        // Auch für Admins gilt: ein Buch, das es nicht gibt, ist nicht zugänglich (404 statt 200).
        if (isAdmin) return db.Books.AnyAsync(b => b.Id == bookId, ct);

        // EINE Abfrage statt bis zu sieben. Die Zweige sind sämtlich ODER-verknüpft, es gab also
        // nie einen Grund, sie nacheinander zu fragen — und die Prüfung hängt an jedem
        // Kurs-Endpunkt (allein CourseService ruft sie an 17 Stellen). Vorbild: BookAccess.ReadableBy.
        var everyoneIds = db.Groups.Where(g => g.IsEveryone).Select(g => g.Id);
        return db.Books.AnyAsync(b => b.Id == bookId && (
            // Öffentlicher Kurs: für JEDEN (auch eingeloggt ohne Gruppen-Freigabe) über den
            // Direkt-Link nutzbar — der eingeloggte Nutzer bekommt dabei serverseitigen Fortschritt.
            b.IsPublic
            // Persönliches Buch des Users (z. B. eigener Chessable-Import) ist immer sichtbar.
            || b.OwnerUserId == userId
            // Ein anderer Nutzer hat mir diesen Kurs direkt geteilt.
            || db.CourseShares.Any(cs => cs.BookId == b.Id && cs.RecipientId == userId)
            // Kalkulations-Serie (privater Verteiler): steht der Nutzer für dieses Buch im
            // Verteiler, sieht er den Kurs — auch wenn das Buch nicht (mehr) öffentlich ist.
            // Trägt so das „privat" der Serie: sobald IsPublic aus ist, gilt hier die
            // Mitgliedschaft (siehe CalcSeriesMember).
            || db.CalcSeriesMembers.Any(m => m.BookId == b.Id && m.UserId == userId)
            // Über eine Gruppe freigegeben — „Everyone" eingeschlossen.
            || db.BookGroupAccesses.Any(a => a.BookId == b.Id &&
                   (everyoneIds.Contains(a.GroupId)
                    || db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == a.GroupId)))
        ), ct);
    }

    /// <summary>Antwort der Besitzer-oder-Admin-Regel (<see cref="LoadManageableAsync"/>) — überall dieselbe.</summary>
    public const string ManageForbiddenMessage = "Only the owner or an admin may edit this course.";

    /// <summary>
    /// DIE Besitzer-oder-Admin-Regel für das Verwalten eines Kurs-Buchs (Inhalte, Themen, Kalkulations-Serie):
    /// nicht lesbar oder unbekannt → <see cref="NotFoundException"/> (404, kein Existenz-Orakel), lesbar aber
    /// weder Besitzer noch Admin → <see cref="ForbiddenException"/> (403). Beides wird vom globalen
    /// <c>DomainExceptionFilter</c> zu <c>{ message }</c>. Liefert das (getrackte) Buch.
    ///
    /// <para>Vorher stand die Regel je Dienst mit eigener Antwort: <c>UnauthorizedAccessException</c> mit
    /// verschiedenen Texten (Inhaltspflege, Themen) und ein <c>bool</c>, aus dem der Controller ein
    /// <c>Forbid()</c> ohne Rumpf machte — auch für ein Buch, das es gar nicht gibt (Kalkulations-Serie,
    /// Codereview A7-011).</para>
    /// </summary>
    public static async Task<Book> LoadManageableAsync(AppDbContext db, int userId, int bookId, bool isAdmin,
        CancellationToken ct = default)
    {
        if (!await CanAccessAsync(db, userId, bookId, isAdmin, ct))
            throw new NotFoundException("Book not found.");
        var book = await db.Books.FirstAsync(b => b.Id == bookId, ct);
        if (!isAdmin && book.OwnerUserId != userId)
            throw new ForbiddenException(ManageForbiddenMessage);
        return book;
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
