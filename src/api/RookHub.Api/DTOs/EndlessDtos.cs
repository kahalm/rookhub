using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

// --- Response DTOs ---

public class EndlessSyncResponseDto
{
    public EndlessProgressDto? Progress { get; set; }
    public List<EndlessSessionDto> Sessions { get; set; } = new();
}

public class EndlessProgressDto
{
    public int StartElo { get; set; }
    public string Themes { get; set; } = string.Empty;
    public int? FasttrackThreshold1 { get; set; }
    public int? FasttrackThreshold2 { get; set; }
    public int StockfishDepth { get; set; }
    public int Highscore { get; set; }
    public string? ActiveGameState { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class EndlessSessionDto
{
    public int Id { get; set; }
    public long Timestamp { get; set; }
    public int TotalSolved { get; set; }
    public int MaxRating { get; set; }
    public int DurationSeconds { get; set; }
    public string ConfigJson { get; set; } = string.Empty;
    public string MistakeAtRatings { get; set; } = string.Empty;
    public string? Seed { get; set; }
    public string? ChainPuzzleIds { get; set; }
    public bool IsArchived { get; set; }
}

// --- Request DTOs ---

public class SaveEndlessProgressDto
{
    [Range(0, 5000)]
    public int StartElo { get; set; }

    [MaxLength(200)]
    public string Themes { get; set; } = string.Empty;

    public int? FasttrackThreshold1 { get; set; }
    public int? FasttrackThreshold2 { get; set; }

    [Range(1, 24)]
    public int StockfishDepth { get; set; } = 16;

    [Range(0, 100000)]
    public int Highscore { get; set; }

    [MaxLength(1_000_000)]
    public string? ActiveGameState { get; set; }
}

public class SaveAnonymousProgressDto : SaveEndlessProgressDto
{
    [Required, MaxLength(36), RegularExpression(ValidationConstants.SessionIdPattern)]
    public string SessionId { get; set; } = string.Empty;
}

public class RecordEndlessSessionDto
{
    public long Timestamp { get; set; }

    [Range(0, 10000)]
    public int TotalSolved { get; set; }

    [Range(0, 100000)]
    public int MaxRating { get; set; }

    [Range(0, 100000)]
    public int DurationSeconds { get; set; }

    [MaxLength(5000)]
    public string ConfigJson { get; set; } = string.Empty;

    [MaxLength(100)]
    public string MistakeAtRatings { get; set; } = string.Empty;

    /// <summary>Eindeutiger Seed des Gauntlet-Laufs (identifiziert die Kette für ein späteres Replay).</summary>
    [MaxLength(64)]
    public string? Seed { get; set; }

    /// <summary>Geordnete Puzzle-IDs der gespielten Kette als CSV (für späteres Replay).</summary>
    [MaxLength(20000)]
    public string? ChainPuzzleIds { get; set; }

    /// <summary>Optional: einzelne Puzzles der Session (nur fürs Logging der Start-/Lösungszeit, nicht persistiert).</summary>
    public List<EndlessSessionPuzzleDto> Puzzles { get; set; } = new();
}

/// <summary>Ein einzelnes Puzzle einer Endless-Session (Start-/Lösungszeit als Unix-Millis) — nur fürs Logging, nicht persistiert.</summary>
public class EndlessSessionPuzzleDto
{
    public int PuzzleId { get; set; }
    public string? LichessId { get; set; }
    public int Rating { get; set; }
    public bool Solved { get; set; }
    public long StartedAt { get; set; }
    public long EndedAt { get; set; }
}

public class RecordAnonymousSessionDto : RecordEndlessSessionDto
{
    [Required, MaxLength(36), RegularExpression(ValidationConstants.SessionIdPattern)]
    public string SessionId { get; set; } = string.Empty;
}

public class BulkImportSessionDto
{
    public List<RecordEndlessSessionDto> Sessions { get; set; } = new();
}

public class BulkImportAnonymousSessionDto
{
    [Required, MaxLength(36), RegularExpression(ValidationConstants.SessionIdPattern)]
    public string SessionId { get; set; } = string.Empty;

    public List<RecordEndlessSessionDto> Sessions { get; set; } = new();
}

public class ClaimEndlessSessionDto
{
    [Required, MaxLength(36), RegularExpression(ValidationConstants.SessionIdPattern)]
    public string AnonymousSessionId { get; set; } = string.Empty;
}

public class ArchiveSessionsDto
{
    [Required]
    public List<int> SessionIds { get; set; } = new();

    public bool Archive { get; set; } = true;
}

public class EndlessHistoryResponseDto
{
    public List<EndlessSessionDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
