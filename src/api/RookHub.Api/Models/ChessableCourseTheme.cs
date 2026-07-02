namespace RookHub.Api.Models;

/// <summary>
/// Thema (Partiephase bzw. Taktik), das ein User einem Chessable-Kurs oder einer manuellen
/// Offline-Aktivität zuordnet, damit die Trainingszeit in der Themen-Aufschlüsselung des Trainingsziele-
/// Trackers richtig einsortiert wird. Deckungsgleich mit der internen Themen-Klassifikation
/// (<c>Thm</c>) des <see cref="Services.TrainingGoalService"/>.
/// <para><see cref="Other"/> = ausdrücklich unklassifiziert (v. a. manuelle Aktivitäten wie „Endspiel-
/// Analyse mit Freund", die keiner der drei Phasen passt). Für Chessable-Kurs-Zuordnungen bleibt der
/// unassigned-Default weiterhin Other, ohne dass der User Other explizit wählen muss.</para>
/// </summary>
public enum ChessableTheme
{
    Opening = 0,
    Middlegame = 1,
    Endgame = 2,
    Tactics = 3,
    Other = 4,
}

/// <summary>
/// Manuelle Zuordnung „Chessable-Kurs-ID → Thema" eines Users. Greift, wenn ein Kurs nicht automatisch
/// über ein RookHub-Repertoire (ChessableCourseId) klassifiziert werden kann. Wirkt auf die
/// Themen-Aufschlüsselung im Tracker — auch rückwirkend für bereits gemeldete Aktivitäten desselben
/// Kurses (alle <see cref="ChessableActivity"/> mit gleicher <see cref="CourseId"/>).
/// </summary>
public class ChessableCourseTheme
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Chessable-Kurs-ID (numerisch als String). Eindeutig je User.</summary>
    public string CourseId { get; set; } = string.Empty;

    /// <summary>Zuletzt gesehener lesbarer Kursname (nur Anzeige; aus der jüngsten Aktivität übernommen).</summary>
    public string? CourseName { get; set; }

    public ChessableTheme Theme { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
