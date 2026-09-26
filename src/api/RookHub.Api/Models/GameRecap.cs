using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// „Kurz erzählt" (0.541.0): die Partie in zwei, drei Sätzen — für die Link-Vorschau des Teilen-Links (og:description) und
/// oben auf der Partieseite. Geschrieben vom Sprachmodell auf eigener Hardware aus Zugfolge und Bewertungskurve
/// (<c>GameRecapService</c>), nach der Analyse von selbst. Je Partie und Sprache EIN Text; nach der Vertiefung neu.
/// </summary>
public class GameRecap
{
    public int Id { get; set; }

    public int SavedGameId { get; set; }
    public SavedGame? SavedGame { get; set; }

    [Required, MaxLength(8)] public string Language { get; set; } = "en";

    [Required, MaxLength(1000)] public string Text { get; set; } = string.Empty;

    [MaxLength(80)] public string? Model { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
