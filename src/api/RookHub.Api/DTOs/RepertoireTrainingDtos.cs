using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>SM-2-Zustand einer Repertoire-Trainingskarte (an das Frontend geliefert).</summary>
public record RepertoireCardStateDto(
    string CardKey,
    string ExpectedMove,
    int Reps,
    int Lapses,
    double IntervalDays,
    double Ease,
    DateTime DueAt,
    DateTime? LastReviewedAt);

/// <summary>Bewertung einer Karte nach einem Versuch. Grade: 0 = again (falsch/relearn),
/// 1 = hard (geduldeter Alternativzug / mühsam), 2 = good (richtig), 3 = easy.</summary>
public class ReviewCardRequest
{
    [Required, MaxLength(120)]
    public string CardKey { get; set; } = string.Empty;

    [MaxLength(16)]
    public string ExpectedMove { get; set; } = string.Empty;

    [Range(0, 3)]
    public int Grade { get; set; }
}
