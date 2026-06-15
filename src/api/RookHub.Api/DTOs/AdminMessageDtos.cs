namespace RookHub.Api.DTOs;

/// <summary>Eine Nachricht im Admin↔User-Thread fürs Frontend. <see cref="ReadByRecipient"/> ist die
/// Lesebestätigung der jeweils anderen Seite (Admin-Nachricht: vom User gelesen; User-Antwort: vom Admin gelesen).</summary>
public record AdminMessageDto(
    int Id,
    bool FromAdmin,
    string Body,
    DateTime CreatedAt,
    bool ReadByRecipient);

/// <summary>Eingehender Nachrichtentext (Admin-Senden oder User-Antwort).</summary>
public record SendMessageDto(string Body);

/// <summary>Admin-Übersicht eines Threads: ein Eintrag je User mit Konversation.</summary>
public record AdminThreadSummaryDto(
    int UserId,
    string Username,
    string LastMessagePreview,
    DateTime LastMessageAt,
    bool LastFromAdmin,
    int UnreadFromUser);

/// <summary>Ungelesen-Zähler (Admin-Tab-Badge).</summary>
public record MessageUnreadCountDto(int Count);

/// <summary>Nachrichten-Status des Users für die Navbar: Ungelesen-Anzahl + ob überhaupt eine
/// Konversation existiert (steuert das Einblenden des Mail-Icons).</summary>
public record UserMessageStatusDto(int Unread, bool HasMessages);
