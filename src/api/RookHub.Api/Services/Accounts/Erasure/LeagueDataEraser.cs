using Microsoft.EntityFrameworkCore;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Kontolöschung, Bereich LeagueHub: Hochladenden-Vermerk der Vereinspartien, Import-Entwürfe, Stapel-Uploads.</summary>
internal static class LeagueDataEraser
{
    /// <summary>Vereinspartien behalten, Hochladenden lösen; Entwürfe und Stapel-Uploads löschen.</summary>
    internal static async Task UploadsAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Vereins-Datenbank (LeagueHub): die Partien bleiben (sie gehören zur Liga, nicht zum Konto), nur der Vermerk,
        // wer sie hochgeladen hat, geht — bei anonymisierten gibt es ihn ohnehin nicht.
        foreach (var g in await db.LeagueClubGames.IgnoreQueryFilters().Where(g => g.UploadedByUserId == userId).ToListAsync())
            g.UploadedByUserId = null;
        // Offene Entwürfe von PGN-Importen (0.595.0) gehen ganz — sie tragen den Rohtext samt Klarnamen.
        db.LeagueClubDrafts.RemoveRange(await db.LeagueClubDrafts.Where(d => d.UserId == userId).ToListAsync());
        // Stapel-Uploads (0.651.0): die Bilder gehören dem Hochladenden — mit dem Konto weg (Bilder per Cascade).
        db.LeagueBatchUploads.RemoveRange(await db.LeagueBatchUploads.Where(b => b.UserId == userId).ToListAsync());
    }
}
