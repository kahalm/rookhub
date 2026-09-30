using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IWebhookTaskQueue? _pushQueue;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public NotificationService(AppDbContext db, IWebhookTaskQueue? pushQueue = null)
    {
        _db = db;
        _pushQueue = pushQueue;
    }

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

        // Web-Push best-effort: außerhalb des Request-Pfads, mit eigenem DI-Scope (der Worker liefert
        // den ServiceProvider). Kein Push konfiguriert / Bereich beim User aus → No-op im Service.
        if (_pushQueue != null)
            foreach (var userId in ids)
            {
                var uid = userId;
                await _pushQueue.EnqueueAsync(async (sp, ct) =>
                {
                    var push = sp.GetRequiredService<PushNotificationService>();
                    await push.SendToUserAsync(uid, type, data, link, ct);
                });
            }
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
        (page, pageSize) = Paging.Normalize(page, pageSize);
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

    // ---- Namens-Schnappschuss des Auslösers + Retention (Codereview 2026-09-29, A9-003) ----
    //
    // Zwölf Auslöser legen den Benutzernamen des Auslösers als data.username beim EMPFÄNGER ab (Freundschaft,
    // Challenge, Teilen, Neuanmeldung an alle Admins …). Die Kontolöschung räumte nur die EIGENEN Benachrichtigungen
    // ab — in Glocke und Verlauf der anderen stand der alte Name für immer. Usernamen ändern sich nur bei der
    // Löschung (es gibt kein Umbenennen), der Name ist bis dahin also die Identität des Auslösers: die Löschung
    // ersetzt ihn in fremden Benachrichtigungen durch den anonymisierten Namen (ProfileService.EraseAsync), der
    // tägliche Lauf fängt den Altbestand und das Rennen mit einem gleichzeitig angelegten Eintrag.

    private const string UsernameKey = "username";

    /// <summary>Gelesene Benachrichtigungen verfallen nach dieser Frist (Entscheidung A9-003); ungelesene bleiben.</summary>
    public static readonly TimeSpan SeenRetention = TimeSpan.FromDays(180);

    /// <summary>Ersatzname für ein gelöschtes Konto, dessen Id nicht mehr bekannt ist (Altbestand vor dem Fix).</summary>
    public const string DeletedActorName = "deleted";

    /// <summary>Benachrichtigungen ANDERER Nutzer, deren <c>data.username</c> genau <paramref name="username"/> ist.
    /// Vorauswahl per Teilstring in SQL (Kollation ggf. ohne Groß/klein), exakt geprüft wird danach.</summary>
    public static async Task<List<Notification>> MentioningUsernameAsync(AppDbContext db, string username, int exceptUserId,
        CancellationToken ct = default)
    {
        // Dieselben Optionen wie beim Anlegen → dieselbe Maskierung (Umlaute, Anführungszeichen) im Suchtext.
        var needle = $"\"{UsernameKey}\":" + JsonSerializer.Serialize(username, JsonOpts);
        var candidates = await db.Notifications
            .Where(n => n.UserId != exceptUserId && n.DataJson != null && n.DataJson.Contains(needle))
            .ToListAsync(ct);
        return candidates.Where(n => ReadUsername(n.DataJson) == username).ToList();
    }

    /// <summary>Setzt <c>data.username</c> einer Benachrichtigung (übrige Parameter bleiben).</summary>
    public static void SetUsername(Notification n, string username)
    {
        var data = ReadData(n.DataJson) ?? new Dictionary<string, string>();
        data[UsernameKey] = username;
        n.DataJson = JsonSerializer.Serialize(data, JsonOpts);
    }

    /// <summary>Täglicher Lauf (<see cref="NotificationRetentionScheduler"/>): gelesene Benachrichtigungen älter als
    /// <see cref="SeenRetention"/> löschen und Namen, zu denen es kein Konto mehr gibt (gelöscht vor diesem Fix, oder
    /// ein Eintrag, der während der Löschung noch entstand), durch <see cref="DeletedActorName"/> ersetzen.</summary>
    public async Task<(int Purged, int Anonymized)> RunRetentionAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var cutoff = nowUtc - SeenRetention;
        var expired = _db.Notifications.Where(n => n.SeenAt != null && n.CreatedAt < cutoff);
        int purged;
        if (_db.Database.IsRelational())
            purged = await expired.ExecuteDeleteAsync(ct);
        else
        {
            var rows = await expired.ToListAsync(ct);   // InMemory (Tests) kennt kein ExecuteDelete
            _db.Notifications.RemoveRange(rows);
            await _db.SaveChangesAsync(ct);
            purged = rows.Count;
        }

        var keyNeedle = $"\"{UsernameKey}\":";
        var named = (await _db.Notifications
                .Where(n => n.DataJson != null && n.DataJson.Contains(keyNeedle))
                .Select(n => new { n.Id, n.DataJson })
                .ToListAsync(ct))
            .Select(r => (r.Id, Name: ReadUsername(r.DataJson)))
            .Where(r => !string.IsNullOrEmpty(r.Name) && r.Name != DeletedActorName)
            .ToList();
        if (named.Count == 0) return (purged, 0);

        // Groß/klein egal: der Unique-Index auf Username vergleicht in der Kollation ebenso.
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in named.Select(r => r.Name!).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(500))
            existing.UnionWith(await _db.AppUsers.Where(u => chunk.Contains(u.Username)).Select(u => u.Username).ToListAsync(ct));

        var anonymized = 0;
        foreach (var chunk in named.Where(r => !existing.Contains(r.Name!)).Select(r => r.Id).Chunk(500))
        {
            foreach (var n in await _db.Notifications.Where(n => chunk.Contains(n.Id)).ToListAsync(ct))
            {
                SetUsername(n, DeletedActorName);
                anonymized++;
            }
            await _db.SaveChangesAsync(ct);
        }
        return (purged, anonymized);
    }

    private static Dictionary<string, string>? ReadData(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts); }
        catch (JsonException) { return null; }
    }

    private static string? ReadUsername(string? json)
        => ReadData(json) is { } data && data.TryGetValue(UsernameKey, out var name) ? name : null;

    private static NotificationDto ToDto(Notification n) => new(
        n.Id,
        n.Type,
        n.DataJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(n.DataJson, JsonOpts),
        n.Link,
        n.CreatedAt,
        n.SeenAt != null);
}
