using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public class UserProfile
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    [MaxLength(50)]
    public string? FirstName { get; set; }

    [MaxLength(50)]
    public string? LastName { get; set; }

    [MaxLength(100)]
    public string? DisplayName { get; set; }

    [MaxLength(20)]
    public string? FideId { get; set; }

    [MaxLength(20)]
    public string? ChessResultsId { get; set; }

    [MaxLength(50)]
    public string? ChessComUsername { get; set; }

    [MaxLength(50)]
    public string? LichessUsername { get; set; }

    // User Preferences (synced across devices)
    [MaxLength(20)]
    public string? BoardTheme { get; set; }

    [MaxLength(20)]
    public string? PieceSet { get; set; }

    public int? StockfishDepth { get; set; }

    [MaxLength(20)]
    public string? PuzzleDifficulty { get; set; }

    public int? BookStockfishDepth { get; set; }
}
