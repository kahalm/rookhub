using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Persoenlicher API-Token eines Users (GitHub-PAT-Stil) fuer Maschinen-Clients
/// wie die chess.com-Tampermonkey-Extension. Der Raw-Token (Format
/// <c>rkh_&lt;43-char-base64url&gt;</c>) wird nur beim Anlegen einmalig im
/// Response geliefert; gespeichert ist nur der SHA-256-Hash.
/// </summary>
public class UserApiToken
{
    public int Id { get; set; }

    /// <summary>Besitzer; Cascade-Delete mit dem User.</summary>
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Vom User vergebener Name (z. B. „Chess.com Extension").</summary>
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256-Hex des Raw-Tokens (64 Zeichen). Unique-indexed.</summary>
    [Required, MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Erste 12 Zeichen des Raw-Tokens (inkl. <c>rkh_</c>-Prefix) — fuer die UI-Anzeige.</summary>
    [Required, MaxLength(12)]
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Berechtigungs-Bereich (z. B. <c>extension</c>). Bestimmt, welche Endpoints akzeptieren.</summary>
    [Required, MaxLength(50)]
    public string Scope { get; set; } = "extension";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Letzter erfolgreicher Auth-Hit; <c>null</c> wenn noch nie benutzt.</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Optionaler Ablauf — <c>null</c> = laeuft nie ab.</summary>
    public DateTime? ExpiresAt { get; set; }
}
