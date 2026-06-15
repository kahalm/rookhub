namespace RookHub.Api.DTOs;

/// <summary>Eine In-App-Benachrichtigung fürs Frontend. <see cref="Data"/> liefert die i18n-Parameter
/// (z. B. username/courseName); der Text wird clientseitig über "notifications.type.&lt;type&gt;" gerendert.</summary>
public record NotificationDto(
    int Id,
    string Type,
    Dictionary<string, string>? Data,
    string? Link,
    DateTime CreatedAt,
    bool Seen);

/// <summary>Ungelesen-Zähler für das Navbar-Glocken-Badge.</summary>
public record NotificationCountDto(int Count);

/// <summary>Eine Seite der vollständigen Benachrichtigungs-History (neueste zuerst) + Gesamtzahl
/// (für „mehr laden"/Pager).</summary>
public record NotificationHistoryDto(List<NotificationDto> Items, int Total);
