using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

public class PuzzleDto
{
    public int Id { get; set; }
    public string LichessId { get; set; } = string.Empty;
    public string Fen { get; set; } = string.Empty;
    public string Moves { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? Themes { get; set; }
    public string? GameUrl { get; set; }
    /// <summary>Von einem Nutzer als „dumme Tipps" markiert (Review-Flag; siehe <c>Puzzle.HintsFlagged</c>).</summary>
    public bool HintsFlagged { get; set; }
}

public class RandomBatchRequestDto
{
    public List<RatingWindowDto> Windows { get; set; } = new();
    public string? Themes { get; set; }
    public bool ExcludeSolved { get; set; }
    /// <summary>ODER-Themenfilter (mind. eins) — für „schwächste Themen trainieren".</summary>
    public string? ThemesAny { get; set; }
}

/// <summary>Ein Rating-Fenster des Offline-Vorab-Ladens. Beide Grenzen sind validiert: ein Fenster mit
/// <c>MinRating &gt; MaxRating</c> bzw. <c>MaxRating = int.MaxValue</c> ließ den Tag-Index-Pfad
/// (<c>Random.Shared.Next(min, max + 1)</c>) mit einer ArgumentOutOfRangeException in einen 500 laufen —
/// anonym auslösbar über <c>POST /api/puzzles/random-batch</c>.</summary>
public class RatingWindowDto
{
    [Range(0, 5000)]
    public int MinRating { get; set; }

    [Range(0, 5000)]
    public int MaxRating { get; set; }
}

public class RecordPuzzleAttemptDto
{
    public bool Solved { get; set; }

    [Range(0, 3600)]
    public int TimeSpentSeconds { get; set; }

    [MaxLength(10000)]
    public string? MoveLog { get; set; }

    [Range(0, 10000)]
    public int? ScreenWidth { get; set; }

    [Range(0, 10000)]
    public int? ScreenHeight { get; set; }

    [Range(0, 4)]
    public int VisualizationLevel { get; set; } = 0;

    public bool EvalShown { get; set; } = false;

    [Range(0, 100)]
    public int VizShowCount { get; set; } = 0;

    /// <summary>Höchste aufgedeckte Tipp-Stufe (0–3).</summary>
    [Range(0, 3)]
    public int HintsUsed { get; set; } = 0;

    // KEIN Modus-Feld: die Spielweise ergibt sich aus VisualizationLevel (0 = "easy", > 0 =
    // "training"). Ein zweites Feld wäre widerspruchsfähig (Stufe 0 + "training").
}

public class PuzzleStatsDto
{
    public int TotalAttempts { get; set; }
    public int Solved { get; set; }
    public double Accuracy { get; set; }
    public int CurrentStreak { get; set; }
    public int BestStreak { get; set; }
    public int PuzzleElo { get; set; } = 1500;
    public Dictionary<int, int>? PuzzleEloPerLevel { get; set; }
    /// <summary>Versuche im Modus „training" — abgeleitet: Visualisierungsstufe &gt; 0.</summary>
    public int TrainingCount { get; set; }
    /// <summary>Versuche im Modus „easy" — abgeleitet: Visualisierungsstufe 0 (Drag &amp; Drop).</summary>
    public int EasyCount { get; set; }
}

public class AnonymousAttemptDto
{
    [Required, MaxLength(36)]
    public string SessionId { get; set; } = string.Empty;

    public bool Solved { get; set; }

    [Range(0, 3600)]
    public int TimeSpentSeconds { get; set; }

    [MaxLength(10000)]
    public string? MoveLog { get; set; }

    [Range(0, 10000)]
    public int? ScreenWidth { get; set; }

    [Range(0, 10000)]
    public int? ScreenHeight { get; set; }

    [Range(0, 4)]
    public int VisualizationLevel { get; set; } = 0;

    public bool EvalShown { get; set; } = false;

    [Range(0, 100)]
    public int VizShowCount { get; set; } = 0;

    /// <summary>Höchste aufgedeckte Tipp-Stufe (0–3).</summary>
    [Range(0, 3)]
    public int HintsUsed { get; set; } = 0;

    // KEIN Modus-Feld — siehe RecordPuzzleAttemptDto: die Spielweise folgt aus VisualizationLevel.
}

public class ClaimSessionDto
{
    [Required, MaxLength(36)]
    public string SessionId { get; set; } = string.Empty;
}

public class PuzzleAttemptDto
{
    public int Id { get; set; }
    public int PuzzleId { get; set; }
    public string LichessId { get; set; } = string.Empty;
    public int PuzzleRating { get; set; }
    public bool Solved { get; set; }
    public int TimeSpentSeconds { get; set; }
    public DateTime AttemptedAt { get; set; }
    public string? MoveLog { get; set; }
    public int? EloAfter { get; set; }
    public int? EloChange { get; set; }
    public int VisualizationLevel { get; set; }
    /// <summary>
    /// Spielweise dieses Versuchs ("training"/"easy") — ABGELEITET aus <see cref="VisualizationLevel"/>
    /// (0 = „easy", &gt; 0 = „training") und NICHT gespeichert. Der Standard-Puzzle-Versuch hat bewusst
    /// keine Modus-Spalte; auch Altbestand liefert damit den tatsächlich gespielten Modus.
    /// </summary>
    public string Mode { get; set; } = Models.SolveMode.Easy;
}

/// <summary>Ein Punkt der Puzzle-Elo-Kurve (für die Statistikseite).</summary>
public class EloHistoryPointDto
{
    public DateTime AttemptedAt { get; set; }
    public int Elo { get; set; }
    public int VizLevel { get; set; }
    public bool Solved { get; set; }
}

// --- Statistik-Aufschlüsselungen ---

public class ThemeStatDto { public string Theme { get; set; } = string.Empty; public int Attempts { get; set; } public int Solved { get; set; } }
public class RatingBandStatDto { public int From { get; set; } public int To { get; set; } public int Attempts { get; set; } public int Solved { get; set; } }
public class ActivityDayDto { public string Date { get; set; } = string.Empty; public int Count { get; set; } }

public class PuzzleBreakdownDto
{
    /// <summary>Top-Themen nach Anzahl Versuche.</summary>
    public List<ThemeStatDto> Themes { get; set; } = new();
    /// <summary>Rating-Bänder (200er-Schritte) mit Versuchen/Gelöst — nach Schwierigkeit + Rating-Verteilung.</summary>
    public List<RatingBandStatDto> RatingBands { get; set; } = new();
    /// <summary>Versuche pro Tag (letzte 365 Tage) für die Aktivitäts-Heatmap.</summary>
    public List<ActivityDayDto> Activity { get; set; } = new();
}
