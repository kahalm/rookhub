using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Einmal-Token fuer „Passwort vergessen". Der Rohwert wird per E-Mail an den User
/// geschickt und NIE gespeichert — in der DB liegt nur sein SHA-256-Hex-Hash (analog
/// zu <see cref="UserApiToken"/>). Ein Token ist gueltig bis <see cref="ExpiresAt"/>
/// und genau einmal einloesbar (<see cref="UsedAt"/> wird beim Einloesen gesetzt).
/// </summary>
public class PasswordResetToken
{
    public int Id { get; set; }

    /// <summary>Betroffener User; Cascade-Delete mit dem User.</summary>
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>SHA-256-Hex des Roh-Tokens (64 Zeichen). Unique-indexed fuer O(1)-Lookup.</summary>
    [Required, MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Ablaufzeitpunkt — danach ist das Token ungueltig.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Gesetzt beim Einloesen; danach kann das Token nicht erneut verwendet werden.</summary>
    public DateTime? UsedAt { get; set; }
}
