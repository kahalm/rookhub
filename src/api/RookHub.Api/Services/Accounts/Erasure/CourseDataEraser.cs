using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Kontolöschung, Bereich Kurse: persönliche Bücher, Kalk-Serie/Katalog, Kurs-Verknüpfungen, Übersetzungsaufträge.</summary>
internal static class CourseDataEraser
{
    /// <summary>Persönliche Bücher über den Admin-Löschpfad — speichert je Buch selbst, steht deshalb ZUERST.</summary>
    internal static async Task OwnedBooksAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // 0) Persönliche Bücher (importierte/erstellte Kurse) samt Abhängigen über den bestehenden
        //    Admin-Löschpfad entfernen — der räumt Puzzles, Fortschritte, Freigaben, Links etc.
        //    konsistent ab (jeder DeleteBookAsync speichert für sich; ein Wiederholungslauf nach
        //    einem Teil-Fehlschlag ist dank Idempotenz der Restschritte unkritisch).
        var ownedBookIds = await db.Books.Where(b => b.OwnerUserId == userId).Select(b => b.Id).ToListAsync();
        foreach (var bookId in ownedBookIds)
            await ctx.BookAdmin.DeleteBookAsync(bookId);
    }

    /// <summary>„Gesehen“-Vermerke der Kalkulations-Serie, Katalog-Freigaben und -Anfragen.</summary>
    internal static async Task CalcAndCatalogAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // „Gesehen"-Vermerke der Kalkulations-Serie: WER WANN welche Ausgabe geöffnet hat — ein
        // personenbezogenes Nutzungsprotokoll, keine Statistik (der Verteiler ist ohnehin weg).
        db.CalcEditionViews.RemoveRange(await db.CalcEditionViews.Where(x => x.UserId == userId).ToListAsync());
        // Katalog-Freigaben/-Anfragen des Nutzers (als Besitzer UND als Anfragender): Freigaben auf
        // Inhalte eines gelöschten Kontos sind gegenstandslos, die Anfrage nennt beide Personen.
        db.CatalogGrants.RemoveRange(await db.CatalogGrants
            .Where(x => x.OwnerUserId == userId || x.SubjectUserId == userId).ToListAsync());
        db.CatalogRequests.RemoveRange(await db.CatalogRequests
            .Where(x => x.OwnerUserId == userId || x.RequesterUserId == userId).ToListAsync());
    }

    /// <summary>Persönliche Kurs-Verknüpfungen und Puzzle-Favoriten.</summary>
    internal static async Task LinksAndFavoritesAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Persönliche Kurs-Verknüpfung (Buch↔Workbook) und Puzzle-Favoriten: reine Nutzerwahl.
        db.CourseLinks.RemoveRange(await db.CourseLinks.Where(x => x.UserId == userId).ToListAsync());
        db.FavoritePuzzles.RemoveRange(await db.FavoritePuzzles.Where(x => x.UserId == userId).ToListAsync());
    }

    /// <summary>Offene Kurs-Übersetzungsaufträge zurückziehen (nicht löschen).</summary>
    internal static async Task TranslationJobsAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Offene Kurs-Übersetzungsaufträge des Nutzers (RequestedByUserId hat bewusst keinen FK): nach der Löschung
        // wartet niemand mehr darauf, und sie hielten den Platz in der Warteschlange. Zurückgezogen statt gelöscht —
        // ein LAUFENDER bricht beim nächsten Zwischenstand von selbst ab (CourseTranslationJobService.RunAsync).
        // Aufträge an EIGENEN Büchern sind oben schon mit dem Buch gegangen.
        foreach (var job in await db.CourseTranslationJobs.Where(j => j.RequestedByUserId == userId
                     && (j.Status == CourseTranslationJobStatus.Queued || j.Status == CourseTranslationJobStatus.Running))
                     .ToListAsync())
        {
            job.Status = CourseTranslationJobStatus.Cancelled;
            job.FinishedAt = DateTime.UtcNow;
            job.LastError = "account deleted";
        }
    }
}
