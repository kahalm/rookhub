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
    int UnreadFromUser,
    int? ClaimedByAdminId,
    string? ClaimedByAdminName);

/// <summary>Ungelesen-Zähler (User-Nachrichten-Badge bzw. Admin-Tab-Badge).</summary>
public record MessageUnreadCountDto(int Count);
