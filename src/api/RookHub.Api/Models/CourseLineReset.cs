namespace RookHub.Api.Models;

/// <summary>
/// Wann ein User eine einzelne Kurs-Linie zuletzt über den KAPITEL-Reset zurückgesetzt hat. Der Kapitel-Reset
/// löscht die gelösten Linien, lässt aber das Zeit-Log <see cref="CourseAttempt"/> stehen (Trainingsziele) —
/// und der Kurs schließt jede Linie mit einem Versuch seit dem Reset aus. Ohne diesen Zeitpunkt zählte dort nur
/// der buchweite <see cref="CourseProgress.ResetAt"/>: nach einem Kapitel-Reset fielen die eben gespielten
/// Linien weiter aus dem Pool, und der Kurs meldete „abgeschlossen" (gemeldet 2026-10-05, Kurs 434).
/// Ein Versuch zählt nur, wenn er NACH beiden Zeitpunkten liegt (<c>CourseService.AttemptsSinceReset</c>).
/// Eine Zeile je Linie; der buchweite Reset löscht die Zeilen des Buchs (er überholt sie).
/// </summary>
public class CourseLineReset
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int BookId { get; set; }
    public Book? Book { get; set; }

    public int BookPuzzleId { get; set; }
    public BookPuzzle? BookPuzzle { get; set; }

    public DateTime ResetAt { get; set; } = DateTime.UtcNow;
}
