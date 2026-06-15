namespace RookHub.Api.Models;

/// <summary>
/// Generische In-App-Benachrichtigung für einen User (treibt die Navbar-Glocke + „!"-Indikator).
/// Bewusst typ-agnostisch: <see cref="Type"/> + <see cref="DataJson"/> (i18n-Parameter) werden im
/// Frontend zu lokalisiertem Text gerendert. <see cref="SeenAt"/> = null ⇒ ungelesen.
/// Spätere Kanäle (Mail/Push) hängen an genau diesem Strom.
/// </summary>
public class Notification
{
    public int Id { get; set; }

    /// <summary>Empfänger.</summary>
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>Typ-Schlüssel (siehe <see cref="NotificationType"/>) → bestimmt Icon + i18n-Text im Frontend.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Optionale i18n-Parameter als JSON (z. B. {"username":"…"} / {"courseName":"…"}).</summary>
    public string? DataJson { get; set; }

    /// <summary>Ziel-Route beim Klick auf die Benachrichtigung (z. B. "/friends", "/courses").</summary>
    public string? Link { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gesetzt, sobald der User die Glocke geöffnet hat. null ⇒ ungelesen (Badge zählt es).</summary>
    public DateTime? SeenAt { get; set; }
}

/// <summary>Bekannte Notification-Typen. Frontend mappt jeden auf Icon + i18n-Key "notifications.type.&lt;type&gt;".</summary>
public static class NotificationType
{
    public const string ChessableImportCompleted = "chessable_import_completed";
    public const string ChessableImportFailed = "chessable_import_failed";
    public const string FriendRequestReceived = "friend_request_received";
    public const string FriendRequestAccepted = "friend_request_accepted";
    public const string RevengePerformed = "revenge_performed";
    public const string ChallengeReceived = "challenge_received";
    public const string ChallengeResolved = "challenge_resolved";
    /// <summary>Admin hat dem User eine Direktnachricht geschickt (→ User-Glocke, Link „/messages").</summary>
    public const string AdminMessageReceived = "admin_message_received";
    /// <summary>User hat im Thread geantwortet (→ Glocke aller Admins, Link „/admin").</summary>
    public const string UserMessageReceived = "user_message_received";
}
