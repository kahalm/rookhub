using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Eine als FLASHCARD markierte KURS-Linie eines Users: speist den Bereich „nur markierte
/// Flashcards" eines Kurses (ansehen/durchgehen/drucken). Eine Zeile je (User, Linie).
/// <para>FK-Konvention wie CoursePuzzleResult/CalculationTree: <c>BookPuzzleId</c> ist RESTRICT
/// (vermeidet doppelte Cascade-Pfade von Book) — Linien-/Kapitel-/Buch-Löschpfade räumen die
/// Markierungen explizit ab.</para>
/// </summary>
public class CourseFlashcardMark
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Denormalisiert fürs schnelle Laden aller Markierungen eines Kurses.</summary>
    public int BookId { get; set; }
    public Book? Book { get; set; }

    public int BookPuzzleId { get; set; }
    public BookPuzzle? BookPuzzle { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Eine als FLASHCARD markierte REPERTOIRE-Linie eines Users. Identität der Linie ist der
/// <c>LineKey</c> (Hash der Zugfolge — dieselbe Identität wie Linienliste/SR-Trainer): ändert
/// sich die Zugfolge beim Re-Import, gilt die Linie als neu und die Markierung läuft ins Leere
/// (gewolltes Verhalten, wie beim SR-Zustand).
/// </summary>
public class RepertoireFlashcardMark
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int RepertoireId { get; set; }
    public Repertoire? Repertoire { get; set; }

    [Required, MaxLength(120)]
    public string LineKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
