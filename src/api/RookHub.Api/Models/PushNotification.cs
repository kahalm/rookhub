using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Eine Web-Push-Subscription eines Users (Browser/Gerät). Ein User kann mehrere haben
/// (mehrere Browser/Geräte). Wird beim Aktivieren von Push angelegt und beim Abbestellen bzw.
/// bei „gone" (HTTP 404/410 vom Push-Dienst) wieder entfernt.
/// </summary>
public class UserPushSubscription
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Push-Endpoint-URL des Browsers (eindeutig; identifiziert die Subscription).</summary>
    [Required, MaxLength(500)]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Client-Public-Key (base64url) für die Payload-Verschlüsselung (ECDH P-256).</summary>
    [Required, MaxLength(200)]
    public string P256dh { get; set; } = string.Empty;

    /// <summary>Auth-Secret (base64url) der Subscription.</summary>
    [Required, MaxLength(100)]
    public string Auth { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Push-Einstellungen eines Users: welche Benachrichtigungs-Bereiche als Web-Push zugestellt werden
/// sollen (CSV der Kategorie-Keys courses/friends/puzzles/messages/tournaments/admin/other). Eine
/// fehlende Zeile bzw. leere Liste = Push für ALLE Bereiche AUS (Default: Push deaktiviert). Der
/// Bereich „admin" darf nur von Admins aktiviert werden.
/// </summary>
public class NotificationPushSetting
{
    /// <summary>Primärschlüssel = UserId (genau eine Einstellungszeile je User).</summary>
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    [MaxLength(100)]
    public string? EnabledCategories { get; set; }

    public DateTime UpdatedAt { get; set; }
}
