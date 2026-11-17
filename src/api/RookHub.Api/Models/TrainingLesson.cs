namespace RookHub.Api.Models;

/// <summary>
/// Eine aufgezeichnete Trainingsstunde (Trainingsvideos, privat): ein Video mit Zusammenfassung, besprochenen Partien,
/// Segmenten mit Stellung und Trainerkommentar, Fragen und Notizen. Gehört genau EINEM Konto (<see cref="UserId"/>);
/// sehen darf sie nur dieses Konto und ein Admin — nie öffentlich, nie in Sitemap oder Suche.
/// Eingelesen aus je einer JSON-Datei pro Datum (<c>TrainingVideoImporter</c>). Eine Stunde (Datum) wird in bis zu ZWEI
/// Einträge geteilt (<see cref="Part"/>): <c>private</c> = die eigenen Partien des Kontos, <c>general</c> = Lehrbeispiele
/// und Übungen. Beide zeigen auf dasselbe Video. Ein Re-Import desselben Datums für dasselbe Konto ersetzt die Einträge
/// samt allen Kindzeilen (Schlüssel Konto + Datum + Teil).
/// </summary>
public class TrainingLesson
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Datum der Stunde (zugleich der Schlüssel der Datei, <c>2026-10-27.json</c>).</summary>
    public DateOnly Date { get; set; }

    /// <summary><see cref="TrainingLessonParts.Private"/> (eigene Partien) | <see cref="TrainingLessonParts.General"/>
    /// (Lehrbeispiele, Übungen).</summary>
    public string Part { get; set; } = TrainingLessonParts.Private;

    /// <summary>Dateiname des Videos im Video-Ordner (<c>TrainingVideos:VideoRoot</c>), ohne Pfad; <c>null</c> = kein Video.</summary>
    public string? VideoFile { get; set; }

    public int? DurationSec { get; set; }

    public string? Summary { get; set; }

    /// <summary>Schema-Fassung der eingelesenen Datei (<c>schema</c>).</summary>
    public int Schema { get; set; } = 1;

    public DateTime ImportedAt { get; set; }

    public List<TrainingLessonGame> Games { get; set; } = new();
    public List<TrainingSegment> Segments { get; set; } = new();
    public List<TrainingNote> Notes { get; set; } = new();
}

/// <summary>Die zwei Teile einer Trainerstunde (<see cref="TrainingLesson.Part"/>).</summary>
public static class TrainingLessonParts
{
    /// <summary>Eigene Partien des Kontos (Partie-Art <c>own</c>) mit den Kommentaren des Trainers.</summary>
    public const string Private = "private";

    /// <summary>Lehrbeispiele und Übungen (Partie-Art <c>lesson</c> | <c>exercise</c>).</summary>
    public const string General = "general";

    /// <summary>Teil einer Partie-Art: <c>own</c> → privat, alles andere → allgemein.</summary>
    public static string OfKind(string? kind) => kind == "own" ? Private : General;
}
