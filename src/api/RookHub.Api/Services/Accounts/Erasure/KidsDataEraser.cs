using Microsoft.EntityFrameworkCore;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Kontolöschung, Bereich KidHub: Spielstand des Kindes.</summary>
internal static class KidsDataEraser
{
    /// <summary>Level-, Kurs- und Linienfortschritt.</summary>
    internal static async Task ProgressAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // KidHub-Fortschritt: Spielstand des Kindes, keine Statistik fuer andere — geht mit dem Konto.
        db.KidsLevelProgresses.RemoveRange(await db.KidsLevelProgresses.Where(p => p.UserId == userId).ToListAsync());
        db.KidsCourseProgresses.RemoveRange(await db.KidsCourseProgresses.Where(p => p.UserId == userId).ToListAsync());
        db.KidsCourseLines.RemoveRange(await db.KidsCourseLines.Where(l => l.UserId == userId).ToListAsync());
    }
}
