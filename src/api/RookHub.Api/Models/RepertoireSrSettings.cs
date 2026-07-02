using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Globale (per-User) Standard-Intervalle der 9-Stufen-Spaced-Repetition-Leiter für den
/// Repertoire-Trainer. Pro Repertoire kann über <see cref="Repertoire.SrIntervalsJson"/>
/// übersteuert werden; fehlt beides, gelten die eingebauten Defaults
/// (<see cref="Services.RepertoireTrainingService.DefaultLevelsJson"/>).
/// </summary>
public class RepertoireSrSettings
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>JSON-Array mit genau 9 Einträgen `{ "value": number, "unit": "h|d|w|mo" }`.</summary>
    [Required]
    public string IntervalsJson { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
