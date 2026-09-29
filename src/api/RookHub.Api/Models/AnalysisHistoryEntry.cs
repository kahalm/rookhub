using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Eine Analyse des Analysebretts im Verlauf (0.603.0, Wunsch 2026-09-29: „merk dir eine History der letzten 20 Analysen
/// von jedem User — diese sollen auch irgendwo ausgewählt werden können"): Ausgangsstellung, die Zugfolge auf dem Brett,
/// wo man stand, und die mit einem Stern markierten Stellungen. Je Nutzer höchstens
/// <see cref="Services.AnalysisHistoryService.MaxPerUser"/>, die älteste geht zuerst.
/// </summary>
public class AnalysisHistoryEntry
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    [Required, MaxLength(120)]
    public string StartFen { get; set; } = string.Empty;

    /// <summary>Die Zugfolge ab <see cref="StartFen"/> als UCI, durch Leerzeichen getrennt (nachgespielt geprüft).</summary>
    public string Moves { get; set; } = string.Empty;

    public int MoveCount { get; set; }

    /// <summary>Halbzug, an dem man zuletzt stand (0 = Ausgangsstellung).</summary>
    public int Ply { get; set; }

    /// <summary>Aus den Kopfdaten eines geladenen PGN („Weiß – Schwarz"), sonst leer.</summary>
    [MaxLength(200)]
    public string? Title { get; set; }

    /// <summary>Mit Stern markierte Halbzüge (0 = Ausgangsstellung), aufsteigend, durch Komma getrennt.</summary>
    [MaxLength(400)]
    public string? Starred { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
