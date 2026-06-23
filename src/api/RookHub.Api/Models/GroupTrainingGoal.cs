namespace RookHub.Api.Models;

/// <summary>
/// Coach-Vorlage für Trainingsziele je Gruppe. Wird zum effektiven Tagesziel eines
/// Mitglieds, solange dieses keinen persönlichen <see cref="UserTrainingGoal"/>-Override hat.
/// Ein einziges Tageszeit-Ziel (<see cref="DailyMinutes"/>, gemeinsamer Topf aller Quellen),
/// Spielen = Anzahl Rapid-/Classical-Partien pro ISO-Woche (jeweils 0 = nicht Teil des Ziels).
/// </summary>
public class GroupTrainingGoal
{
    public int Id { get; set; }

    public int GroupId { get; set; }
    public Group? Group { get; set; }

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
