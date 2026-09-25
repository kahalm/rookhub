using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// „Warum war das ein Fehler?" (0.534.0): ein, zwei Sätze zu EINEM Fehler einer analysierten Partie in EINER Sprache,
/// von einem Sprachmodell auf eigener Hardware aus geprüften Fakten geschrieben (<c>GameMoveExplanationService</c>).
/// Hängt an der ANALYSE, nicht am Betrachter: einmal erzeugt, sehen ihn alle, die die Partie sehen.
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

    [Required, MaxLength(1200)] public string Text { get; set; } = string.Empty;

    [MaxLength(80)] public string? Model { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
