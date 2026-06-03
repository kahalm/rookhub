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

    /// <summary>Eindeutiger Seed des Gauntlet-Laufs (z.B. crypto.randomUUID) — identifiziert die Kette (Replay).</summary>
    [MaxLength(64)]
    public string? Seed { get; set; }

    /// <summary>Geordnete Puzzle-IDs der gespielten Kette (CSV). Puzzles sind über die ID unveränderlich → exaktes Replay.</summary>
    [Column(TypeName = "TEXT")]
    public string? ChainPuzzleIds { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
