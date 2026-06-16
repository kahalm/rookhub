namespace RookHub.Api.DTOs;

/// <summary>Ein Eintrag einer Bestenliste (ein Nutzer + sein Zählwert in der gewählten Periode).</summary>
public class LeaderboardEntryDto
{
    public string Name { get; set; } = string.Empty;
    public string? DiscordId { get; set; }
    public string? DiscordUsername { get; set; }
    /// <summary>Zählwert: einzigartige gelöste Puzzles, abgeschlossene Endlos-Läufe bzw. gelöste Kurs-Linien.</summary>
    public int Count { get; set; }
}

/// <summary>
/// Bestenlisten für eine Periode (daily/weekly/monthly/alltime) über die drei Kategorien:
/// einzigartige Standard-Puzzles, Endlos-Läufe und gelöste Kurs-Linien.
/// </summary>
public class LeaderboardsDto
{
    public string Period { get; set; } = string.Empty;
    public List<LeaderboardEntryDto> Puzzles { get; set; } = new();
    public List<LeaderboardEntryDto> EndlessRuns { get; set; } = new();
    public List<LeaderboardEntryDto> CourseLines { get; set; } = new();
    /// <summary>Einzigartige gelöste Tagespuzzles (Buch-Puzzles, die als Daily zugeordnet waren/sind).</summary>
    public List<LeaderboardEntryDto> DailyPuzzles { get; set; } = new();
}
