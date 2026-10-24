using Microsoft.EntityFrameworkCore;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Kontolöschung, Bereich ClubHub: Verknüpfung mit dem Karteiblatt, Trainer-Zuteilungen.</summary>
internal static class ClubDataEraser
{
    /// <summary>Karteiblatt lösen (bleibt), Trainer-Zuteilungen löschen.</summary>
    internal static async Task MembershipAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // ClubHub: die Verknüpfung mit dem Karteiblatt lösen (das Blatt gehört dem Verein und bleibt) und Trainer-
        // Zuteilungen entfernen — das Konto wird IN PLACE anonymisiert, weder SetNull noch Cascade feuern.
        foreach (var m in await db.ClubMembers.Where(m => m.LinkedUserId == userId).ToListAsync())
            m.LinkedUserId = null;
        db.ClubGroupTrainers.RemoveRange(await db.ClubGroupTrainers.Where(t => t.UserId == userId).ToListAsync());
    }
}
