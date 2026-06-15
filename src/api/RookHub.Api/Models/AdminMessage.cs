namespace RookHub.Api.Models;

/// <summary>
/// Eine Nachricht in der Admin↔User-Konversation. Ein „Thread" = alle <see cref="AdminMessage"/> mit
/// derselben <see cref="UserId"/> (dem Nicht-Admin-Teilnehmer), chronologisch. Der Admin startet den
/// Thread; danach können beide Seiten beliebig oft antworten. <see cref="FromAdmin"/> bestimmt die
/// Richtung. Read-Receipts getrennt je Seite (<see cref="SeenByUserAt"/>/<see cref="SeenByAdminAt"/>).
/// </summary>
public class AdminMessage
{
    public int Id { get; set; }

    /// <summary>Nicht-Admin-Teilnehmer = Thread-Schlüssel. Mit diesem User wird konversiert.</summary>
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>Tatsächlicher Absender (Audit): bei Admin-Nachrichten die Admin-Id, sonst == <see cref="UserId"/>.</summary>
    public int SenderId { get; set; }

    /// <summary>true = vom Admin an den User; false = Antwort des Users.</summary>
    public bool FromAdmin { get; set; }

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gesetzt, sobald der User eine Admin-Nachricht gelesen hat (nur relevant für FromAdmin=true).</summary>
    public DateTime? SeenByUserAt { get; set; }

    /// <summary>Gesetzt, sobald ein Admin eine User-Antwort gelesen hat (nur relevant für FromAdmin=false).</summary>
    public DateTime? SeenByAdminAt { get; set; }
}
