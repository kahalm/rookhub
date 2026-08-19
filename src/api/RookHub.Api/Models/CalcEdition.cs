using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Eine „Ausgabe" einer Kalkulations-Serie (z. B. Noel): terminiert EIN Wochen-Kapitel eines
/// Kalkulationsbuchs. Der Autor legt die Stellungen (als Calc-Positionen des Kapitels) vorab an; die
/// Ausgabe steuert, WANN das Kapitel sichtbar wird (<see cref="PublishAt"/> für die Liste,
/// <see cref="TesterPreviewAt"/> früher für Tester — Phase 2) und trägt das Video. Kapitel OHNE Ausgabe
/// bleiben ungegatet (sanfter Übergang). Cascade mit dem Buch.
/// </summary>
public class CalcEdition
{
    public int Id { get; set; }

    public int BookId { get; set; }
    public Book? Book { get; set; }

    /// <summary>Kapitelname (= Wochen-Kapitel, exakt wie in <see cref="BookPuzzle.Chapter"/>). Eine Ausgabe je Kapitel.</summary>
    [Required, MaxLength(300)]
    public string Chapter { get; set; } = string.Empty;

    /// <summary>Optionaler Titel (sonst zeigt die UI den Kapitelnamen).</summary>
    [MaxLength(300)]
    public string? Title { get; set; }

    /// <summary>YouTube-/Video-Link zur Ausgabe.</summary>
    [MaxLength(500)]
    public string? VideoUrl { get; set; }

    /// <summary>Öffentliche Freigabe: ab hier ist das Kapitel sichtbar. Davor Entwurf/versteckt.</summary>
    public DateTime PublishAt { get; set; }

    /// <summary>Optionaler FRÜHERER Tester-Termin (Phase 2): ab hier sehen als Tester markierte Mitglieder
    /// das Kapitel. Gedacht als Vorschau vor <see cref="PublishAt"/>.</summary>
    public DateTime? TesterPreviewAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Idempotenz-Marke (Phase 3b): gesetzt, sobald die ÖFFENTLICHE Freigabe (<see cref="PublishAt"/>)
    /// an den Verteiler angekündigt wurde. Verhindert Doppel-Benachrichtigungen.</summary>
    public DateTime? PublishAnnouncedAt { get; set; }

    /// <summary>Idempotenz-Marke (Phase 3b): gesetzt, sobald die TESTER-Vorschau (<see cref="TesterPreviewAt"/>)
    /// an die Tester angekündigt wurde.</summary>
    public DateTime? TesterAnnouncedAt { get; set; }

    /// <summary>Wer in der TESTER-Runde bereits benachrichtigt wurde (CSV der UserIds, wie
    /// <c>NotificationPushSetting.EnabledCategories</c>). Die öffentliche Runde schließt GENAU diese
    /// Empfänger aus — nicht anhand des (veränderlichen) <c>IsTester</c>-Flags: sonst würde ein nach der
    /// Tester-Runde hinzugefügter Tester keine Benachrichtigung bekommen, und ein zwischenzeitlich
    /// ent-Tester-tes Mitglied zweimal. Leer/nur „" = Tester-Runde ohne Empfänger.</summary>
    [MaxLength(4000)]
    public string? TesterAnnouncedUserIds { get; set; }
}
