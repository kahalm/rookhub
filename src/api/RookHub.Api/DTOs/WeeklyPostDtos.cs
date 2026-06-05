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
    /// <summary>Gesamtzeit des Users über alle gespielten Puzzles dieses Wochenposts in Sekunden.</summary>
    public int TotalSeconds { get; set; }
    /// <summary>Indizes der bereits gespielten Puzzles (für „zum ersten neuen Puzzle springen"); leer in der Übersicht.</summary>
    public List<int> PlayedIndices { get; set; } = new();
}

/// <summary>Aggregierte Wochenpost-Ergebnisse (für die Discord-Anzeige): wer wie weit ist.</summary>
public class WeeklyPostResultsDto
{
    public int WeeklyPostId { get; set; }
    /// <summary>Anzahl Puzzles im Wochenpost.</summary>
    public int Total { get; set; }
    /// <summary>Anzahl User, die alle Puzzles gespielt haben (= erledigt).</summary>
    public int CompletedCount { get; set; }
    public List<WeeklyPlayerResultDto> Players { get; set; } = new();
}

/// <summary>Stand eines Users bei einem Wochenpost (für die Discord-Anzeige).</summary>
public class WeeklyPlayerResultDto
{
    public string Name { get; set; } = string.Empty;
    public string? DiscordId { get; set; }
    public string? DiscordUsername { get; set; }
    public int PlayedCount { get; set; }
    public int SolvedCount { get; set; }
    /// <summary>Gesamtzeit über alle gespielten Puzzles in Sekunden.</summary>
    public int TotalSeconds { get; set; }
    /// <summary>True, wenn alle Puzzles gespielt wurden.</summary>
    public bool Completed { get; set; }
}
