using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Wo eine Kurslinie im E-Book steht (Wunsch 2026-10-15: „aus dem Kurs immer zum Buch springen"). Angelegt vom Abgleich
/// über die Stellung (<c>CourseEbookLinker</c>); gilt für alle, die Kurs und Buch sehen dürfen.
///
/// <para><b>Robust gegen Neulesen</b>: ein neu gelesenes Buch löscht seine Diagramme und legt sie neu an (neue Ids). Der
/// Verweis merkt sich deshalb NICHT nur das Diagramm, sondern Buch, Figurenstellung (<see cref="Placement"/>) und
/// Seite; das Diagramm fällt beim Löschen auf <c>null</c> (SetNull) und wird über die Stellung wiedergefunden — nach dem
/// Neulesen und spätestens beim nächsten Abruf (<c>CourseEbookLinker.RelinkEbookAsync</c>).</para>
/// </summary>
public class CourseLineEbookRef
{
    public int Id { get; set; }

    public int BookPuzzleId { get; set; }
    public BookPuzzle? BookPuzzle { get; set; }

    public int EbookId { get; set; }
    public Ebook? Ebook { get; set; }

    /// <summary>Aktuelles Diagramm; <c>null</c>, solange es nach einem Neulesen (noch) nicht wiedergefunden ist.</summary>
    public int? EbookDiagramId { get; set; }
    public EbookDiagram? EbookDiagram { get; set; }

    /// <summary>Figurenstellung (FEN-Feld 1) des Diagramms — der Schlüssel zum Wiederfinden.</summary>
    [MaxLength(100)]
    public string Placement { get; set; } = string.Empty;

    /// <summary>Seite beim Verknüpfen; bei mehreren gleichen Stellungen gewinnt die nächstgelegene.</summary>
    public int Page { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
