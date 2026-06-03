namespace RookHub.Api.Models;

/// <summary>
/// Zuordnung eines konkreten Buch-Puzzles zu einem UTC-Datum: was war/ist
/// das Tagespuzzle an diesem Tag? Wird vom <c>DailyPuzzleScheduler</c> um
/// 00:00 UTC angelegt, on-demand nachgeholt, falls die API zu dem Zeitpunkt
/// nicht lief. <c>Date</c> ist der Primaerschluessel (genau ein Eintrag je Tag).
/// </summary>
public class DailyPuzzle
{
    /// <summary>UTC-Datum (DATE-Spalte, keine Uhrzeit).</summary>
    public DateOnly Date { get; set; }

    /// <summary>FK auf das gewaehlte Buch-Puzzle.</summary>
    public int BookPuzzleId { get; set; }
    public BookPuzzle? BookPuzzle { get; set; }

    /// <summary>Zeitpunkt der Zuordnung (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
