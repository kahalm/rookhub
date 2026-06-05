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
}
