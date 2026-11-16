namespace RookHub.Api.Models;

/// <summary>
/// Stand eines Kontos an einer Stellung der Trainerstunden („Nachspielen“ und stundenübergreifender Fragen-Satz):
/// zuletzt gelöst, falsch oder übersprungen — Grundlage für „nur Falsche wiederholen“. Eine Zeile je Konto und Stellung.
/// Bewusst NICHT an der Segment-Id: ein Re-Import ersetzt die Segmente, der Fortschritt soll bleiben. Der Schlüssel ist
/// die Stunde (stabile Id) + <see cref="SegmentKey"/> (Startzeit und Figurenfeld, <c>TrainingVideoService.SegmentKey</c>).
/// </summary>
public class TrainingSegmentProgress
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int LessonId { get; set; }
    public TrainingLesson? Lesson { get; set; }

    /// <summary><c>&lt;startSec&gt;|&lt;figurenfeld&gt;</c>.</summary>
    public string SegmentKey { get; set; } = string.Empty;

    /// <summary><c>solved</c> | <c>wrong</c> | <c>skipped</c> (letzter Versuch).</summary>
    public string Status { get; set; } = "skipped";

    /// <summary>Fehlversuche beim letzten Mal (0 = auf Anhieb).</summary>
    public int Attempts { get; set; }

    /// <summary>Wie oft die Stellung insgesamt gespielt wurde.</summary>
    public int Plays { get; set; }

    public DateTime UpdatedAt { get; set; }
}
