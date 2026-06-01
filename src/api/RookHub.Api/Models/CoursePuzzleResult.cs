namespace RookHub.Api.Models;

/// <summary>
/// Quelle der Wahrheit, welche <see cref="BookPuzzle"/> ein User im Kurs (Buch) gelöst hat.
/// Eine Zeile je gelöstem Puzzle; Unique über (UserId, BookPuzzleId) macht das Aufzeichnen
/// idempotent. <see cref="BookId"/> ist denormalisiert (= BookPuzzle.BookId) für schnelle
/// Fortschritts-Counts ohne Join.
/// </summary>
public class CoursePuzzleResult
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int BookId { get; set; }
    public Book? Book { get; set; }

    public int BookPuzzleId { get; set; }
    public BookPuzzle? BookPuzzle { get; set; }

    public DateTime SolvedAt { get; set; } = DateTime.UtcNow;
}
