namespace RookHub.Api.Models;

/// <summary>
/// Benachrichtigung an den „Opfer"-User (Target), wenn ein Freund (Avenger) eines seiner
/// gescheiterten Puzzles im Revenge-Modus angegangen ist — egal ob gelöst oder nicht.
/// </summary>
public class RevengeNotification
{
    public int Id { get; set; }

    /// <summary>Der Freund, der die Revanche versucht hat.</summary>
    public int AvengerUserId { get; set; }
    public AppUser AvengerUser { get; set; } = null!;

    /// <summary>Der User, dessen gescheitertes Puzzle „gerächt" wurde (Empfänger der Benachrichtigung).</summary>
    public int TargetUserId { get; set; }
    public AppUser TargetUser { get; set; } = null!;

    public int PuzzleId { get; set; }
    public Puzzle Puzzle { get; set; } = null!;

    /// <summary>Hat der Avenger das Puzzle gelöst?</summary>
    public bool Solved { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Vom Target gesehen (null = ungelesen → zählt fürs Badge).</summary>
    public DateTime? SeenAt { get; set; }
}
