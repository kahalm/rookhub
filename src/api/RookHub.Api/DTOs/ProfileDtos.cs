using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

public class ProfileDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? FideId { get; set; }
    public string? ChessResultsId { get; set; }
    public string? ChessComUsername { get; set; }
    public string? LichessUsername { get; set; }
}

public class UpdateProfileDto
{
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
}
