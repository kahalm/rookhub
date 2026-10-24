using Microsoft.EntityFrameworkCore;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Kontolöschung, Bereich Partien: gespeicherte und rekonstruierte Partien, geteilte Linien.</summary>
internal static class GamesDataEraser
{
    /// <summary>Gespeicherte Partien (/g/), Rekonstruktionen, geteilte Linien (/l/).</summary>
    internal static async Task SavedAndSharedAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Öffentlich abrufbare Inhalte mit Klarnamen/Fremddaten: geteilte Partien (/g/{token}) und
        // geteilte Linien (/l/{token}) — die Share-Links müssen mit dem Konto verschwinden.
        db.SavedGames.RemoveRange(await db.SavedGames.Where(g => g.UserId == userId).ToListAsync());
        // „Partie rekonstruieren": erst die Teile, dann die Kopfzeilen — die Löschung anonymisiert
        // nur die Nutzerzeile, es feuert also kein Cascade-FK (und InMemory cascadet ohnehin nicht).
        var reconstructionIds = await db.GameReconstructions.Where(r => r.UserId == userId).Select(r => r.Id).ToListAsync();
        if (reconstructionIds.Count > 0)
        {
            db.GameReconstructionParts.RemoveRange(
                await db.GameReconstructionParts.Where(p => reconstructionIds.Contains(p.GameReconstructionId)).ToListAsync());
            db.GameReconstructions.RemoveRange(
                await db.GameReconstructions.Where(r => r.UserId == userId).ToListAsync());
        }
        db.SharedLines.RemoveRange(await db.SharedLines.Where(l => l.OwnerUserId == userId).ToListAsync());
    }
}
