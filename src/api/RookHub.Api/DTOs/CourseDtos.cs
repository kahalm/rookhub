namespace RookHub.Api.DTOs;

/// <summary>Ein Buch als Kurs in der Übersicht inkl. nutzerbezogenem Fortschritt.</summary>
public class CourseListItemDto
{
    public int BookId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Difficulty { get; set; }
    public int? Rating { get; set; }
    public string? Tags { get; set; }
    public string? Description { get; set; }
    public int PuzzleCount { get; set; }
    public int SolvedCount { get; set; }
    /// <summary>0–100, gerundet. 0 bei leerem Buch.</summary>
    public int ProgressPercent { get; set; }
    public string? LastMode { get; set; }
}

/// <summary>Nächstes zu lösendes Puzzle eines Kurses + aktueller Fortschritt.</summary>
public class CourseNextPuzzleDto
{
    /// <summary>Null, wenn der Kurs abgeschlossen ist (<see cref="Completed"/> = true).</summary>
    public BookPuzzleDto? Puzzle { get; set; }
    public int SolvedCount { get; set; }
    public int Total { get; set; }
    public bool Completed { get; set; }
}

/// <summary>Aufzeichnung eines Lösungsversuchs im Kurs.</summary>
public class RecordCourseResultDto
{
    public int BookPuzzleId { get; set; }
    public bool Solved { get; set; }
    /// <summary>Optional: "sequential" oder "random" — aktualisiert den zuletzt genutzten Modus.</summary>
    public string? Mode { get; set; }
}

/// <summary>Fortschritt eines Kurses (Buch) für den aktuellen User.</summary>
public class CourseProgressDto
{
    public int BookId { get; set; }
    public int SolvedCount { get; set; }
    public int Total { get; set; }
    public int ProgressPercent { get; set; }
    public bool Completed { get; set; }
    public string? LastMode { get; set; }
}
