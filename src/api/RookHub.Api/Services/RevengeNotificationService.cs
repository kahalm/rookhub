using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class RevengeNotificationService
{
    private readonly AppDbContext _db;
    private readonly FriendService _friendService;
    private readonly NotificationService _notifications;

    public RevengeNotificationService(AppDbContext db, FriendService friendService, NotificationService notifications)
    {
        _db = db;
        _friendService = friendService;
        _notifications = notifications;
    }

    /// <summary>
    /// Hält fest, dass <paramref name="avengerId"/> ein gescheitertes Puzzle von <paramref name="targetId"/>
    /// angegangen ist (gelöst oder nicht), damit der Ziel-User informiert wird. Legt nur an, wenn: die beiden
    /// befreundet sind, der Target an diesem Puzzle tatsächlich gescheitert ist, UND der Avenger das Puzzle
    /// wirklich versucht hat. Das Ergebnis (<c>solved</c>) wird BEWUSST aus den echten <see cref="PuzzleAttempt"/>
    /// des Avengers hergeleitet, NICHT dem Client geglaubt — sonst könnte jeder eingeloggte User beliebige
    /// „X hat gelöst"-Benachrichtigungen bei jedem Freund fabrizieren/spammen. Zusätzlich Dedupe je
    /// (Avenger, Target, Puzzle). Fire-and-forget vom Puzzle-Solver; true = angelegt.
    /// </summary>
    public async Task<bool> RecordAsync(int avengerId, int targetId, int puzzleId)
    {
        if (avengerId == targetId) return false;
        if (!await _friendService.AreFriendsAsync(avengerId, targetId)) return false;

        var targetFailedIt = await _db.PuzzleAttempts.AnyAsync(a => a.UserId == targetId && a.PuzzleId == puzzleId && !a.Solved);
        if (!targetFailedIt) return false;

        // Ergebnis serverseitig aus den tatsächlichen Versuchen des Avengers herleiten (nicht dem Client glauben).
        // Kein Versuch des Avengers für dieses Puzzle → keine (fabrizierbare) Benachrichtigung.
        var avengerAttempts = await _db.PuzzleAttempts
            .Where(a => a.UserId == avengerId && a.PuzzleId == puzzleId)
            .Select(a => a.Solved)
            .ToListAsync();
        if (avengerAttempts.Count == 0) return false;
        var solved = avengerAttempts.Any(s => s);

        // Spam-/Doppel-Schutz: höchstens eine Revanche-Benachrichtigung je (Avenger, Target, Puzzle).
        if (await _db.RevengeNotifications.AnyAsync(n =>
                n.AvengerUserId == avengerId && n.TargetUserId == targetId && n.PuzzleId == puzzleId))
            return false;

        _db.RevengeNotifications.Add(new RevengeNotification
        {
            AvengerUserId = avengerId,
            TargetUserId = targetId,
            PuzzleId = puzzleId,
            Solved = solved
        });
        await _db.SaveChangesAsync();

        // In die generische Glocke spiegeln (Ziel-User informieren).
        var avengerName = await _db.AppUsers.Where(u => u.Id == avengerId).Select(u => u.Username).FirstOrDefaultAsync() ?? "?";
        await _notifications.CreateAsync(targetId, NotificationType.RevengePerformed,
            new Dictionary<string, string> { ["username"] = avengerName, ["solved"] = solved ? "true" : "false" }, "/friends");
        return true;
    }

    /// <summary>Revanche-Benachrichtigungen für einen User (neueste zuerst).</summary>
    public async Task<List<RevengeNotificationDto>> GetForUserAsync(int userId, int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        return await _db.RevengeNotifications
            .Where(n => n.TargetUserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new RevengeNotificationDto
            {
                Id = n.Id,
                AvengerUserId = n.AvengerUserId,
                AvengerUsername = n.AvengerUser.Username,
                AvengerDisplayName = n.AvengerUser.Profile != null ? n.AvengerUser.Profile.DisplayName : null,
                PuzzleId = n.PuzzleId,
                Rating = n.Puzzle.Rating,
                Solved = n.Solved,
                CreatedAt = n.CreatedAt,
                Seen = n.SeenAt != null
            })
            .ToListAsync();
    }

    /// <summary>Anzahl ungelesener Revanche-Benachrichtigungen — fürs Navbar-Badge.</summary>
    public async Task<int> GetUnseenCountAsync(int userId)
        => await _db.RevengeNotifications.CountAsync(n => n.TargetUserId == userId && n.SeenAt == null);

    /// <summary>Markiert alle ungelesenen Benachrichtigungen eines Users als gesehen.</summary>
    public async Task MarkAllSeenAsync(int userId)
    {
        var unseen = await _db.RevengeNotifications
            .Where(n => n.TargetUserId == userId && n.SeenAt == null)
            .ToListAsync();
        if (unseen.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var n in unseen) n.SeenAt = now;
        await _db.SaveChangesAsync();
    }
}
