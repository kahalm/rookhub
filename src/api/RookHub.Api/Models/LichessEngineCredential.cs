using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Persistierter Lichess-API-Token (Scope <c>engine:read</c>) eines RookHub-Users für die
/// External-Engine-Anbindung: RookHub listet damit die auf dem Lichess-Konto des Users
/// registrierten External Engines (eigene Maschine via offiziellem Provider, Miet-Anbieter
/// wie stockfishcloud) und reicht Analyse-Anfragen an den Lichess-Broker durch.
/// AES-verschlüsselt wie der Chessable-Bearer; Plaintext nie persistiert.
/// </summary>
public class LichessEngineCredential
{
    public int Id { get; set; }

    /// <summary>Besitzer; Cascade-Delete mit dem User. 1:1.</summary>
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>AES-verschlüsselter Lichess-Token (EncryptionService, v2/GCM).</summary>
    [Required]
    public string EncryptedToken { get; set; } = string.Empty;

    /// <summary>Lichess-Engine-ID der HINTERGRUND-Engine für Analyseaufträge (null = keine). Der
    /// Live-Picker blendet sie aus; der Worker pausiert sie, sobald der Nutzer live extern rechnet.</summary>
    [MaxLength(64)]
    public string? BackgroundEngineId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
