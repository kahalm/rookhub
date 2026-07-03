namespace RookHub.Api.Models;

/// <summary>
/// Persönliche Verknüpfung zweier Kurse (Bücher) desselben Nutzers — typisch „Buch ↔ Workbook",
/// damit man beim sequenziellen Durcharbeiten schnell zwischen beiden hin- und herwechseln kann.
/// Pro Nutzer wird die Verknüpfung SYMMETRISCH in ZWEI Zeilen gespeichert (A→B und B→A), sodass der
/// Partner von jedem Buch aus mit einem einzigen (UserId, BookId)-Lookup gefunden wird; ein Buch hat
/// je Nutzer höchstens einen Partner (Unique (UserId, BookId)).
/// </summary>
public class CourseLink
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>Ausgangsbuch dieser (gerichteten) Zeile.</summary>
    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    /// <summary>Verknüpfter Partner-Kurs. Bewusst KEIN zweiter Book-FK (sonst zwei Cascade-Pfade von
    /// Book → CourseLink); die Gegenzeile + Cleanup beim Buch-Löschen halten die Konsistenz.</summary>
    public int LinkedBookId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
