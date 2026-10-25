using Microsoft.EntityFrameworkCore;

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
    }

    /// <summary>Revanche-Vermerke, in denen der Nutzer Rächer (Avenger) ODER Ziel (Target) ist. Die Zeile ist das
    /// abhängige Ende ihrer Restrict-FKs (AppUser, Puzzles) — sie zu löschen blockiert nichts; die Puzzles selbst bleiben.</summary>
    internal static async Task RevengeNotificationsAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        db.RevengeNotifications.RemoveRange(
            await db.RevengeNotifications.Where(n => n.AvengerUserId == userId || n.TargetUserId == userId).ToListAsync());
    }
}
