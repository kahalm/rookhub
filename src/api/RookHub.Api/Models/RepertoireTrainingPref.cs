using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Trainings-Einstellungen EINES Nutzers für EIN Repertoire (0.996.0) — heute nur die Reihenfolge der Linien im
/// Trainer. Je (Nutzer, Repertoire) höchstens eine Zeile; keine Zeile = nichts gewählt (es gilt die Vorgabe).
/// Besitzer UND Freigabe-Empfänger haben je ihre eigene (wie beim SR-Fortschritt): wer ein geteiltes fremdes
/// Repertoire trainiert, stellt seine Reihenfolge selbst ein. Serverseitig, damit sie über Geräte hinweg gilt.
/// </summary>
public class RepertoireTrainingPref
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int RepertoireId { get; set; }
    public Repertoire? Repertoire { get; set; }

    /// <summary>Einer der Werte aus <see cref="Services.Repertoires.RepertoireLineOrders.All"/>; <c>""</c> = nichts gewählt
    /// (die Zeile entstand nur für die Kapitelfarben).</summary>
    [Required, MaxLength(20)]
    public string LineOrder { get; set; } = string.Empty;

    /// <summary>Eigene Trainingsfarbe je Kapitel als JSON (<c>{ "Kapitel": "w" | "b" }</c>, 1.020.2) — vorher nur im
    /// localStorage (<c>rookhub_rep_train_chaptercolor_&lt;id&gt;</c>), beim Abmelden geräumt.</summary>
    public string? ChapterColorsJson { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
