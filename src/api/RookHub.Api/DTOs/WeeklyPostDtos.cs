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
