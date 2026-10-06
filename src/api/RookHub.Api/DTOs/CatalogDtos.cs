using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>Katalog-Freigaben eines Besitzers: welche User + Gruppen den Katalog sehen dürfen.</summary>
public class CatalogGrantsDto
{
    public List<int> UserIds { get; set; } = new();
    public List<int> GroupIds { get; set; } = new();
    /// <summary>Nur in Antworten: Namen zu <see cref="UserIds"/>, damit die Oberfläche die Freigegebenen
    /// anzeigen kann, ohne die ganze Nutzerliste zu laden. Beim Speichern ignoriert.</summary>
    public List<CatalogGrantUserDto> Users { get; set; } = new();
}

/// <summary>Ein freigegebener Nutzer mit Namen (Antwort von GET/PUT /api/catalog/grants).</summary>
public class CatalogGrantUserDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
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
