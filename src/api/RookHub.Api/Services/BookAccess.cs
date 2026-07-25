using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// EINE Quelle für die Lese-Zugriffsregel auf ein <see cref="Book"/> in den OFFENEN
/// Buch-Puzzle-Endpoints (<see cref="Controllers.BookPuzzleController"/>).
///
/// <para>Hintergrund: der Buch-/Kurs-Inhalt war über die anonymen Endpoints vollständig abziehbar —
/// <c>GET /api/book-puzzles/books</c> listete ALLE Bücher (inkl. persönlicher Chessable-Importe fremder
/// Nutzer), <c>?bookId=</c> überschrieb bei <c>/random</c> den Pool-Filter ohne jede Prüfung, und
/// <c>{id}/next</c> lief ein Buch von jedem Einstiegspunkt aus komplett durch. Das Gruppen-/Freigabe-Gating
/// der Kurs-Endpoints (<see cref="CourseService.CanAccessAsync"/>) war damit faktisch wirkungslos.</para>
///
/// <para><b>Regel „öffentlich sichtbar"</b>: ein Buch ist anonym lesbar, wenn ein Admin es bewusst
/// geöffnet hat — <see cref="Book.IsPublic"/> (öffentlicher Kurs) oder Mitgliedschaft in einem offenen
/// Pool (<see cref="Book.ForDaily"/>/<see cref="Book.ForRandom"/>/<see cref="Book.ForBlind"/>; deren
/// Inhalte werden ohnehin anonym ausgewürfelt und geteilt). Persönliche Importe und rein
/// gruppen-freigegebene Kurse tragen keins dieser Flags → geschützt. Eingeloggte bekommen zusätzlich
/// ihre eigenen, die mit ihnen geteilten und die über eine Gruppe freigegebenen Bücher.</para>
///
/// <para>Bewusst NICHT identisch mit <see cref="CourseService.CanAccessAsync"/>: die Pool-Flags öffnen
/// nur den Zugriff auf EINZELNE Puzzles/Zufallsziehungen, nicht den strukturierten Kurs (Kapitel,
/// Fortschritt, Offline-Export). Die Kurs-Regel bleibt daher strenger.</para>
///
/// <para><b>Absichtlich weiterhin offen</b>: <c>GET /api/book-puzzles/{id}</c> (Einzel-Puzzle per ID).
/// Darauf beruhen die Teilen-Links, das Tagespuzzle, die OG-Vorschaubilder und der schach-bot-Lookup
/// per LineId — ein Gate dort würde diese Features brechen, und wer die konkrete Id kennt, hat sie
/// bereits aus einem geteilten Link.</para>
/// </summary>
public static class BookAccess
{
    /// <summary>Bücher, die ein Admin bewusst anonym geöffnet hat (öffentlicher Kurs oder offener Pool).</summary>
    public static IQueryable<Book> PubliclyExposed(AppDbContext db) =>
        db.Books.Where(b => b.IsPublic || b.ForDaily || b.ForRandom || b.ForBlind);

    /// <summary>Alle Bücher, die der (ggf. anonyme) Aufrufer über die offenen Buch-Endpoints lesen darf.
    /// Admins sehen alles; sonst öffentlich/Pool + (eingeloggt) eigene, geteilte und gruppen-freigegebene.</summary>
    public static IQueryable<Book> ReadableBy(AppDbContext db, int? userId, bool isAdmin)
    {
        if (isAdmin) return db.Books;
        if (userId is not int uid) return PubliclyExposed(db);
        var everyoneIds = db.Groups.Where(g => g.IsEveryone).Select(g => g.Id);
        return db.Books.Where(b =>
            b.IsPublic || b.ForDaily || b.ForRandom || b.ForBlind
            || b.OwnerUserId == uid
            || db.CourseShares.Any(cs => cs.BookId == b.Id && cs.RecipientId == uid)
            || db.BookGroupAccesses.Any(a => a.BookId == b.Id &&
                (everyoneIds.Contains(a.GroupId) ||
                 db.UserGroups.Any(ug => ug.UserId == uid && ug.GroupId == a.GroupId))));
    }

    /// <summary>Darf der (ggf. anonyme) Aufrufer dieses Buch lesen? <c>false</c> auch für unbekannte Ids.</summary>
    public static Task<bool> CanReadAsync(AppDbContext db, int bookId, int? userId, bool isAdmin,
        CancellationToken ct = default) =>
        ReadableBy(db, userId, isAdmin).AnyAsync(b => b.Id == bookId, ct);

    /// <summary>
    /// Zugriffsprüfung für ein einzelnes Buch-Puzzle über sein Buch. Altbestand ohne
    /// <see cref="BookPuzzle.BookId"/> wird über <see cref="BookPuzzle.BookFileName"/> aufgelöst; existiert
    /// dazu keine <see cref="Book"/>-Zeile, ist das Puzzle ungegatet (an so einem Buch kann auch keine
    /// Gruppen-/Personen-Freigabe hängen — die hängt ausschließlich an <see cref="Book"/>).
    /// </summary>
    public static async Task<bool> CanReadPuzzleAsync(AppDbContext db, BookPuzzle puzzle, int? userId,
        bool isAdmin, CancellationToken ct = default)
    {
        if (puzzle.BookId is int bookId)
            return await CanReadAsync(db, bookId, userId, isAdmin, ct);

        var legacyId = await db.Books.Where(b => b.FileName == puzzle.BookFileName)
            .Select(b => (int?)b.Id).FirstOrDefaultAsync(ct);
        return legacyId is not int id || await CanReadAsync(db, id, userId, isAdmin, ct);
    }
}
