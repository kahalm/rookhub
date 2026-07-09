namespace RookHub.Api.Models;

/// <summary>
/// Katalog-Freigabe: erlaubt einem User ODER einer Gruppe, die LISTE der Kurse/Repertoires eines
/// Besitzers (<see cref="OwnerUserId"/>, aktuell nur Admins) zu sehen und einzelne Items anzufordern.
/// Genau eines von <see cref="SubjectUserId"/>/<see cref="SubjectGroupId"/> ist gesetzt.
/// Gibt NUR Sichtbarkeit auf den Katalog — kein Zugriff auf die Inhalte; der entsteht erst, wenn der
/// Besitzer eine Anforderung (<see cref="CatalogRequest"/>) genehmigt (→ Kurs-/Repertoire-Teilen).
/// </summary>
public class CatalogGrant
{
    public int Id { get; set; }
    public int OwnerUserId { get; set; }
    public int? SubjectUserId { get; set; }
    public int? SubjectGroupId { get; set; }
    public DateTime CreatedAt { get; set; }
}
