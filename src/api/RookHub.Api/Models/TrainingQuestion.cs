namespace RookHub.Api.Models;

/// <summary>Eine Frage des Trainers an einem Segment („Was spielst du hier?“) — Grundlage des späteren Fragen-Übungssatzes.</summary>
public class TrainingQuestion
{
    public int Id { get; set; }

    public int SegmentId { get; set; }
    public TrainingSegment? Segment { get; set; }

    public string Prompt { get; set; } = string.Empty;

    /// <summary>Erwartete Antwort in SAN.</summary>
    public string? Answer { get; set; }
    public string? AnswerLine { get; set; }
    public int? AnswerSec { get; set; }
    public string? Explanation { get; set; }
}
