using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using WebPush;

namespace RookHub.Api.Services;

/// <summary>VAPID-Konfiguration für Web-Push (aus dem Abschnitt „WebPush" der Config/ENV).</summary>
public sealed class WebPushOptions
{
    /// <summary>Kontakt-Subject (mailto: oder https-URL) für den Push-Dienst.</summary>
    public string? Subject { get; set; }
    /// <summary>VAPID Public Key (base64url) — auch dem Client bekannt.</summary>
    public string? PublicKey { get; set; }
    /// <summary>VAPID Private Key (base64url) — geheim.</summary>
    public string? PrivateKey { get; set; }
}

/// <summary>Versendet eine Web-Push-Nachricht; abstrahiert die WebPush-Lib (für Tests ersetzbar).
/// Wirft <see cref="WebPushException"/> mit StatusCode (404/410 = Subscription „gone").</summary>
public interface IWebPushSender
{
    Task SendAsync(UserPushSubscription sub, string payloadJson, WebPushOptions opts, CancellationToken ct = default);
}

/// <summary>Echte Implementierung mit <see cref="WebPushClient"/>.</summary>
public sealed class WebPushSender : IWebPushSender
{
    private readonly WebPushClient _client = new();

    public Task SendAsync(UserPushSubscription sub, string payloadJson, WebPushOptions opts, CancellationToken ct = default)
    {
        var subscription = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
        var vapid = new VapidDetails(opts.Subject, opts.PublicKey, opts.PrivateKey);
        return _client.SendNotificationAsync(subscription, payloadJson, vapid, ct);
    }
}

/// <summary>
/// Web-Push für den Benachrichtigungs-Strom. Verwaltet je User die Push-Subscriptions (Browser/Geräte)
/// und die aktivierten Bereiche (Kategorien) und stellt beim Anlegen einer Benachrichtigung — sofern
/// VAPID konfiguriert ist, der User Subscriptions hat UND den Bereich aktiviert hat — einen Web-Push zu.
/// Standardmäßig ist Push AUS (keine aktivierten Bereiche). Der Bereich „admin" ist Admins vorbehalten.
/// </summary>
public class PushNotificationService
{
    private readonly AppDbContext _db;
    private readonly IWebPushSender _sender;
    private readonly WebPushOptions _opts;
    private readonly ILogger<PushNotificationService> _log;

    public PushNotificationService(AppDbContext db, IWebPushSender sender,
        Microsoft.Extensions.Options.IOptions<WebPushOptions> opts, ILogger<PushNotificationService> log)
    {
        _db = db; _sender = sender; _opts = opts.Value; _log = log;
    }

    /// <summary>Alle Bereiche in fester Reihenfolge (identisch zu den Frontend-Notification-Kategorien).</summary>
    public static readonly string[] AllCategories =
        { "courses", "friends", "puzzles", "messages", "tournaments", "admin", "other" };

    /// <summary>Ist Web-Push serverseitig einsatzbereit (VAPID-Schlüssel gesetzt)?</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_opts.Subject) &&
        !string.IsNullOrWhiteSpace(_opts.PublicKey) &&
        !string.IsNullOrWhiteSpace(_opts.PrivateKey);

    /// <summary>VAPID-Public-Key für den Client (null, wenn nicht konfiguriert).</summary>
    public string? PublicKey => IsConfigured ? _opts.PublicKey : null;

    /// <summary>Bereich (Kategorie) eines Benachrichtigungs-Typs — spiegelt das Frontend
    /// (<c>core/notification-text.ts</c> <c>notificationCategory</c>).</summary>
    public static string CategoryOf(string type) => type switch
    {
        "chessable_import_completed" or "chessable_import_failed" or "chessable_new_course"
            or "chessable_token_added" or "course_shared" or "repertoire_shared" => "courses",
        "friend_request_received" or "friend_request_accepted" => "friends",
        "challenge_received" or "challenge_resolved" or "revenge_performed" => "puzzles",
        "admin_message_received" or "user_message_received" => "messages",
        "tournament_new_round" => "tournaments",
        "new_user_registered" => "admin",
        _ => "other",
    };

    // ----- Einstellungen (aktivierte Bereiche) -----------------------------

    public async Task<List<string>> GetEnabledCategoriesAsync(int userId)
    {
        var csv = await _db.NotificationPushSettings.AsNoTracking()
            .Where(s => s.UserId == userId).Select(s => s.EnabledCategories).FirstOrDefaultAsync();
        return ParseCategories(csv);
    }

    /// <summary>Setzt die aktivierten Push-Bereiche eines Users. Ungültiger Key → <see cref="InvalidOperationException"/>
    /// (→400). „admin" wird für Nicht-Admins verworfen (Bereich nur für Admins). Leere Liste = Push aus.
    /// Gibt die effektiv gespeicherten Keys zurück.</summary>
    public async Task<List<string>> SetEnabledCategoriesAsync(int userId, IEnumerable<string> categories, bool isAdmin)
    {
        var normalized = new List<string>();
        foreach (var raw in categories ?? Enumerable.Empty<string>())
        {
            var key = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (key.Length == 0) continue;
            if (Array.IndexOf(AllCategories, key) < 0)
                throw new InvalidOperationException($"Unknown notification category '{raw}'.");
            if (key == "admin" && !isAdmin) continue;   // Admin-Bereich nur für Admins
            if (!normalized.Contains(key)) normalized.Add(key);
        }
        var setting = await _db.NotificationPushSettings.FirstOrDefaultAsync(s => s.UserId == userId);
        var csv = normalized.Count == 0 ? null : string.Join(",", normalized);
        if (setting == null)
            _db.NotificationPushSettings.Add(new NotificationPushSetting { UserId = userId, EnabledCategories = csv, UpdatedAt = DateTime.UtcNow });
        else { setting.EnabledCategories = csv; setting.UpdatedAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync();
        return normalized;
    }

    // ----- Subscriptions ---------------------------------------------------

    /// <summary>Legt eine Subscription an bzw. ordnet sie (per eindeutigem Endpoint) diesem User zu (Upsert).</summary>
    public async Task SubscribeAsync(int userId, string endpoint, string p256dh, string auth)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(p256dh) || string.IsNullOrWhiteSpace(auth))
            throw new InvalidOperationException("Incomplete push subscription.");
        var existing = await _db.UserPushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint);
        if (existing == null)
            _db.UserPushSubscriptions.Add(new UserPushSubscription
            {
                UserId = userId, Endpoint = endpoint, P256dh = p256dh, Auth = auth, CreatedAt = DateTime.UtcNow,
            });
        else { existing.UserId = userId; existing.P256dh = p256dh; existing.Auth = auth; }
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { /* Race auf dem Unique-Endpoint → ignorieren. */ }
    }

    /// <summary>Entfernt eine Subscription des Users (idempotent).</summary>
    public async Task UnsubscribeAsync(int userId, string endpoint)
    {
        var sub = await _db.UserPushSubscriptions.FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == endpoint);
        if (sub == null) return;
        _db.UserPushSubscriptions.Remove(sub);
        await _db.SaveChangesAsync();
    }

    // ----- Versand ---------------------------------------------------------

    /// <summary>Stellt eine Benachrichtigung als Web-Push zu, sofern konfiguriert, der Bereich beim User
    /// aktiviert ist und Subscriptions existieren. „Gone" (404/410) räumt die tote Subscription ab.
    /// Best-effort: Fehler werden nur geloggt.</summary>
    public async Task SendToUserAsync(int userId, string type, IReadOnlyDictionary<string, string>? data, string? link)
    {
        if (!IsConfigured) return;
        var category = CategoryOf(type);
        var enabled = await GetEnabledCategoriesAsync(userId);
        if (!enabled.Contains(category)) return;

        var subs = await _db.UserPushSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        if (subs.Count == 0) return;

        var payload = BuildPayload(category, string.IsNullOrWhiteSpace(link) ? "/notifications" : link!);
        var gone = new List<UserPushSubscription>();
        foreach (var sub in subs)
        {
            try { await _sender.SendAsync(sub, payload, _opts); }
            catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                gone.Add(sub);   // Subscription abgelaufen/abgemeldet → aufräumen
            }
            catch (Exception ex) { _log.LogWarning(ex, "Web-Push an User {UserId} fehlgeschlagen.", userId); }
        }
        if (gone.Count > 0)
        {
            _db.UserPushSubscriptions.RemoveRange(gone);
            try { await _db.SaveChangesAsync(); } catch { /* best-effort */ }
        }
    }

    /// <summary>ngsw-kompatibles Push-Payload: zeigt Titel/Text automatisch an und navigiert beim Klick
    /// (auch bei geschlossener App via <c>onActionClick.default</c>).</summary>
    private static string BuildPayload(string category, string url)
    {
        var (title, body) = PushText(category);
        var obj = new
        {
            notification = new
            {
                title,
                body,
                icon = "/icons/icon-192.png",
                data = new
                {
                    url,
                    onActionClick = new { @default = new { operation = "navigateLastFocusedOrOpen", url } },
                },
            },
        };
        return JsonSerializer.Serialize(obj);
    }

    /// <summary>Kurzer Push-Text je Bereich (deutsch; die Detail-Ansicht liefert die Glocke). Serverseitig
    /// bewusst pro Bereich statt pro Typ, um die Client-i18n nicht zu duplizieren.</summary>
    private static (string Title, string Body) PushText(string category) => category switch
    {
        "courses" => ("Kurse", "Es gibt Neues zu deinen Kursen."),
        "friends" => ("Freunde", "Neue Freundes-Aktivität."),
        "puzzles" => ("Puzzles", "Neue Puzzle-Aktivität."),
        "messages" => ("Nachrichten", "Du hast eine neue Nachricht."),
        "tournaments" => ("Turniere", "Es gibt ein Turnier-Update."),
        "admin" => ("Admin", "Neues Admin-Ereignis."),
        _ => ("RookHub", "Neue Benachrichtigung."),
    };

    private static List<string> ParseCategories(string? csv)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(csv))
            foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var key = raw.ToLowerInvariant();
                if (Array.IndexOf(AllCategories, key) >= 0 && !list.Contains(key)) list.Add(key);
            }
        return list;
    }
}
