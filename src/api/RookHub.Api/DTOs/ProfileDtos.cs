using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

public class ProfileDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? FideId { get; set; }
    public string? ChessResultsId { get; set; }
    public string? ChessComUsername { get; set; }
    public string? LichessUsername { get; set; }

    // Discord-Verknüpfung (Username zur Anzeige; ID ist die eindeutige Identität)
    public string? DiscordId { get; set; }
    public string? DiscordUsername { get; set; }

    // User Preferences
    public string? BoardTheme { get; set; }
    public string? PieceSet { get; set; }
    public int? StockfishDepth { get; set; }
    public string? PuzzleDifficulty { get; set; }
    public int? BookStockfishDepth { get; set; }
}

/// <summary>
/// Öffentliche Profil-Sicht (anonym via Username abrufbar). Enthält bewusst NUR nicht-sensible,
/// ohnehin öffentliche Schach-Identitäten — KEINE Klarnamen, ChessResults-ID, Discord-Verknüpfung
/// oder Einstellungen (sonst De-Anonymisierung/PII-Leak an Unauthentifizierte).
/// </summary>
public class PublicProfileDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? FideId { get; set; }
    public string? ChessComUsername { get; set; }
    public string? LichessUsername { get; set; }
}

public class UpdateProfileDto
{
    [MaxLength(50)]
    public string? FirstName { get; set; }

    [MaxLength(50)]
    public string? LastName { get; set; }

    [MaxLength(50)]
    public string? DisplayName { get; set; }

    [MaxLength(20), RegularExpression(@"^\d*$", ErrorMessage = "FideId must be numeric.")]
    public string? FideId { get; set; }

    [MaxLength(20), RegularExpression(@"^\d*$", ErrorMessage = "ChessResultsId must be numeric.")]
    public string? ChessResultsId { get; set; }

    [MaxLength(50), RegularExpression(@"^[a-zA-Z0-9_-]*$", ErrorMessage = "ChessComUsername may only contain letters, digits, hyphens and underscores.")]
    public string? ChessComUsername { get; set; }

    [MaxLength(50), RegularExpression(@"^[a-zA-Z0-9_-]*$", ErrorMessage = "LichessUsername may only contain letters, digits, hyphens and underscores.")]
    public string? LichessUsername { get; set; }

    // User Preferences
    [MaxLength(20)]
    public string? BoardTheme { get; set; }

    [MaxLength(20)]
    public string? PieceSet { get; set; }

    [Range(1, 24)]
    public int? StockfishDepth { get; set; }

    [MaxLength(20)]
    public string? PuzzleDifficulty { get; set; }

    [Range(1, 24)]
    public int? BookStockfishDepth { get; set; }
}

public class LinkDiscordDto
{
    [Required]
    public string Token { get; set; } = string.Empty;
}

/// <summary>Bestätigung für das Löschen des eigenen Accounts — verlangt das aktuelle Passwort.</summary>
public class DeleteAccountDto
{
    [Required]
    public string Password { get; set; } = string.Empty;
}
