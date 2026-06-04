namespace RookHub.Api.Models;

/// <summary>
/// Persönlicher Trainingsziel-Override eines Users. Existiert er, hat er Vorrang vor jeder
/// <see cref="GroupTrainingGoal"/>-Vorlage seiner Gruppen. Minuten = Tagesziel je Kategorie
/// (0 = Kategorie ist nicht Teil des Ziels).
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
    /// <summary>Tagesziel Spielen (Lichess/chess.com) in Minuten.</summary>
    public int PlayMinutes { get; set; }
    /// <summary>Wochenziel: Anzahl voll erfüllter Tage (0–7) pro ISO-Woche.</summary>
    public int WeeklyDaysTarget { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
