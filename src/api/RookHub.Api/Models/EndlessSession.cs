using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RookHub.Api.Models;

public class EndlessSession
{
    public int Id { get; set; }

    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    [MaxLength(36)]
    public string? AnonymousSessionId { get; set; }

    public long Timestamp { get; set; }
    public int TotalSolved { get; set; }
    public int MaxRating { get; set; }
    public int DurationSeconds { get; set; }

    [Column(TypeName = "TEXT")]
    public string ConfigJson { get; set; } = string.Empty;

    [MaxLength(100)]
    public string MistakeAtRatings { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
