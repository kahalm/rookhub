using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services.Scoresheet;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Kontolöschung, Bereich Partieformulare: Einlesungen samt Seiten, Ansichten und Archiv — ohne die Fotos zu laden.</summary>
internal static class ScoresheetDataEraser
{
    /// <summary>Formular-Fotos, Seiten, Ansichten, aufbewahrte Liga-Einlesungen.</summary>
    internal static async Task ScansAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Formular-Fotos zuerst (sie hängen an den Partien, tragen aber auch Namen und Handschrift — und eine
        // gescheiterte Einlesung hat gar keine Partie). Ohne das Foto zu laden: es ist das Schwergewicht der Zeile.
        var scanKeys = await ScoresheetScanService.KeysAsync(db.ScoresheetScans.Where(s => s.UserId == userId));
        ScoresheetScanService.RemovePagesWithoutLoading(db,
            await ScoresheetScanService.PageKeysAsync(db, scanKeys.Select(k => k.Id).ToList()));
        ScoresheetScanService.RemoveViewsWithoutLoading(db,
            await ScoresheetScanService.ViewKeysAsync(db, scanKeys.Select(k => k.Id).ToList()));
        // Aufbewahrte Liga-Einlesungen (0.655.0) gehen mit — ohne die Fotos zu laden (MariaDB cascadet ohnehin, InMemory nicht).
        var scanIds = scanKeys.Select(k => k.Id).ToList();
        foreach (var archiveId in await db.ScoresheetScanArchives.Where(a => scanIds.Contains(a.ScoresheetScanId)).Select(a => a.Id).ToListAsync())
        {
            var stub = new ScoresheetScanArchive { Id = archiveId };
            db.ScoresheetScanArchives.Attach(stub);
            db.ScoresheetScanArchives.Remove(stub);
        }
        ScoresheetScanService.RemoveWithoutLoading(db, scanKeys);
    }
}
