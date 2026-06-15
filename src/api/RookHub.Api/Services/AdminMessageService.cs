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

    /// <summary>Stellt sicher, dass die Thread-Metazeile existiert (für Zuweisung/Claim).</summary>
    private async Task<MessageThread> EnsureThreadAsync(int userId)
    {
        var thread = await _db.MessageThreads.FindAsync(userId);
        if (thread is null)
        {
            thread = new MessageThread { UserId = userId };
            _db.MessageThreads.Add(thread);
        }
        return thread;
    }

    // ---- Admin-Seite ----

    /// <summary>Admin schickt/antwortet einem User (legt den Thread an, falls erste Nachricht). Ein
    /// unbearbeiteter Thread wird dabei automatisch von diesem Admin übernommen.</summary>
    public async Task<AdminMessageDto> SendFromAdminAsync(int adminId, int targetUserId, string? body)
    {
        var text = Normalize(body);
        var exists = await _db.AppUsers.AnyAsync(u => u.Id == targetUserId);
        if (!exists) throw new KeyNotFoundException("User not found.");

        var thread = await EnsureThreadAsync(targetUserId);
        // Antworten = übernehmen: noch nicht zugewiesener Thread geht an diesen Admin.
        if (thread.ClaimedByAdminId is null)
        {
            thread.ClaimedByAdminId = adminId;
            thread.ClaimedAt = DateTime.UtcNow;
        }

        var msg = new AdminMessage { UserId = targetUserId, SenderId = adminId, FromAdmin = true, Body = text };
        _db.AdminMessages.Add(msg);
        await _db.SaveChangesAsync();

        // Glocke beim Empfänger (Link auf die Nachrichten-Seite).
        await _notifications.CreateAsync(targetUserId, NotificationType.AdminMessageReceived, null, "/messages");
        return ToDto(msg);
    }

    /// <summary>Admin übernimmt einen Thread (Zuweisung an sich). Legt die Thread-Zeile bei Bedarf an.</summary>
    public async Task ClaimThreadAsync(int adminId, int targetUserId)
    {
        var thread = await EnsureThreadAsync(targetUserId);
        thread.ClaimedByAdminId = adminId;
        thread.ClaimedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    /// <summary>Gibt einen Thread wieder frei (keine Zuweisung mehr).</summary>
    public async Task ReleaseThreadAsync(int targetUserId)
    {
        var thread = await _db.MessageThreads.FindAsync(targetUserId);
        if (thread is null) return;
        thread.ClaimedByAdminId = null;
        thread.ClaimedAt = null;
        await _db.SaveChangesAsync();
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

        // Zuweisung (Claim) je Thread + Admin-Namen für die Anzeige auflösen.
        var claims = await _db.MessageThreads
            .Where(t => t.ClaimedByAdminId != null)
            .Select(t => new { t.UserId, t.ClaimedByAdminId })
            .ToListAsync();
        var claimByUser = claims.ToDictionary(c => c.UserId, c => c.ClaimedByAdminId);
        var adminIds = claims.Select(c => c.ClaimedByAdminId!.Value).Distinct().ToList();
        var adminNames = await _db.AppUsers
            .Where(u => adminIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username);

        return rows
            .GroupBy(m => m.UserId)
            .Select(g =>
            {
                var last = g.First();   // bereits absteigend sortiert ⇒ neueste
                claimByUser.TryGetValue(g.Key, out var claimedBy);
                return new AdminThreadSummaryDto(
                    g.Key,
                    last.Username,
                    last.Body.Length > 80 ? last.Body[..80] : last.Body,
                    last.CreatedAt,
                    last.FromAdmin,
                    g.Count(m => !m.FromAdmin && m.SeenByAdminAt == null),
                    claimedBy,
                    claimedBy is int cb && adminNames.TryGetValue(cb, out var n) ? n : null);
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

    /// <summary>User schreibt dem Admin-Team — startet die Konversation selbst oder antwortet im
    /// bestehenden Thread. Alle Admins werden benachrichtigt; ein Admin kann den Thread übernehmen.</summary>
    public async Task<AdminMessageDto> SendFromUserAsync(int userId, string? body)
    {
        var text = Normalize(body);
        await EnsureThreadAsync(userId);

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

    private static AdminMessageDto ToDto(AdminMessage m) => new(
        m.Id,
        m.FromAdmin,
        m.Body,
        m.CreatedAt,
        m.FromAdmin ? m.SeenByUserAt != null : m.SeenByAdminAt != null);
}
