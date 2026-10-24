using Microsoft.EntityFrameworkCore;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Kontolöschung, Bereich Repertoires: die Repertoires selbst und ihr Lernstand.</summary>
internal static class RepertoireDataEraser
{
    /// <summary>Die Repertoires (Dateien cascaden).</summary>
    internal static async Task RepertoiresAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        db.Repertoires.RemoveRange(await db.Repertoires.Where(r => r.UserId == userId).ToListAsync());
    }

    /// <summary>Karten-Zustände und eigene SR-Intervalle.</summary>
    internal static async Task LearningStateAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Repertoire-Lernstand + eigene SR-Intervalle: die Repertoires selbst gehen oben mit, ihre
        // Karten-Zustände hängen aber an (UserId, LineKey) und blieben sonst als Waisen liegen.
        db.RepertoireCardStates.RemoveRange(await db.RepertoireCardStates.Where(x => x.UserId == userId).ToListAsync());
        db.RepertoireSrSettings.RemoveRange(await db.RepertoireSrSettings.Where(x => x.UserId == userId).ToListAsync());
    }
}
