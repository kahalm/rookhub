using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

public class PuzzleDto
{
    public int Id { get; set; }
    public string LichessId { get; set; } = string.Empty;
    public string Fen { get; set; } = string.Empty;
    public string Moves { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? Themes { get; set; }
    public string? GameUrl { get; set; }
}

public class RecordPuzzleAttemptDto
{
    public bool Solved { get; set; }

    [Range(0, 3600)]
    public int TimeSpentSeconds { get; set; }
}

public class PuzzleStatsDto
{
    public int TotalAttempts { get; set; }
    public int Solved { get; set; }
    public double Accuracy { get; set; }
    public int CurrentStreak { get; set; }
    public int BestStreak { get; set; }
}

public class PuzzleAttemptDto
{
    public int Id { get; set; }
    public int PuzzleId { get; set; }
    public string LichessId { get; set; } = string.Empty;
    public int PuzzleRating { get; set; }
    public bool Solved { get; set; }
    public int TimeSpentSeconds { get; set; }
    public DateTime AttemptedAt { get; set; }
}
