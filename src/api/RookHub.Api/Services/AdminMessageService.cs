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

    /// <summary>Stellt sicher, dass die Thread-Metazeile existiert (für Zuweisung/Claim) und gibt sie
    /// zurück. Legt die Zeile sofort in EINEM eigenen SaveChanges an, damit der spätere Nachrichten-Insert
    /// des Aufrufers nicht mit einer parallel angelegten Thread-Zeile kollidiert.
    /// Schreiben Admin und User die ERSTE Nachricht gleichzeitig, würden sonst beide dieselbe PK
    /// (<see cref="MessageThread.UserId"/>) einfügen → der zweite SaveChanges wirft. Hier wird der
    /// PK-Konflikt abgefangen, die eigene (nicht persistierte) Add-Entry verworfen und die inzwischen
    /// vom anderen Request angelegte Zeile nachgeladen.</summary>
    private async Task<MessageThread> EnsureThreadAsync(int userId)
    {
        var thread = await _db.MessageThreads.FindAsync(userId);
        if (thread is not null) return thread;

        thread = new MessageThread { UserId = userId };
        _db.MessageThreads.Add(thread);
        try
        {
            await _db.SaveChangesAsync();
            return thread;
        }
        catch (DbUpdateException)
        {
            // Race: ein paralleler Request hat die Thread-Zeile gerade angelegt.
            _db.Entry(thread).State = EntityState.Detached;
            var existing = await _db.MessageThreads.FindAsync(userId);
            if (existing is null) throw;   // anderer Fehler als der erwartete PK-Konflikt
            return existing;
        }
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
        // Aggregat je Thread per GROUP BY auf DB-Ebene — das Ergebnis ist durch die ANZAHL DER THREADS
        // beschränkt, nicht durch die Gesamt-Nachrichtenzahl. Lädt also nicht mehr alle (bis zu 4000 Zeichen
        // langen) Bodies aller User in den Speicher. (Aggregat-GroupBy übersetzt EF sowohl gegen MariaDB als
        // auch InMemory korrekt.)
        var agg = await _db.AdminMessages
            .GroupBy(m => m.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                LastAt = g.Max(m => m.CreatedAt),
                // Eindeutige PK der jüngsten Nachricht je Thread — der Nachlade-Join MUSS darüber gehen,
                // nicht über CreatedAt: zeitgleiche Nachrichten aus FREMDEN Threads würden sonst mit-matchen
                // (falsche Vorschau) und der CreatedAt-IN-Filter ist nicht indexnutzbar.
                LastId = g.Max(m => m.Id),
                Unread = g.Count(m => !m.FromAdmin && m.SeenByAdminAt == null),
            })
            .ToListAsync();
        if (agg.Count == 0) return new List<AdminThreadSummaryDto>();

        // Nur die jeweils JÜNGSTE Nachricht je Thread nachladen (genau eine Zeile pro Thread, PK-Lookup).
        var lastIds = agg.Select(a => a.LastId).ToList();
        var lastMsgs = await _db.AdminMessages
            .Where(m => lastIds.Contains(m.Id))
            .Select(m => new { m.UserId, m.Body, m.CreatedAt, m.FromAdmin, Username = m.User.Username })
            .ToListAsync();
        var lastByUser = lastMsgs.ToDictionary(m => m.UserId, m => m);

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

        return agg
            .OrderByDescending(a => a.LastAt)
            .Select(a =>
            {
                lastByUser.TryGetValue(a.UserId, out var last);
                claimByUser.TryGetValue(a.UserId, out var claimedBy);
                var body = last?.Body ?? "";
                return new AdminThreadSummaryDto(
                    a.UserId,
                    last?.Username ?? "?",
                    body.Length > 80 ? body[..80] : body,
                    a.LastAt,
                    last?.FromAdmin ?? false,
                    a.Unread,
                    claimedBy,
                    claimedBy is int cb && adminNames.TryGetValue(cb, out var n) ? n : null);
            })
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
        // Deep-Link: öffnet im Admin-Bereich direkt den Nachrichten-Tab + diese Konversation.
        var link = $"/admin?tab=messages&thread={userId}";
        // Alle Admins in EINEM SaveChanges benachrichtigen (atomar, statt N Einzel-Saves mit
        // Teil-Benachrichtigungs-Risiko, falls einer mittendrin fehlschlägt).
        await _notifications.CreateManyAsync(adminIds, NotificationType.UserMessageReceived,
            new Dictionary<string, string> { ["username"] = senderName }, link);

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
