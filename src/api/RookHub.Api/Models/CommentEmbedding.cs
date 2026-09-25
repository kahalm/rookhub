using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// „Frag die Kommentare" (0.536.0): ein Stück Kommentartext einer Bibliothekspartie samt Vektor für die semantische
/// Suche (<c>CommentSearchService</c>). Ein Stück fasst aufeinanderfolgende kommentierte Halbzüge zusammen
/// („17. Bxh7+: …"), bis etwa <see cref="CommentChunks.TargetChars"/> Zeichen.
/// </summary>
/// <remarks>Der Vektor ist eine MariaDB-Spalte <c>VECTOR(512)</c> mit Kosinus-Index (seit MariaDB 11.7; Prod und Dev
/// laufen 11.8). EF kennt den Typ nicht — hier ist er <c>byte[]</c> (float32, little endian, genau 4 × 512 Bytes), so
/// nimmt MariaDB ihn auch als Parameter an. Den Index legt die Migration per SQL an.</remarks>
public class CommentEmbedding
{
    /// <summary>Länge der Vektoren — steht in der Spalte (<c>VECTOR(512)</c>) und wird beim Einbetten angefordert
    /// (Matryoshka-Modelle wie Qwen3-Embedding kürzen ohne Qualitätseinbruch).</summary>
    public const int Dimensions = 512;

    public long Id { get; set; }

    public int LibraryGameId { get; set; }
    public LibraryGame? LibraryGame { get; set; }

    /// <summary>Erster und letzter kommentierter Halbzug des Stücks (−1 = Einleitung vor dem ersten Zug).</summary>
    public int FromPly { get; set; }
    public int ToPly { get; set; }

    /// <summary>Der eingebettete Text — zugleich der Auszug, den die Suche zeigt.</summary>
    [Required, MaxLength(1600)] public string Text { get; set; } = string.Empty;

    [Required] public byte[] Vector { get; set; } = Array.Empty<byte>();

    [MaxLength(80)] public string? Model { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
