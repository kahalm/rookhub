using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RookHub.Api.Models;

public class RepertoireFile
{
    public int Id { get; set; }

    public int RepertoireId { get; set; }
    public Repertoire Repertoire { get; set; } = null!;

    [Required, MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Column(TypeName = "LONGTEXT")]
    public string PgnContent { get; set; } = string.Empty;

    public long FileSize { get; set; }

    /// <summary>
    /// Zwischenspeicher der <c>[ChessableOid "…"]</c>-Kennungen dieses PGN, zeilenweise.
    /// <c>null</c> = noch nicht ermittelt. Existiert, weil das Extension-Overlay bei JEDEM Poll
    /// wissen muss, welche Linien schon importiert sind — dafür wurde bisher das vollständige
    /// PGN geladen und per Regex durchsucht, im Request-Pfad.
    /// </summary>
    public string? ChessableOidsCache { get; set; }

    /// <summary>
    /// Länge des PGN, aus dem <see cref="ChessableOidsCache"/> stammt. Weicht sie von der
    /// aktuellen ab, ist der Zwischenspeicher veraltet und wird neu aufgebaut. BEWUSST so und
    /// nicht per Pflege an jeder Schreibstelle: wird eine davon übersehen, zeigte das Overlay
    /// sonst dauerhaft falsche Haken. Der Chessable-Cache wächst nur, eine Änderung bei exakt
    /// gleicher Länge kann es dort nicht geben.
    /// Randfall: Der Vergleich läuft in SQL über CHAR_LENGTH, in C# über string.Length — bei
    /// Zeichen außerhalb der Basic Multilingual Plane (Emoji in einem Kommentar) zählen die
    /// beiden unterschiedlich. Folge ist dann nur, dass der Zwischenspeicher dauerhaft als
    /// veraltet gilt und das alte Verhalten greift; falsche Kennungen entstehen nicht.
    /// </summary>
    public int? ChessableOidsPgnLength { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
