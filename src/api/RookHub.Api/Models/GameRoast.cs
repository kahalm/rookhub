using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// „Roast my game" (0.535.0): ein frecher Kommentar zur EIGENEN Partie in einem Stil und einer Sprache, geschrieben vom
/// Sprachmodell auf eigener Hardware (<c>GameRoastService</c>). Je Partie, Sprache und Stil gilt der zuletzt
/// gewürfelte — „Neu würfeln" ersetzt ihn.
/// </summary>
public class GameRoast
{
    public int Id { get; set; }

    public int SavedGameId { get; set; }
    public SavedGame? SavedGame { get; set; }

    /// <summary><c>friendly</c> · <c>cheeky</c> · <c>russian</c>.</summary>
    [Required, MaxLength(12)] public string Style { get; set; } = "friendly";

    [Required, MaxLength(8)] public string Language { get; set; } = "en";

    [Required, MaxLength(2000)] public string Text { get; set; } = string.Empty;

    [MaxLength(80)] public string? Model { get; set; }

    /// <summary>Nach der Analyse von selbst geschrieben (0.540.0, <c>GameReviewTexts</c>) — zählt nicht gegen den
    /// Tagesdeckel des Würfelns; „Neu würfeln" macht daraus einen gewürfelten.</summary>
    public bool Automatic { get; set; }

    /// <summary>Vom Besitzer zum öffentlichen Teilen freigegeben (0.742.0): der Teilen-Link der Partie mit <c>?roast={Id}</c>
    /// zeigt dann diesen Text (Seite und Link-Vorschau). Neu würfeln nimmt die Freigabe zurück — ein geteilter Link soll nie
    /// still einen anderen Text zeigen.</summary>
    public DateTime? SharedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
