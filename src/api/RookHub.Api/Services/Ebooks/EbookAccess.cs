using RookHub.Api.Services.Accounts;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// EINE Quelle für die Sichtbarkeits- und Verwaltungsregel der E-Books (Vorbild <c>CourseAccess</c>).
///
/// <para><b>Sehen</b> darf ein Buch: der Besitzer, ein Admin (bzw. wer <c>ebooks.manage</c> hat) und jedes Mitglied
/// einer freigegebenen Gruppe — „Everyone" eingeschlossen, damit „für alle Angemeldeten" ohne Sonderfall geht.
/// <b>Verwalten</b> (Metadaten, Freigabe, Löschen) darf nur der Besitzer oder wer alles verwalten darf. Unsichtbar
/// und unbekannt sehen gleich aus (404, kein Existenz-Orakel).</para>
///
/// <para><b>Ohne Anmeldung</b> (0.778.0, <see cref="EbookCaller"/>): sichtbar sind die für „Everyone“ freigegebenen Bücher
/// und die eigenen anonymen Uploads (gleiche <see cref="Ebook.AnonymousSessionId"/>); verwalten darf man nur letztere.</para>
/// </summary>
public static class EbookAccess
{
    /// <summary>Alle für den Nutzer sichtbaren Bücher als Abfrage (noch ohne Include).</summary>
    public static IQueryable<Ebook> Visible(AppDbContext db, EbookCaller caller, bool manageAll)
    {
        if (manageAll && !caller.IsAnonymous) return db.Ebooks;
        var everyoneIds = db.Groups.Where(g => g.IsEveryone).Select(g => g.Id);
        if (caller.UserId is not int userId)
        {
            var anon = caller.AnonId;
            return db.Ebooks.Where(b =>
                (anon != null && b.OwnerUserId == null && b.AnonymousSessionId == anon)
                || db.EbookGroupAccesses.Any(a => a.EbookId == b.Id && everyoneIds.Contains(a.GroupId)));
        }
        var myGroups = GroupMembership.GroupIdsOf(db, userId);   // auch über enthaltene Gruppen
        return db.Ebooks.Where(b =>
            b.OwnerUserId == userId
            || db.EbookGroupAccesses.Any(a => a.EbookId == b.Id &&
                   (everyoneIds.Contains(a.GroupId)
                    || myGroups.Contains(a.GroupId))));
    }

    public static Task<bool> CanAccessAsync(AppDbContext db, EbookCaller caller, int ebookId, bool manageAll, CancellationToken ct = default)
        => Visible(db, caller, manageAll).AnyAsync(b => b.Id == ebookId, ct);

    public static bool CanManage(Ebook ebook, EbookCaller caller, bool manageAll)
        => (manageAll && !caller.IsAnonymous)
           || (caller.UserId is int uid && ebook.OwnerUserId == uid)
           || (caller.IsAnonymous && caller.AnonId != null && ebook.OwnerUserId == null && ebook.AnonymousSessionId == caller.AnonId);

    public const string ManageForbiddenMessage = "Only the uploader or an admin may change this e-book.";

    /// <summary>Lädt ein Buch samt Dateien und Freigaben zum Verwalten: unsichtbar/unbekannt → 404, sichtbar aber fremd → 403.</summary>
    public static async Task<Ebook> LoadManageableAsync(AppDbContext db, EbookCaller caller, int ebookId, bool manageAll, CancellationToken ct = default)
    {
        var ebook = await Visible(db, caller, manageAll)
            .Include(b => b.Files)
            .Include(b => b.GroupAccess)
            .FirstOrDefaultAsync(b => b.Id == ebookId, ct)
            ?? throw new NotFoundException("E-book not found.");
        if (!CanManage(ebook, caller, manageAll))
            throw new ForbiddenException(ManageForbiddenMessage);
        return ebook;
    }
}
