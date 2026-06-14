using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>Ergebnis einer Revanche melden: A hat das gescheiterte Puzzle von B (gelöst/nicht gelöst) angegangen.</summary>
public class RevengeResultDto
{
    [Required]
    public int TargetUserId { get; set; }
    [Required]
    public int PuzzleId { get; set; }
    public bool Solved { get; set; }
}

/// <summary>Eine Revanche-Benachrichtigung für den Ziel-User (B).</summary>
public class RevengeNotificationDto
{
    public int Id { get; set; }
    public int AvengerUserId { get; set; }
    public string AvengerUsername { get; set; } = string.Empty;
    public string? AvengerDisplayName { get; set; }
    public int PuzzleId { get; set; }
    public int Rating { get; set; }
    public bool Solved { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool Seen { get; set; }
}
