namespace RookHub.Api.Models;

/// <summary>
/// Persönlicher Trainingsziel-Override eines Users. Existiert er, hat er Vorrang vor jeder
/// <see cref="GroupTrainingGoal"/>-Vorlage seiner Gruppen. Puzzles/Buch = Minuten/Tag (Tagesziel),
/// Spielen = Anzahl Rapid-/Classical-Partien pro ISO-Woche (jeweils 0 = nicht Teil des Ziels).
/// </summary>
public class UserTrainingGoal
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Tagesziel Puzzles (Standard + Endlos + Tages-/Buch-Puzzle) in Minuten.</summary>
    public int PuzzleMinutes { get; set; }
    /// <summary>Tagesziel Buchstudie/Kurse in Minuten.</summary>
    public int BookMinutes { get; set; }
    /// <summary>Wochenziel Spielen (Lichess/chess.com): Anzahl Rapid-/Classical-Partien pro ISO-Woche.</summary>
    public int PlayGames { get; set; }
    /// <summary>Wochenziel: Anzahl voll erfüllter Tage (0–7) pro ISO-Woche.</summary>
    public int WeeklyDaysTarget { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
