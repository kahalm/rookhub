using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Eine Analyse des Analysebretts im Verlauf (0.603.0, Wunsch 2026-09-29: „merk dir eine History der letzten 20 Analysen
/// von jedem User — diese sollen auch irgendwo ausgewählt werden können"): Ausgangsstellung, der Zugbaum samt Varianten
/// und Sternen (0.604.0), wo man stand. Je Nutzer höchstens <see cref="Services.AnalysisHistoryService.MaxPerUser"/>,
/// die älteste geht zuerst.
/// </summary>
public class AnalysisHistoryEntry
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    [Required, MaxLength(120)]
    public string StartFen { get; set; } = string.Empty;

    /// <summary>Die HAUPTLINIE ab <see cref="StartFen"/> als UCI, durch Leerzeichen getrennt — für Vorschau und das
    /// Wiederfinden derselben Partie. Der ganze Baum steht in <see cref="TreeJson"/>.</summary>
    public string Moves { get; set; } = string.Empty;

    /// <summary>Länge der Hauptlinie.</summary>
    public int MoveCount { get; set; }

    /// <summary>Zugbaum in der flachen Form von <see cref="DTOs.AnalysisTreeDto"/> (geprüft, JSON); <c>null</c> bei
    /// Einträgen von 0.603.0 — dort ist <see cref="Moves"/> die ganze Analyse.</summary>
    public string? TreeJson { get; set; }

    /// <summary>Index des Knotens, an dem man zuletzt stand (-1 = Ausgangsstellung).</summary>
    public int Current { get; set; } = -1;

    /// <summary>Tiefe dieses Knotens (0 = Ausgangsstellung).</summary>
    public int Ply { get; set; }

    /// <summary>Alle Züge des Baums, Varianten eingeschlossen.</summary>
    public int NodeCount { get; set; }

    /// <summary>Mit Stern markierte Stellungen (die Ausgangsstellung eingeschlossen).</summary>
    public int StarCount { get; set; }

    /// <summary>Aus den Kopfdaten eines geladenen PGN („Weiß – Schwarz"), sonst leer.</summary>
    [MaxLength(200)]
    public string? Title { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
