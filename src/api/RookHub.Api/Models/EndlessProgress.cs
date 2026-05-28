using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RookHub.Api.Models;

public class EndlessProgress
{
    public int Id { get; set; }

    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    [MaxLength(36)]
    public string? AnonymousSessionId { get; set; }

    public int StartElo { get; set; }
    public int Step { get; set; }

    [MaxLength(200)]
    public string Themes { get; set; } = string.Empty;

    public bool Fasttrack { get; set; }
    public int? FasttrackThreshold1 { get; set; }
    public int? FasttrackThreshold2 { get; set; }
    public int StockfishDepth { get; set; } = 16;

    public int Highscore { get; set; }

    [Column(TypeName = "LONGTEXT")]
    public string? ActiveGameState { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
