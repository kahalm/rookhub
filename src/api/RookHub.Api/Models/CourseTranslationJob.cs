using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Ein Auftrag „diesen Kurs in diese Sprache uebersetzen" (Plan „Kurs-Kommentare mehrsprachig",
/// Abschnitt 1). Die Tabelle entsteht mit dem Datenmodell (0.547.0); Anlegen, Warteschlange und
/// Hintergrunddienst folgen in einem eigenen Schritt — bis dahin laeuft eine Kurs-Uebersetzung nur von
/// Hand ueber <c>tools/LibraryImport translate --course</c>.
///
/// <para><b>„Ein offener Auftrag je (Kurs, Sprache)" und „einer je Nutzer"</b> erzwingt der Dienst, nicht
/// die Datenbank: MariaDB kennt keinen gefilterten eindeutigen Index. Rennfest genug, weil ein doppelter
/// Auftrag nur doppelt PRUEFT und nicht doppelt uebersetzt — die Arbeit selbst ist inkrementell
/// (<see cref="CommentText.SourceHash"/>).</para>
///
/// <para><see cref="RequestedByUserId"/> traegt bewusst KEINEN Fremdschluessel: ein geloeschtes Konto soll
/// weder das Loeschen blockieren (Restrict) noch aus seinem Auftrag stillschweigend einen Automatik-Auftrag
/// machen (SetNull hiesse „null = Automatik"). Wer Konten loescht, raeumt offene Auftraege selbst ab.</para>
/// </summary>
public class CourseTranslationJob
{
    public int Id { get; set; }

    public int BookId { get; set; }
    public Book? Book { get; set; }

    /// <summary>Zielsprache (ISO-Kuerzel wie <see cref="CommentSet.Language"/>).</summary>
    [Required, MaxLength(8)]
    public string Language { get; set; } = string.Empty;

    /// <summary>Wer den Auftrag angefordert hat; <c>null</c> = Automatik (de/en fuer alle Kurse).</summary>
    public int? RequestedByUserId { get; set; }

    public CourseTranslationJobStatus Status { get; set; } = CourseTranslationJobStatus.Queued;

    public int LinesTotal { get; set; }
    public int LinesDone { get; set; }
    public int LinesFailed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    [MaxLength(500)]
    public string? LastError { get; set; }
}

/// <summary>Zustand eines <see cref="CourseTranslationJob"/>. Die Zahlen sind Teil des Vertrags (Spalte
/// und spaeter die Abfrage des Bibliothekslaufs: „offen" = 0 oder 1).</summary>
public enum CourseTranslationJobStatus
{
    Queued = 0,
    Running = 1,
    Done = 2,
    Failed = 3,
    Cancelled = 4,
}
