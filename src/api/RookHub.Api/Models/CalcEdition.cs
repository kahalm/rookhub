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
}
