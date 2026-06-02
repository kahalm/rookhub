namespace RookHub.Api.Models;

/// <summary>
/// Lösungsversuch eines eingeloggten Users an einem Buch-Puzzle (Standalone-/Tagespuzzle).
/// Grundlage für die Tagespuzzle-Visualisierung auf Discord (wer hat heute gelöst).
/// </summary>
public class BookPuzzleAttempt
{
    public int Id { get; set; }

    public int BookPuzzleId { get; set; }
    public BookPuzzle BookPuzzle { get; set; } = null!;

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public bool Solved { get; set; }
    public int TimeSeconds { get; set; }
    public DateTime AttemptedAt { get; set; }
}
