using Microsoft.EntityFrameworkCore;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Kontolöschung, Bereich E-Books: Lesestand und Meldungen; die Bücher bleiben besitzerlos.</summary>
internal static class EbookDataEraser
{
    /// <summary>Lesestand, Diagramm-Meldungen und -Korrekturen; Besitz der Bücher lösen.</summary>
    internal static async Task ReadingAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // E-Book-Bibliothek: der Lesestand ist persönlich und geht; die BÜCHER bleiben besitzerlos stehen (nur noch
        // Verwalter pflegen sie) — ein Admin, der die 800-Bücher-Bibliothek eingelesen hat, nimmt sie beim Löschen seines
        // Kontos nicht mit, und ein für die Gruppe freigegebenes Buch verschwindet nicht aus deren Regal.
        db.EbookProgresses.RemoveRange(await db.EbookProgresses.Where(p => p.UserId == userId).ToListAsync());
        db.EbookDiagramReports.RemoveRange(await db.EbookDiagramReports.Where(r => r.UserId == userId).ToListAsync());
        db.EbookMissedDiagrams.RemoveRange(await db.EbookMissedDiagrams.Where(m => m.UserId == userId).ToListAsync());
        db.EbookDiagramCorrections.RemoveRange(await db.EbookDiagramCorrections.Where(c => c.UserId == userId).ToListAsync());
        foreach (var ebook in await db.Ebooks.Where(b => b.OwnerUserId == userId).ToListAsync())
            ebook.OwnerUserId = null;
    }
}
