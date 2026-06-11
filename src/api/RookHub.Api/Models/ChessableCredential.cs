using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Persistierter Chessable-Bearer eines RookHub-Users. RookHub speichert den
/// Token verschlüsselt und reicht ihn pro Request an die piratechess-API durch,
/// die die Chessable-Calls (curl-impersonate) tatsächlich ausführt.
/// </summary>
public class ChessableCredential
{
    public int Id { get; set; }

    /// <summary>Besitzer; Cascade-Delete mit dem User. 1:1.</summary>
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>AES-verschlüsselter Bearer (Base64). Plaintext nie persistiert.</summary>
    [Required]
    public string EncryptedBearer { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
