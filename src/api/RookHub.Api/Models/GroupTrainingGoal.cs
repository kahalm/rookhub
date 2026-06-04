namespace RookHub.Api.Models;

/// <summary>
/// Coach-Vorlage für Trainingsziele je Gruppe. Wird zum effektiven Tagesziel eines
/// Mitglieds, solange dieses keinen persönlichen <see cref="UserTrainingGoal"/>-Override hat.
/// Puzzles/Buch = Minuten/Tag (Tagesziel), Spielen = Anzahl Rapid-/Classical-Partien pro ISO-Woche
/// (jeweils 0 = Kategorie ist nicht Teil des Ziels).
/// </summary>
public class GroupTrainingGoal
{
    public int Id { get; set; }

    public int GroupId { get; set; }
    public Group? Group { get; set; }

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
