using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>Stand der Übersetzung EINES Buchs in EINE Zielsprache (0.876.0).</summary>
public enum EbookTranslationState
{
    /// <summary>Wartet (auch: angefangen und unterbrochen — der Zwischenspeicher hält das Fertige, der nächste Lauf macht weiter).</summary>
    Pending = 0,
    /// <summary>Läuft gerade (beim Start wieder auf Pending, falls der Dienst mitten im Lauf stand).</summary>
    Running = 1,
    /// <summary>Fertig: <see cref="EbookTranslation.RelativePath"/> ist das übersetzte EPUB.</summary>
    Done = 2,
    /// <summary>Gescheitert (EPUB unlesbar, Umwandlung schlug fehl …) — „Fehlgeschlagene wiederholen“ stellt zurück.</summary>
    Failed = 3,
    /// <summary>Kein brauchbarer Text (Scan ohne Textebene) — Stufe 2 (OCR-Ebene) holt das später nach.</summary>
    NoText = 4,
}

/// <summary>
/// Übersetzung eines E-Books in eine Zielsprache (0.876.0, Plan „Bücher auf Englisch“): ein ABGELEITETES EPUB
/// <c>converted/&lt;id&gt;.&lt;lang&gt;.epub</c> unter <c>Ebooks:Root</c> — eine Kopie des Quell-EPUB, in der NUR der Text
/// übersetzt ist (Archiv, Dateinamen, Bilder, CSS, ids gleich; Diagramm-Verweise <see cref="EbookDiagram.ImageRef"/> gelten
/// weiter). Bewusst KEINE <see cref="EbookFile"/>-Zeile: an „Format epub + Derived“ hängen Leseformat, Fließtext-EPUB,
/// Diagramm-Verknüpfung und Fingerabdrücke — eine Übersetzung dort wäre überall ein falscher Treffer.
///
/// <para>Der Fortschritt eines Laufs liegt NICHT hier, sondern im Zwischenspeicher neben der Datei
/// (<c>converted/&lt;id&gt;.&lt;lang&gt;.cache.jsonl</c>, je Text eine Zeile) — eine Unterbrechung um 8:00 macht am Abend dort
/// weiter; ändert sich die Quelle (<see cref="SourceHash"/>), wird nur der geänderte Text neu übersetzt.</para>
/// </summary>
public class EbookTranslation
{
    public int Id { get; set; }

    public int EbookId { get; set; }
    public Ebook? Ebook { get; set; }

    /// <summary>Zielsprache (ISO 639-1, heute nur <c>en</c>).</summary>
    [Required, MaxLength(8)]
    public string Language { get; set; } = "en";

    public EbookTranslationState State { get; set; } = EbookTranslationState.Pending;

    /// <summary>Von Hand vorgezogen (Admin „Jetzt übersetzen“) — kommt vor der Reihenfolge nach Sprache dran.</summary>
    public bool Priority { get; set; }

    /// <summary>Nicht vor diesem Zeitpunkt wieder versuchen (z. B. Text-PDF, dessen Diagramme noch nicht erkannt sind —
    /// erst danach entsteht das Fließtext-EPUB). <c>null</c> = sofort.</summary>
    public DateTime? NotBefore { get; set; }

    /// <summary>Quell-EPUB (<see cref="EbookFile.Id"/>) des letzten Laufs.</summary>
    public int? SourceFileId { get; set; }

    /// <summary>SHA-256 der Text-Dateien des Quell-EPUB (OPF, Kapitel, Navigation) — weicht er ab, ist die Übersetzung veraltet.</summary>
    [MaxLength(64)]
    public string? SourceHash { get; set; }

    /// <summary>Pfad des übersetzten EPUB relativ zu <c>Ebooks:Root</c> (nur bei <see cref="EbookTranslationState.Done"/>).</summary>
    [MaxLength(300)]
    public string? RelativePath { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Verschiedene Texte des Buchs (jeder geht nur EINMAL ans Modell) und davon schon übersetzte.</summary>
    public int SegmentsTotal { get; set; }
    public int SegmentsDone { get; set; }

    /// <summary>Texte, die auch im zweiten Versuch nicht brauchbar zurückkamen — stehen im EPUB im Original.</summary>
    public int SegmentsFailed { get; set; }

    /// <summary>Womit übersetzt wurde (Modellname der Spark).</summary>
    [MaxLength(100)]
    public string? Model { get; set; }

    [MaxLength(500)]
    public string? Error { get; set; }

    /// <summary>Läufe bisher (jede Unterbrechung zählt mit).</summary>
    public int Runs { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Wann zuletzt geprüft wurde, ob die Quelle noch dieselbe ist.</summary>
    public DateTime? VerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
