using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RookHub.Api.Models;

/// <summary>
/// Ein „Wochenpost": ein hochgeladenes PGN mit geplantem Termin (Datum + Uhrzeit).
/// Bildet die wöchentlichen Posts des schach-bots auf RookHub ab. Öffentlich sichtbar,
/// Verwaltung (Upload/Bearbeiten/Löschen) nur durch Admins.
/// </summary>
public class WeeklyPost
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Optionale kurze Beschreibung (im Frontend angezeigt + vom schach-bot in Discord gepostet).</summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    [Required, MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Column(TypeName = "LONGTEXT")]
    public string PgnContent { get; set; } = string.Empty;

    public long FileSize { get; set; }

    /// <summary>
    /// Optionale Quelle: ist dies gesetzt, spiegelt der Wochenpost EIN Kapitel eines Buchs (Kurs) statt
    /// eines hochgeladenen PGN. Die Puzzle-Sequenz wird dann live aus den <see cref="BookPuzzle"/> dieses
    /// Buchs/Kapitels gebaut (statt <see cref="PgnContent"/> zu parsen), <c>PgnContent</c> bleibt leer.
    /// Bewusst KEIN FK (wie <c>CourseLink.LinkedBookId</c>): wird das Buch gelöscht, zeigt der Post einfach
    /// keine Puzzles mehr — der Wochenpost selbst soll nicht mit-kaskadiert gelöscht werden.
    /// </summary>
    public int? SourceBookId { get; set; }

    /// <summary>Kapitelname der Buch-Quelle (normalisiert wie im Kurs; <c>null</c> = Sammel-„ohne Kapitel").
    /// Nur relevant, wenn <see cref="SourceBookId"/> gesetzt ist.</summary>
    [MaxLength(200)]
    public string? SourceChapter { get; set; }

    /// <summary>
    /// Anzahl der Puzzles im PGN, beim Upload einmal berechnet und gecacht. Der PGN-Inhalt ist nach
    /// dem Anlegen unveränderlich (Update ändert nur Titel/Termin), daher genügt einmaliges Setzen.
    /// 0 = noch nicht gesetzt (Alt-Datensatz) → wird beim ersten Zugriff lazy nachgezogen. Spart den
    /// teuren LONGTEXT-Parse auf den Lese-/Aufzeichnungspfaden (Progress/Results/Attempt).
    /// </summary>
    public int PuzzleCount { get; set; }

    /// <summary>Geplanter Termin (lokale Wall-Clock-Zeit, wie vom Admin gewählt; Standard-Uhrzeit 19:00).</summary>
    public DateTime ScheduledAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
