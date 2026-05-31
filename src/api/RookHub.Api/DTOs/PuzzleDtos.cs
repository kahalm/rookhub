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

    [MaxLength(10000)]
    public string? MoveLog { get; set; }

    [Range(0, 10000)]
    public int? ScreenWidth { get; set; }

    [Range(0, 10000)]
    public int? ScreenHeight { get; set; }

    [Range(0, 4)]
    public int VisualizationLevel { get; set; } = 0;
}

public class PuzzleStatsDto
{
    public int TotalAttempts { get; set; }
    public int Solved { get; set; }
    public double Accuracy { get; set; }
    public int CurrentStreak { get; set; }
    public int BestStreak { get; set; }
    public int PuzzleElo { get; set; } = 1500;
    public Dictionary<int, int>? PuzzleEloPerLevel { get; set; }
}

public class AnonymousAttemptDto
{
    [Required, MaxLength(36)]
    public string SessionId { get; set; } = string.Empty;

    public bool Solved { get; set; }

    [Range(0, 3600)]
    public int TimeSpentSeconds { get; set; }

    [MaxLength(10000)]
    public string? MoveLog { get; set; }

    [Range(0, 10000)]
    public int? ScreenWidth { get; set; }

    [Range(0, 10000)]
    public int? ScreenHeight { get; set; }

    [Range(0, 4)]
    public int VisualizationLevel { get; set; } = 0;
}

public class ClaimSessionDto
{
    [Required, MaxLength(36)]
    public string SessionId { get; set; } = string.Empty;
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
    public string? MoveLog { get; set; }
    public int? EloAfter { get; set; }
    public int? EloChange { get; set; }
    public int VisualizationLevel { get; set; }
}
