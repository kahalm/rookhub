using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>Wochenpost in der Liste (ohne PGN-Inhalt).</summary>
public class WeeklyPostDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime ScheduledAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Wochenpost-Detail inkl. PGN-Inhalt (für den Viewer).</summary>
public class WeeklyPostDetailDto : WeeklyPostDto
{
    public string PgnContent { get; set; } = string.Empty;
}

/// <summary>Editierbare Felder eines Wochenposts (Termin + Titel).</summary>
public class UpdateWeeklyPostDto
{
    [MaxLength(300)]
    public string? Title { get; set; }
    public DateTime? ScheduledAt { get; set; }
}

/// <summary>Wochenpost als Puzzle-Sequenz zum Durchspielen (PGN on-the-fly geparst).</summary>
public class WeeklyPlayDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<BookPuzzleDto> Puzzles { get; set; } = new();
}

/// <summary>Aufzeichnung eines gespielten Wochenpost-Puzzles (gelöst oder nicht).</summary>
public class RecordWeeklyAttemptDto
{
    [Range(0, 100000)] public int PuzzleIndex { get; set; }
    public bool Solved { get; set; }
    [Range(0, 86400)] public int TimeSeconds { get; set; }
}

/// <summary>Per-User-Fortschritt eines Wochenposts. „Erledigt" = alle Puzzles gespielt (Solved egal).</summary>
public class WeeklyPostProgressDto
{
    public int WeeklyPostId { get; set; }
    /// <summary>Anzahl Puzzles im Wochenpost.</summary>
    public int Total { get; set; }
    /// <summary>Anzahl gespielter (= aufgezeichneter) Puzzles.</summary>
    public int PlayedCount { get; set; }
    /// <summary>Davon gelöste Puzzles.</summary>
    public int SolvedCount { get; set; }
    /// <summary>True, wenn alle Puzzles gespielt wurden.</summary>
    public bool Completed { get; set; }
}
