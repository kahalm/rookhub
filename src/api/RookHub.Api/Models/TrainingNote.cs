namespace RookHub.Api.Models;

/// <summary>Eine Notiz aus der Trainingsstunde (Schwäche, Eröffnung, Plan …) — Grundlage des späteren Trainer-Notizbuchs.</summary>
public class TrainingNote
{
    public int Id { get; set; }

    public int LessonId { get; set; }
    public TrainingLesson? Lesson { get; set; }

    public int Position { get; set; }
    public int? StartSec { get; set; }
    public string? GameKey { get; set; }

    /// <summary><c>schwaeche</c> | <c>eroeffnung</c> | <c>plan</c> | <c>rechnen</c> | <c>zeit</c> | <c>psychologie</c> | <c>allgemein</c>.</summary>
    public string Category { get; set; } = "allgemein";

    public string Text { get; set; } = string.Empty;
    public string? Quote { get; set; }

    /// <summary>Text + Zitat für den Volltext-Index.</summary>
    public string? SearchText { get; set; }
}
