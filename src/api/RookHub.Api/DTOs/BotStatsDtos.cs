namespace RookHub.Api.DTOs;

/// <summary>
/// Kompakter Spieler-Fortschritt für den Schach-Bot (personalisierter Motivations-DM). Der Spieler wird
/// über seine Discord-ID identifiziert; nur für mit RookHub verknüpfte Konten verfügbar. Bewusst
/// erweiterbar gehalten (z. B. späterer Wochenpost-Progress, sobald RookHub den pro User trackt).
/// </summary>
public class BotPlayerProgressDto
{
    /// <summary>RookHub-Username des verknüpften Kontos.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Anzeigename, falls gesetzt — sonst <c>null</c> (Bot fällt auf den Username zurück).</summary>
    public string? DisplayName { get; set; }

    /// <summary>Heutiger Trainings-Fortschritt + Wochenstand (Ziele, Puzzle-/Buch-Minuten, Spielen-Partien).</summary>
    public TodayProgressDto Today { get; set; } = new();

    /// <summary>Aggregierte Puzzle-Statistik (Elo, gelöst, Streaks, Genauigkeit).</summary>
    public PuzzleStatsDto Puzzles { get; set; } = new();

    /// <summary>Aktueller (jüngster fälliger) Wochenpost + Fortschritt des Users — null, wenn keiner existiert.</summary>
    public BotWeeklyPostDto? WeeklyPost { get; set; }
}

/// <summary>Wochenpost-Stand für den Motivations-Bot: der aktuelle Post + wie weit der User ist.</summary>
public class BotWeeklyPostDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime ScheduledAt { get; set; }
    /// <summary>Anzahl Puzzles im Wochenpost.</summary>
    public int Total { get; set; }
    /// <summary>Gespielte Puzzles des Users.</summary>
    public int PlayedCount { get; set; }
    /// <summary>Davon gelöst.</summary>
    public int SolvedCount { get; set; }
    /// <summary>True, wenn der User alle Puzzles gespielt hat (= erledigt).</summary>
    public bool Completed { get; set; }
}
