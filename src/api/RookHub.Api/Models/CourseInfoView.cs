namespace RookHub.Api.Models;

/// <summary>
/// Merkt, welche Info-/Erklärlinie (<see cref="BookPuzzle.IsInfoOnly"/>) ein User im Kurs
/// bereits sequenziell durchgeklickt hat. Info-Linien sind kein Quiz und landen daher nicht in
/// <see cref="CoursePuzzleResult"/> (das würde den Fortschritt verfälschen). Ohne diese Spur würde
/// der sequenzielle Modus dieselbe Info-Linie bei jedem Wiedereinstieg von vorne zeigen; mit ihr
/// startet der Kurs beim nächsten Mal hinter der zuletzt durchgeklickten Info-Linie.
/// Eine Zeile je gesehener Info-Linie; Unique über (UserId, BookPuzzleId) macht das Aufzeichnen
/// idempotent. <see cref="BookId"/> ist denormalisiert (= BookPuzzle.BookId) für schnelle Filter.
/// </summary>
public class CourseInfoView
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int BookId { get; set; }
    public Book? Book { get; set; }

    public int BookPuzzleId { get; set; }
    public BookPuzzle? BookPuzzle { get; set; }

    public DateTime SeenAt { get; set; } = DateTime.UtcNow;
}
