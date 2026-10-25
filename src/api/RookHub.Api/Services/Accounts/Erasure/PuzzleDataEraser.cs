using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Kontolöschung, Bereich Puzzles zwischen Freunden: Puzzle-Challenges und Revanche-Vermerke (A1-018,
/// Entscheidung User 25.10.: beide Tabellen gehen in BEIDEN Rollen mit dem Konto).</summary>
internal static class PuzzleDataEraser
{
    /// <summary>Puzzle-Challenges, die der Nutzer geschickt (From) ODER bekommen (To) hat. Beide FKs auf AppUser sind
    /// Restrict, die Puzzle-Id ist polymorph ohne FK — nichts anderes hängt an der Zeile.</summary>
    internal static async Task ChallengesAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        db.PuzzleChallenges.RemoveRange(
            await db.PuzzleChallenges.Where(c => c.FromUserId == userId || c.ToUserId == userId).ToListAsync());
        await RemoveDeadNotificationsAsync(ctx, NotificationType.ChallengeReceived, NotificationType.ChallengeResolved);
    }

    /// <summary>Revanche-Vermerke, in denen der Nutzer Rächer (Avenger) ODER Ziel (Target) ist. Die Zeile ist das
    /// abhängige Ende ihrer Restrict-FKs (AppUser, Puzzles) — sie zu löschen blockiert nichts; die Puzzles selbst bleiben.</summary>
    internal static async Task RevengeNotificationsAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        db.RevengeNotifications.RemoveRange(
            await db.RevengeNotifications.Where(n => n.AvengerUserId == userId || n.TargetUserId == userId).ToListAsync());
        await RemoveDeadNotificationsAsync(ctx, NotificationType.RevengePerformed);
    }

    /// <summary>
    /// Glocken-Einträge ANDERER Nutzer, die auf eine eben gelöschte Challenge bzw. Revanche des Kontos zeigen (Folge von
    /// A1-018, 0.833.1). Sie verweisen nicht per Id, sondern tragen nur den Namen des Auslösers (<c>data.username</c>) und
    /// den Link „/friends" — dort fehlt die Zeile danach, der Eintrag führte ins Leere. Da ALLE Challenges und Revanchen des
    /// Kontos in beiden Rollen gehen, ist jeder solche Eintrag mit seinem Namen tot: weg damit (statt ihn nur zu
    /// anonymisieren wie die übrigen Typen in <see cref="UserDataEraser"/>). Andere Typen (Freundschaft, Teilen …) bleiben.
    /// </summary>
    private static async Task RemoveDeadNotificationsAsync(UserErasureContext ctx, params string[] types)
    {
        var username = await ctx.Db.AppUsers.Where(u => u.Id == ctx.UserId).Select(u => u.Username).FirstOrDefaultAsync();
        if (username is null) return;
        var dead = (await NotificationService.MentioningUsernameAsync(ctx.Db, username, ctx.UserId))
            .Where(n => types.Contains(n.Type)).ToList();
        ctx.Db.Notifications.RemoveRange(dead);
    }
}
