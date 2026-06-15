using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Admin↔User-Direktnachrichten. Der Admin startet einen Thread mit einem User; danach können beide
/// Seiten beliebig oft antworten (durchgehende Konversation). Jede neue Nachricht legt eine In-App-
/// Benachrichtigung bei der Gegenseite an (User-Glocke bzw. alle Admins). Thread = alle Nachrichten
/// mit derselben <see cref="AdminMessage.UserId"/>.
/// </summary>
public class AdminMessageService
{
    private readonly AppDbContext _db;
    private readonly NotificationService _notifications;

    public AdminMessageService(AppDbContext db, NotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    /// <summary>Max. Länge einer Nachricht (überzähliges wird abgeschnitten).</summary>
    public const int MaxBodyLength = 4000;

    private static string Normalize(string? body)
    {
        body = (body ?? string.Empty).Trim();
        if (body.Length == 0) throw new InvalidOperationException("Message body must not be empty.");
        return body.Length > MaxBodyLength ? body[..MaxBodyLength] : body;
    }

    // ---- Admin-Seite ----

    /// <summary>Admin schickt/antwortet einem User (legt den Thread an, falls erste Nachricht).</summary>
    public async Task<AdminMessageDto> SendFromAdminAsync(int adminId, int targetUserId, string? body)
    {
        var text = Normalize(body);
        var exists = await _db.AppUsers.AnyAsync(u => u.Id == targetUserId);
        if (!exists) throw new KeyNotFoundException("User not found.");

        var msg = new AdminMessage { UserId = targetUserId, SenderId = adminId, FromAdmin = true, Body = text };
        _db.AdminMessages.Add(msg);
        await _db.SaveChangesAsync();

        // Glocke beim Empfänger (Link auf die Nachrichten-Seite).
        await _notifications.CreateAsync(targetUserId, NotificationType.AdminMessageReceived, null, "/messages");
        return ToDto(msg);
    }

    /// <summary>Alle Threads (ein Eintrag je User) mit letzter Nachricht + Anzahl ungelesener User-Antworten.</summary>
    public async Task<List<AdminThreadSummaryDto>> GetThreadsAsync()
    {
        // Bewusst alle Nachrichten laden und in-memory gruppieren: robust gegen EF-GroupBy-Übersetzung
        // (InMemory-Tests + MariaDB) und das Volumen ist moderat (Admin-Support-Konversationen).
        var rows = await _db.AdminMessages
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new { m.UserId, m.Body, m.CreatedAt, m.FromAdmin, m.SeenByAdminAt, Username = m.User.Username })
            .ToListAsync();

        return rows
            .GroupBy(m => m.UserId)
            .Select(g =>
            {
                var last = g.First();   // bereits absteigend sortiert ⇒ neueste
                return new AdminThreadSummaryDto(
                    g.Key,
                    last.Username,
                    last.Body.Length > 80 ? last.Body[..80] : last.Body,
                    last.CreatedAt,
                    last.FromAdmin,
                    g.Count(m => !m.FromAdmin && m.SeenByAdminAt == null));
            })
            .OrderByDescending(t => t.LastMessageAt)
            .ToList();
    }

    /// <summary>Vollständiger Thread mit einem User (chronologisch, älteste zuerst).</summary>
    public async Task<List<AdminMessageDto>> GetThreadAsync(int targetUserId)
    {
        var list = await _db.AdminMessages
            .Where(m => m.UserId == targetUserId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
        return list.Select(ToDto).ToList();
    }

    /// <summary>Markiert die User-Antworten eines Threads als vom Admin gelesen.</summary>
    public async Task MarkSeenByAdminAsync(int targetUserId)
    {
        var unseen = await _db.AdminMessages
            .Where(m => m.UserId == targetUserId && !m.FromAdmin && m.SeenByAdminAt == null)
            .ToListAsync();
        if (unseen.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var m in unseen) m.SeenByAdminAt = now;
        await _db.SaveChangesAsync();
    }

    /// <summary>Anzahl ungelesener User-Antworten über alle Threads (Admin-Tab-Badge).</summary>
    public async Task<int> CountUnreadForAdminAsync()
        => await _db.AdminMessages.CountAsync(m => !m.FromAdmin && m.SeenByAdminAt == null);

    // ---- User-Seite ----

    /// <summary>Eigener Thread des Users (chronologisch). Leer, wenn der Admin nie geschrieben hat.</summary>
    public async Task<List<AdminMessageDto>> GetUserThreadAsync(int userId)
        => await GetThreadAsync(userId);

    /// <summary>User antwortet im eigenen Thread. Nur erlaubt, wenn der Admin den Thread gestartet hat.</summary>
    public async Task<AdminMessageDto> ReplyFromUserAsync(int userId, string? body)
    {
        var text = Normalize(body);
        var hasThread = await _db.AdminMessages.AnyAsync(m => m.UserId == userId);
        if (!hasThread) throw new InvalidOperationException("No conversation to reply to.");

        var msg = new AdminMessage { UserId = userId, SenderId = userId, FromAdmin = false, Body = text };
        _db.AdminMessages.Add(msg);
        await _db.SaveChangesAsync();

        // Glocke bei allen Admins (Link in den Admin-Bereich).
        var senderName = (await _db.AppUsers.FindAsync(userId))?.Username ?? "user";
        var adminIds = await _db.AppUsers.Where(u => u.IsAdmin).Select(u => u.Id).ToListAsync();
        foreach (var adminId in adminIds)
            await _notifications.CreateAsync(adminId, NotificationType.UserMessageReceived,
                new Dictionary<string, string> { ["username"] = senderName }, "/admin");

        return ToDto(msg);
    }

    /// <summary>Markiert die Admin-Nachrichten im eigenen Thread als vom User gelesen.</summary>
    public async Task MarkSeenByUserAsync(int userId)
    {
        var unseen = await _db.AdminMessages
            .Where(m => m.UserId == userId && m.FromAdmin && m.SeenByUserAt == null)
            .ToListAsync();
        if (unseen.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var m in unseen) m.SeenByUserAt = now;
        await _db.SaveChangesAsync();
    }

    /// <summary>Anzahl ungelesener Admin-Nachrichten des Users (Navbar-Badge auf „Nachrichten").</summary>
    public async Task<int> CountUnreadForUserAsync(int userId)
        => await _db.AdminMessages.CountAsync(m => m.UserId == userId && m.FromAdmin && m.SeenByUserAt == null);

    /// <summary>Hat der User überhaupt eine Konversation (mind. eine Nachricht bekommen/geschrieben)?
    /// Steuert, ob das Navbar-Nachrichten-Icon eingeblendet wird.</summary>
    public async Task<bool> HasThreadForUserAsync(int userId)
        => await _db.AdminMessages.AnyAsync(m => m.UserId == userId);

    private static AdminMessageDto ToDto(AdminMessage m) => new(
        m.Id,
        m.FromAdmin,
        m.Body,
        m.CreatedAt,
        m.FromAdmin ? m.SeenByUserAt != null : m.SeenByAdminAt != null);
}
