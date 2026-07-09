using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>Katalog-Freigaben eines Besitzers: welche User + Gruppen den Katalog sehen dürfen.</summary>
public class CatalogGrantsDto
{
    public List<int> UserIds { get; set; } = new();
    public List<int> GroupIds { get; set; } = new();
}

/// <summary>Ein Item im Katalog (aus Viewer-Sicht) inkl. eigenem Status.</summary>
public class CatalogItemDto
{
    public int OwnerUserId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    /// <summary>"course" oder "repertoire".</summary>
    public string ItemType { get; set; } = string.Empty;
    public int ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>"none" (noch nicht angefordert) / "pending" (angefordert) / "shared" (bereits freigegeben).</summary>
    public string Status { get; set; } = "none";
}

public class CatalogRequestInputDto
{
    [Required]
    public string ItemType { get; set; } = string.Empty;   // "course" | "repertoire"
    public int ItemId { get; set; }
}

/// <summary>Eine offene/erledigte Anforderung aus Besitzer-Sicht.</summary>
public class CatalogRequestDto
{
    public int Id { get; set; }
    public int RequesterUserId { get; set; }
    public string RequesterName { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
}

/// <summary>Ob der aufrufende User überhaupt einen Katalog sehen darf (Menü-/Route-Gate).</summary>
public class CatalogAccessDto
{
    public bool HasAccess { get; set; }
}
