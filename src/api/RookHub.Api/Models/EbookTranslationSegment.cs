using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Ein Text einer EXTERN beanspruchten Buch-Übersetzung (Einspeisepunkt für externe Übersetzer, z. B. Claude-Agenten):
/// genau der Modelltext, den sonst die Spark bekäme (<see cref="Services.Ebooks.EbookTextSegment.ModelText"/>, Platzhalter
/// <c>&lt;tN&gt;…&lt;/tN&gt;</c>/<c>&lt;tN/&gt;</c>), jeder verschiedene Text des Buchs einmal in Lesereihenfolge.
/// Lebt nur zwischen Beanspruchen und Zusammenbauen bzw. Freigeben — danach hält der Zwischenspeicher
/// (<c>converted/&lt;id&gt;.&lt;lang&gt;.cache.jsonl</c>) die Übersetzungen.
/// </summary>
public class EbookTranslationSegment
{
    public long Id { get; set; }

    public int EbookTranslationId { get; set; }
    public EbookTranslation? Translation { get; set; }

    /// <summary>Laufende Nummer 0…n-1 (Reihenfolge des ersten Vorkommens), je Übersetzung eindeutig.</summary>
    public int Index { get; set; }

    /// <summary>Dokument des EPUB, in dem der Text zuerst vorkommt (z. B. <c>OEBPS/Text/ch1.xhtml</c>) — nur zur Orientierung.</summary>
    [MaxLength(300)]
    public string DocumentPath { get; set; } = "";

    /// <summary>Modelltext (Quelle mit Platzhaltern).</summary>
    public string Source { get; set; } = "";

    /// <summary>Geprüfte Übersetzung; <c>null</c> = noch offen.</summary>
    public string? Target { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
