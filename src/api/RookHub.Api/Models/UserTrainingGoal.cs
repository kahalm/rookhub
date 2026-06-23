namespace RookHub.Api.Models;

/// <summary>
/// Persönlicher Trainingsziel-Override eines Users. Existiert er, hat er Vorrang vor jeder
/// <see cref="GroupTrainingGoal"/>-Vorlage seiner Gruppen. Ein einziges Tageszeit-Ziel
/// (<see cref="DailyMinutes"/>), das von allen Quellen (Puzzle/Kurs/Chessable) gemeinsam gefüllt wird;
/// Spielen = Anzahl Rapid-/Classical-Partien pro ISO-Woche (jeweils 0 = nicht Teil des Ziels).
/// </summary>
public class UserTrainingGoal
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Tagesziel Trainingszeit in Minuten — gemeinsamer Topf, gefüttert von allen Quellen
    /// (Standard-/Endlos-/Buch-Puzzle, Kurse, Chessable). 0 = kein Tageszeit-Ziel.</summary>
    public int DailyMinutes { get; set; }
    /// <summary>Wochenziel Spielen (Lichess/chess.com): Anzahl Rapid-/Classical-Partien pro ISO-Woche.</summary>
    public int PlayGames { get; set; }
    /// <summary>Wochenziel: Anzahl voll erfüllter Tage (0–7) pro ISO-Woche.</summary>
    public int WeeklyDaysTarget { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
