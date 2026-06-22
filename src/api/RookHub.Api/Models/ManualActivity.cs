using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Art einer manuell eingetragenen (selbst gemeldeten) Offline-Trainingsaktivität.
/// Jede Art mappt beim Aggregieren auf genau eine bestehende Tracker-Dimension:
/// </summary>
public enum ManualActivityKind
{
    /// <summary>OTB-Partie (Rapid/Classical, Verein/Turnier) → Kategorie „Spielen" (Wochenziel, Amount = Partienzahl).</summary>
    OtbGame = 0,
    /// <summary>Offline-Puzzletraining (Taktik/Puzzlebuch am Brett) → Kategorie „Puzzles" (Amount = Minuten).</summary>
    OfflinePuzzle = 1,
    /// <summary>Offline-Studium (Buch/Eröffnung/Spielanalyse am Brett) → Kategorie „Buch/Kurs" (Amount = Minuten).</summary>
    OfflineStudy = 2,
    /// <summary>Trainerstunde/Lektion (Coaching, Vereinstraining, Video/Stream) → Kategorie „Buch/Kurs" (Amount = Minuten).</summary>
    Coaching = 3,
}

/// <summary>
/// Manuell eingetragene Offline-Trainingsaktivität eines Users. Anders als die automatisch erfassten
/// Quellen (PuzzleAttempt, CourseAttempt, ChessableActivity, PlayTimeDaily) wird das hier vom User
/// selbst gemeldet — und ist daher korrigierbar (Bearbeiten/Löschen). Speist über
/// <see cref="Services.TrainingGoalService"/> dieselben bestehenden Kategorien des Trackers
/// (Spielen / Puzzles / Buch-Kurs) und wird dort als „manuell" markiert.
/// </summary>
public class ManualActivity
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>UTC-Tag, an dem die Aktivität stattfand (DATE-Spalte, keine Uhrzeit).</summary>
    public DateOnly Date { get; set; }

    public ManualActivityKind Kind { get; set; }

    /// <summary>Bei <see cref="ManualActivityKind.OtbGame"/> = Anzahl Partien; sonst = trainierte Minuten.</summary>
    public int Amount { get; set; }

    /// <summary>Optionale Notiz (z.B. Gegner/Turnier/Thema).</summary>
    [MaxLength(200)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
