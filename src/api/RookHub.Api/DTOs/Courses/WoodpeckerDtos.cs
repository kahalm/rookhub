using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs.Courses;

/// <summary>Neues Woodpecker-Training: Kurse in der gewünschten Reihenfolge, Start optional rückdatiert.</summary>
public class CreateWoodpeckerPlanDto
{
    [Required, MinLength(1), MaxLength(50)]
    public List<int> BookIds { get; set; } = new();

    /// <summary>Beginn von Zyklus 1 (UTC); leer = jetzt. Darf in der Vergangenheit liegen, nicht in der Zukunft.</summary>
    public DateTime? StartedAt { get; set; }
}

/// <summary>Beginn des laufenden Zyklus verschieben (rückdatieren).</summary>
public class UpdateWoodpeckerStartDto
{
    [Required]
    public DateTime StartedAt { get; set; }
}

public class RecordWoodpeckerAttemptDto
{
    public int BookPuzzleId { get; set; }
    public bool Solved { get; set; }
    public int TimeSeconds { get; set; }
}

public class WoodpeckerBookDto
{
    public int BookId { get; set; }
    public string DisplayName { get; set; } = "";
    /// <summary>Lösbare Aufgaben (ohne Erklärlinien).</summary>
    public int PuzzleCount { get; set; }
}

public class WoodpeckerCycleDto
{
    public int Number { get; set; }
    public int TargetDays { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime Deadline { get; set; }
    public DateTime? FinishedAt { get; set; }
    /// <summary>In diesem Zyklus bearbeitete Aufgaben.</summary>
    public int Attempted { get; set; }
    public int Solved { get; set; }
    /// <summary>Summe der Lösezeiten in diesem Zyklus.</summary>
    public int TimeSeconds { get; set; }
}

public class WoodpeckerPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<WoodpeckerBookDto> Books { get; set; } = new();
    /// <summary>Größe des Satzes (in Zyklus 1 wachsend).</summary>
    public int SetSize { get; set; }
    /// <summary>Lösbare Aufgaben aller Kurse zusammen.</summary>
    public int AvailableCount { get; set; }
    public int CurrentCycle { get; set; }
    public int MaxCycles { get; set; }
    /// <summary><c>solving</c> (Zyklus läuft), <c>break</c> (Pause, nächster Zyklus ab <see cref="NextCycleAt"/>),
    /// <c>ready</c> (nächster Zyklus kann starten), <c>completed</c>.</summary>
    public string Status { get; set; } = "solving";
    public DateTime? NextCycleAt { get; set; }
    public List<WoodpeckerCycleDto> Cycles { get; set; } = new();
}

public class WoodpeckerNextDto
{
    /// <summary>Nächste Aufgabe; <c>null</c>, wenn der Zyklus erledigt ist oder keine Aufgabe mehr übrig.</summary>
    public BookPuzzleDto? Puzzle { get; set; }
    public WoodpeckerPlanDto Plan { get; set; } = new();
}
