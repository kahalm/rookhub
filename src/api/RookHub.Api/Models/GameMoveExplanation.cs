using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// „Warum war das ein Fehler?" (0.534.0): ein, zwei Sätze zu EINEM Fehler einer analysierten Partie in EINER Sprache,
/// von einem Sprachmodell auf eigener Hardware aus geprüften Fakten geschrieben (<c>GameMoveExplanationService</c>).
/// Hängt an der ANALYSE, nicht am Betrachter: einmal erzeugt, sehen ihn alle, die die Partie sehen — geschrieben ist er
/// aus der Sicht des Besitzers (<see cref="Viewpoint"/>).
/// </summary>
public class GameMoveExplanation
{
    public int Id { get; set; }

    public int GameAnalysisId { get; set; }
    public GameAnalysis? GameAnalysis { get; set; }

    /// <summary>Halbzug wie <see cref="GameAnalysisPosition.Ply"/> (0 = der erste Zug).</summary>
    public int Ply { get; set; }

    /// <summary>Sprache des Textes (ISO-Kürzel, <c>de</c>, <c>en</c> …).</summary>
    [Required, MaxLength(8)] public string Language { get; set; } = "en";

    /// <summary><c>inaccuracy</c> · <c>mistake</c> · <c>blunder</c> · <c>miss</c> — die Klasse beim Erzeugen.</summary>
    [Required, MaxLength(12)] public string Class { get; set; } = string.Empty;

    /// <summary>Aus wessen Sicht der Text geschrieben ist (0.540.0): <c>white</c> / <c>black</c> = die Seite des
    /// Besitzers, er ist „du" und der andere „dein Gegner"; leer = Seite unbekannt, neutral über Weiß und Schwarz.
    /// Legt der Besitzer später eine andere Seite fest, gilt der Text nicht mehr und wird neu geschrieben.</summary>
    [MaxLength(5)] public string Viewpoint { get; set; } = string.Empty;

    [Required, MaxLength(1200)] public string Text { get; set; } = string.Empty;

    [MaxLength(80)] public string? Model { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
