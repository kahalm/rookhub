using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Aktualisiert (i. d. R. nächtlich, vom <see cref="ChessableCourseRefreshScheduler"/> getrieben) die
/// gecachte Chessable-Kursliste ALLER hinterlegten Bearer. Pro Credential:
/// <list type="bullet">
///   <item>Bei offenem Circuit-Breaker (<see cref="ChessableCredential.BlockedAt"/>) wird der Bearer
///     übersprungen — ein bekannter toter Token wird nicht erneut angeklopft.</item>
///   <item>Weist Chessable den Bearer als endgültig unbrauchbar ab (Account gesperrt/gelöscht bzw.
///     Token tot, <see cref="ChessableBearerBreaker.IsBearerFatal"/>), wird der Breaker geöffnet
///     („blockierte Tokens sperren"). Ein reiner IP-/Cloudflare-Block sperrt NICHT.</item>
///   <item>Taucht gegenüber der bisher gecachten Liste ein NEUER Kurs auf, geht eine Benachrichtigung
///     an alle Admins. Beim allerersten Befüllen (vorher kein Cache) wird NICHT benachrichtigt
///     (das ist die Grundlinie, kein „neuer" Kurs).</item>
/// </list>
/// </summary>
public class ChessableCourseRefreshService
{
    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly ChessableProxyService _chessable;
    private readonly ChessableBearerBreaker _breaker;
    private readonly NotificationService _notifications;
    private readonly ILogger<ChessableCourseRefreshService> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ChessableCourseRefreshService(
        AppDbContext db,
        EncryptionService encryption,
        ChessableProxyService chessable,
        ChessableBearerBreaker breaker,
        NotificationService notifications,
        ILogger<ChessableCourseRefreshService> logger)
    {
        _db = db;
        _encryption = encryption;
        _chessable = chessable;
        _breaker = breaker;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>Aktualisiert die Kursliste aller Bearer. Liefert eine kleine Zusammenfassung (für Tests/Logs).</summary>
    public async Task<RefreshSummary> RefreshAllAsync(CancellationToken ct = default)
    {
        var creds = await _db.ChessableCredentials.ToListAsync(ct);
        var adminIds = await _db.AppUsers.Where(u => u.IsAdmin).Select(u => u.Id).ToListAsync(ct);

        var summary = new RefreshSummary { Total = creds.Count };
        foreach (var cred in creds)
        {
            if (ct.IsCancellationRequested) break;

            // Bekannter toter Token → nicht erneut anfragen.
            if (cred.BlockedAt is not null) { summary.SkippedBlocked++; continue; }

            var bearer = _encryption.TryDecrypt(cred.EncryptedBearer);
            if (bearer is null) { summary.SkippedUndecryptable++; continue; }

            List<ChessableCourseDto> courses;
            try
            {
                courses = await _chessable.GetCoursesAsync(bearer, ct);
            }
            catch (ChessableProxyException ex)
            {
                if (ChessableBearerBreaker.IsBearerFatal(ex.Message))
                {
                    await _breaker.TripAsync(cred.UserId, ex.Message, ct);
                    summary.Blocked++;
                }
                else
                {
                    summary.TransientErrors++;
                    _logger.LogWarning("Chessable-Kurslisten-Refresh für User {UserId} fehlgeschlagen (nicht-fatal): {Message}",
                        cred.UserId, ex.Message);
                }
                continue;
            }
            catch (Exception ex)
            {
                summary.TransientErrors++;
                _logger.LogWarning(ex, "Chessable-Kurslisten-Refresh für User {UserId} fehlgeschlagen", cred.UserId);
                continue;
            }

            // Neue Kurse gegenüber dem bisherigen Cache bestimmen (Baseline beim ersten Befüllen = keine Notif).
            var hadCache = !string.IsNullOrEmpty(cred.CachedCoursesJson);
            var oldBids = ParseBids(cred.CachedCoursesJson);
            var newCourses = hadCache
                ? courses.Where(c => !oldBids.Contains(c.Bid)).ToList()
                : new List<ChessableCourseDto>();

            cred.CachedCoursesJson = JsonSerializer.Serialize(courses, JsonOpts);
            cred.CoursesCachedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            summary.Refreshed++;

            if (newCourses.Count > 0 && adminIds.Count > 0)
            {
                var username = await _db.AppUsers.Where(u => u.Id == cred.UserId).Select(u => u.Username).FirstOrDefaultAsync(ct) ?? "?";
                foreach (var c in newCourses)
                {
                    await _notifications.CreateManyAsync(adminIds, NotificationType.ChessableNewCourse,
                        new Dictionary<string, string> { ["username"] = username, ["courseName"] = c.Name }, "/admin");
                }
                summary.NewCourses += newCourses.Count;
                _logger.LogInformation("Chessable-Refresh: {Count} neue(r) Kurs(e) bei User {UserId} — Admins benachrichtigt",
                    newCourses.Count, cred.UserId);
            }
        }

        _logger.LogInformation(
            "Chessable-Kurslisten-Refresh fertig: {Refreshed}/{Total} aktualisiert, {Blocked} gesperrt, {NewCourses} neue Kurse, {Transient} transiente Fehler",
            summary.Refreshed, summary.Total, summary.Blocked, summary.NewCourses, summary.TransientErrors);
        return summary;
    }

    private static HashSet<string> ParseBids(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new HashSet<string>();
        try
        {
            var list = JsonSerializer.Deserialize<List<ChessableCourseDto>>(json, JsonOpts);
            return list is null ? new HashSet<string>() : list.Select(c => c.Bid).ToHashSet();
        }
        catch (JsonException) { return new HashSet<string>(); }
    }

    /// <summary>Kleine Ergebnis-Zusammenfassung eines Refresh-Laufs (Logging/Tests).</summary>
    public class RefreshSummary
    {
        public int Total { get; set; }
        public int Refreshed { get; set; }
        public int SkippedBlocked { get; set; }
        public int SkippedUndecryptable { get; set; }
        public int Blocked { get; set; }
        public int NewCourses { get; set; }
        public int TransientErrors { get; set; }
    }
}
