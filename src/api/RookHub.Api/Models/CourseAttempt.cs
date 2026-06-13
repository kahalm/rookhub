namespace RookHub.Api.Models;

/// <summary>
/// Append-only Zeit-Log eines Kurs-Lösungsversuchs (gelöst, fehlgeschlagen oder Wiederholung) —
/// im Gegensatz zur idempotenten „gelöst"-Menge <see cref="CoursePuzzleResult"/> wird hier JEDER
/// Versuch festgehalten. Grundlage für die akkumulierte Kurs-/Studienzeit im Trainingsziele-Tracker
/// (Routing in die Kategorie Puzzles vs. Buch/Kurs richtet sich nach <see cref="Book.Kind"/>).
/// <see cref="BookId"/> ist denormalisiert für den Kind-Join ohne Umweg über BookPuzzle.
/// </summary>
public class CourseAttempt
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int BookId { get; set; }
    public Book? Book { get; set; }

    public int BookPuzzleId { get; set; }
    public BookPuzzle? BookPuzzle { get; set; }

    public bool Solved { get; set; }

    /// <summary>Am Puzzle verbrachte Zeit in Sekunden (gegen Inflation beim Aggregieren gedeckelt).</summary>
    public int TimeSeconds { get; set; }

    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
}
