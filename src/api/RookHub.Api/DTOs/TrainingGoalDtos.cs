using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>
/// Effektives Trainingsziel eines Users — persönlicher Override, sonst Gruppen-Vorlage, sonst keins.
/// Minuten = Tagesziel je Kategorie (0 = Kategorie nicht Teil des Ziels).
/// </summary>
public class TrainingGoalDto
{
    public int PuzzleMinutes { get; set; }
    public int BookMinutes { get; set; }
    public int PlayMinutes { get; set; }
    public int WeeklyDaysTarget { get; set; }

    /// <summary>"personal" = eigener Override, "group" = aus Gruppen-Vorlage geerbt, "none" = noch keins gesetzt.</summary>
    public string Source { get; set; } = "none";
    /// <summary>Name der Gruppe, aus der die Vorlage stammt (nur bei <see cref="Source"/> = "group").</summary>
    public string? GroupName { get; set; }
}

/// <summary>Eingabe zum Setzen eines Ziels (persönlich oder als Gruppen-Vorlage).</summary>
public class TrainingGoalInputDto
{
    [Range(0, 600)] public int PuzzleMinutes { get; set; }
    [Range(0, 600)] public int BookMinutes { get; set; }
    [Range(0, 600)] public int PlayMinutes { get; set; }
    [Range(0, 7)] public int WeeklyDaysTarget { get; set; }
}

/// <summary>Ein Tag im Ziele-Tracker mit verbrachten Sekunden je Kategorie + Tagesstatus.</summary>
public class TrackerDayDto
{
    /// <summary>UTC-Datum als yyyy-MM-dd.</summary>
    public string Date { get; set; } = string.Empty;
    public int PuzzleSeconds { get; set; }
    public int BookSeconds { get; set; }
    public int PlaySeconds { get; set; }
    /// <summary>"none" | "partial" | "full" gegenüber dem effektiven Ziel.</summary>
    public string Status { get; set; } = "none";
}

/// <summary>Effektives Ziel + Tagesreihe für den Tracker (nur Tage mit Aktivität).</summary>
public class TrackerResponseDto
{
    public TrainingGoalDto Goal { get; set; } = new();
    public List<TrackerDayDto> Days { get; set; } = new();
}

/// <summary>Fortschritt einer einzelnen Ziel-Kategorie.</summary>
public class CategoryProgressDto
{
    public int TargetMinutes { get; set; }
    public int DoneSeconds { get; set; }
    public bool Met { get; set; }
}

/// <summary>Heutiger Fortschritt je Kategorie + Wochenstand.</summary>
public class TodayProgressDto
{
    public TrainingGoalDto Goal { get; set; } = new();
    public CategoryProgressDto Puzzles { get; set; } = new();
    public CategoryProgressDto Book { get; set; } = new();
    public CategoryProgressDto Play { get; set; } = new();
    /// <summary>"none" | "partial" | "full" für heute.</summary>
    public string Status { get; set; } = "none";
    /// <summary>Voll erfüllte Tage in der laufenden ISO-Woche (Mo–So, inkl. heute).</summary>
    public int WeekDaysMet { get; set; }
    public int WeeklyDaysTarget { get; set; }
}
