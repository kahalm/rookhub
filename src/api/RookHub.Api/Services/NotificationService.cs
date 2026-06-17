using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Generischer In-App-Benachrichtigungs-Strom: legt Benachrichtigungen an (von den jeweiligen
/// Domänen-Services per fire-and-forget aufgerufen) und liefert Liste/Zähler/„als gesehen" für
/// die Navbar-Glocke. Bewusst schlank — spätere Kanäle (Mail/Push) docken hier an.
/// </summary>
public class NotificationService
{
    private readonly AppDbContext _db;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public NotificationService(AppDbContext db) => _db = db;

    /// <summary>Legt eine Benachrichtigung für <paramref name="userId"/> an.</summary>
    public Task CreateAsync(int userId, string type,
        IReadOnlyDictionary<string, string>? data = null, string? link = null)
        => CreateManyAsync(new[] { userId }, type, data, link);

    /// <summary>Legt dieselbe Benachrichtigung für mehrere Empfänger in EINEM SaveChanges an.
    /// Atomar (alle oder keiner) — verhindert Teil-Benachrichtigungen, wenn z. B. der User→Admin-Strom
    /// alle Admins informiert, und spart die N Einzel-Roundtrips eines Schleifen-CreateAsync.</summary>
    public async Task CreateManyAsync(IEnumerable<int> userIds, string type,
        IReadOnlyDictionary<string, string>? data = null, string? link = null)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var json = data is { Count: > 0 } ? JsonSerializer.Serialize(data, JsonOpts) : null;
        foreach (var userId in ids)
            _db.Notifications.Add(new Notification { UserId = userId, Type = type, DataJson = json, Link = link });
        await _db.SaveChangesAsync();
    }

    /// <summary>Letzte Benachrichtigungen eines Users (neueste zuerst).</summary>
    public async Task<List<NotificationDto>> GetForUserAsync(int userId, int take = 20, bool unseenOnly = false)
    {
        take = Math.Clamp(take, 1, 100);
        var q = _db.Notifications.Where(n => n.UserId == userId);
        if (unseenOnly) q = q.Where(n => n.SeenAt == null);
        var list = await q
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync();
        return list.Select(ToDto).ToList();
    }

    /// <summary>Eine Seite der vollständigen History eines Users (neueste zuerst) + Gesamtzahl.</summary>
    public async Task<NotificationHistoryDto> GetHistoryAsync(int userId, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = _db.Notifications.Where(n => n.UserId == userId);
        var total = await q.CountAsync();
        var list = await q
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return new NotificationHistoryDto(list.Select(ToDto).ToList(), total);
    }

    /// <summary>Anzahl ungelesener Benachrichtigungen — für das Glocken-Badge.</summary>
    public async Task<int> CountUnseenAsync(int userId)
        => await _db.Notifications.CountAsync(n => n.UserId == userId && n.SeenAt == null);

    /// <summary>Markiert eine einzelne Benachrichtigung als gesehen (Klick darauf). No-op, wenn sie
    /// nicht dem User gehört oder bereits gesehen ist.</summary>
    public async Task MarkSeenAsync(int userId, int id)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (n is null || n.SeenAt != null) return;
        n.SeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    /// <summary>Markiert alle ungelesenen Benachrichtigungen eines Users als gesehen (Glocke geöffnet).</summary>
    public async Task MarkAllSeenAsync(int userId)
    {
        var unseen = await _db.Notifications
            .Where(n => n.UserId == userId && n.SeenAt == null)
            .ToListAsync();
        if (unseen.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var n in unseen) n.SeenAt = now;
        await _db.SaveChangesAsync();
    }

    private static NotificationDto ToDto(Notification n) => new(
        n.Id,
        n.Type,
        n.DataJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(n.DataJson, JsonOpts),
        n.Link,
        n.CreatedAt,
        n.SeenAt != null);
}
