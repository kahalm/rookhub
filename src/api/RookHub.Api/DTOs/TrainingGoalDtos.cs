using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>
/// Effektives Trainingsziel eines Users — persönlicher Override, sonst Gruppen-Vorlage, sonst keins.
/// Puzzles/Buch = Minuten/Tag (Tagesziel), Spielen = Anzahl Rapid-/Classical-Partien pro ISO-Woche
/// (jeweils 0 = Kategorie nicht Teil des Ziels).
/// </summary>
public class TrainingGoalDto
{
    public int PuzzleMinutes { get; set; }
    public int BookMinutes { get; set; }
    /// <summary>Wochenziel: Anzahl Rapid-/Classical-Partien pro ISO-Woche.</summary>
    public int PlayGames { get; set; }
    public int WeeklyDaysTarget { get; set; }

    /// <summary>"personal" = eigener Override, "group" = aus Gruppen-Vorlage geerbt, "none" = noch keins gesetzt.</summary>
    public string Source { get; set; } = "none";
    /// <summary>Name der Gruppe, aus der die Vorlage stammt (nur bei <see cref="Source"/> = "group").</summary>
    public string? GroupName { get; set; }
}

/// <summary>Eingabe zum Setzen eines Ziels (persönlich oder als Gruppen-Vorlage).
/// Puzzles/Buch = Minuten/Tag, Spielen = Partien/Woche.</summary>
public class TrainingGoalInputDto
{
    [Range(0, 600)] public int PuzzleMinutes { get; set; }
    [Range(0, 600)] public int BookMinutes { get; set; }
    [Range(0, 200)] public int PlayGames { get; set; }
    [Range(0, 7)] public int WeeklyDaysTarget { get; set; }
}

/// <summary>Ein Tag im Ziele-Tracker: verbrachte Sekunden (Puzzles/Buch) + gespielte Partien an dem Tag
/// (informativ — das Spielen-Ziel ist wöchentlich) + Tagesstatus (nur Puzzles/Buch).</summary>
public class TrackerDayDto
{
    /// <summary>UTC-Datum als yyyy-MM-dd.</summary>
    public string Date { get; set; } = string.Empty;
    public int PuzzleSeconds { get; set; }
    public int BookSeconds { get; set; }
    /// <summary>Rapid-/Classical-Partien an diesem Tag (informativ; Tagesstatus nutzt nur Puzzles/Buch).</summary>
    public int PlayGames { get; set; }
    /// <summary>"none" | "partial" | "full" gegenüber dem effektiven Tagesziel (Puzzles/Buch).</summary>
    public string Status { get; set; } = "none";
}

/// <summary>Effektives Ziel + Tagesreihe für den Tracker (nur Tage mit Aktivität).</summary>
public class TrackerResponseDto
{
    public TrainingGoalDto Goal { get; set; } = new();
    public List<TrackerDayDto> Days { get; set; } = new();
}

/// <summary>Fortschritt einer zeitbasierten Tages-Kategorie (Puzzles/Buch).</summary>
public class CategoryProgressDto
{
    public int TargetMinutes { get; set; }
    public int DoneSeconds { get; set; }
    public bool Met { get; set; }
}

/// <summary>Fortschritt des wöchentlichen Spielen-Ziels (Anzahl Rapid-/Classical-Partien in der laufenden ISO-Woche).</summary>
public class PlayProgressDto
{
    public int TargetGames { get; set; }
    public int DoneGames { get; set; }
    public bool Met { get; set; }
}

/// <summary>Heutiger Fortschritt (Puzzles/Buch je Tag) + Wochenstand (Spielen-Partien + voll erfüllte Tage).</summary>
public class TodayProgressDto
{
    public TrainingGoalDto Goal { get; set; } = new();
    public CategoryProgressDto Puzzles { get; set; } = new();
    public CategoryProgressDto Book { get; set; } = new();
    /// <summary>Wöchentliches Spielen-Ziel: Partien in der laufenden ISO-Woche (Mo–So) vs. Zielanzahl.</summary>
    public PlayProgressDto Play { get; set; } = new();
    /// <summary>"none" | "partial" | "full" für heute (nur Tagesziele Puzzles/Buch).</summary>
    public string Status { get; set; } = "none";
    /// <summary>Voll erfüllte Tage in der laufenden ISO-Woche (Mo–So, inkl. heute).</summary>
    public int WeekDaysMet { get; set; }
    public int WeeklyDaysTarget { get; set; }
}
