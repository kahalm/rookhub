namespace RookHub.Api.Models;

/// <summary>
/// Metadaten einer Admin↔User-Konversation (ein Eintrag je User-Thread, Schlüssel = <see cref="UserId"/>).
/// Trägt die Zuweisung: ein Admin kann einen Thread „übernehmen" (<see cref="ClaimedByAdminId"/>), damit
/// die übrigen Admins sehen, dass sich jemand kümmert. Die eigentlichen Nachrichten liegen in
/// <see cref="AdminMessage"/>. Die Zeile entsteht mit der ersten Nachricht (egal welche Seite).
/// </summary>
public class MessageThread
{
    /// <summary>Nicht-Admin-Teilnehmer = Thread-Schlüssel (PK + FK auf AppUser, Cascade).</summary>
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>Admin, der den Thread übernommen hat (null = unbearbeitet). Bewusst OHNE FK-Navigation,
    /// um doppelte Cascade-Pfade auf AppUser zu vermeiden; Name wird beim Abruf aufgelöst.</summary>
    public int? ClaimedByAdminId { get; set; }

    public DateTime? ClaimedAt { get; set; }
}
