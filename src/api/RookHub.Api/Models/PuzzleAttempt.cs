namespace RookHub.Api.Models;

public class PuzzleAttempt
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public int PuzzleId { get; set; }
    public Puzzle Puzzle { get; set; } = null!;

    public bool Solved { get; set; }
    public int TimeSpentSeconds { get; set; }
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
}
