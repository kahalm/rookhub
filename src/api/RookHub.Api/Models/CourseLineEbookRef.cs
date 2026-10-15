namespace RookHub.Api.Models;

/// <summary>
/// Wo eine Kurslinie im E-Book steht (Wunsch 2026-10-15: „aus dem Kurs immer zum Buch springen"): Verweis auf das
/// erkannte Diagramm (<see cref="EbookDiagram"/>, daraus Buch + Seite). Angelegt vom Abgleich über die Stellung
/// (<c>CourseEbookLinker</c>); gilt für alle, die Kurs und Buch sehen dürfen. Ein neu erkanntes Buch löscht seine
/// Diagramme und damit auch diese Verweise (Kaskade) — dann einfach neu verknüpfen.
/// </summary>
public class CourseLineEbookRef
{
    public int Id { get; set; }

    public int BookPuzzleId { get; set; }
    public BookPuzzle? BookPuzzle { get; set; }

    public int EbookDiagramId { get; set; }
    public EbookDiagram? EbookDiagram { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
