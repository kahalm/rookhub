namespace RookHub.Api.Models;

public enum CatalogItemType { Course = 0, Repertoire = 1 }

/// <summary>
/// Anforderung eines einzelnen Katalog-Items (Kurs oder Repertoire) durch einen berechtigten Viewer.
/// Der Besitzer (<see cref="OwnerUserId"/>) genehmigt/lehnt ab; bei Genehmigung wird das Item über die
/// bestehende Kurs-/Repertoire-Teilen-Logik freigegeben. <see cref="Status"/>: pending/approved/declined.
/// </summary>
public class CatalogRequest
{
    public int Id { get; set; }
    public int RequesterUserId { get; set; }
    public int OwnerUserId { get; set; }
    public CatalogItemType ItemType { get; set; }
    /// <summary>BookId (Kurs) bzw. RepertoireId — je nach <see cref="ItemType"/>.</summary>
    public int ItemId { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
