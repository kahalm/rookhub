namespace RookHub.Api.Models;

/// <summary>
/// Der selbst eingeklickte Analysebaum EINES Users zu EINER Stellung eines Kalkulationsbuchs
/// (<see cref="Book.IsCalculation"/>). Im Kalkulations-Modus gibt es keine Lösung: der Nutzer
/// rechnet die Stellung am eingefrorenen Brett durch und legt seine Varianten (für beide Seiten)
/// samt Kommentaren und Bewertungssymbolen selbst an.
/// <para>Der Baum wird als opakes JSON gehalten (<see cref="TreeJson"/>, Schema-Version im Dokument),
/// weil nur das Frontend ihn interpretiert — so kann sich die Baum-Struktur weiterentwickeln, ohne
/// dass jede Änderung eine Migration braucht. Unique über (UserId, BookPuzzleId) ⇒ ein Baum je
/// Nutzer und Stellung; <see cref="BookId"/> ist denormalisiert (= BookPuzzle.BookId) für die
/// „bearbeitet"-Zähler der Kursübersicht ohne Join.</para>
/// </summary>
public class CalculationTree
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int BookId { get; set; }
    public Book? Book { get; set; }

    public int BookPuzzleId { get; set; }
    public BookPuzzle? BookPuzzle { get; set; }

    /// <summary>Serialisierter Analysebaum (LONGTEXT). Vom Server nicht interpretiert, nur auf
    /// gültiges JSON und Maximalgröße geprüft (siehe <c>CalculationService.MaxTreeJsonLength</c>).</summary>
    public string TreeJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
