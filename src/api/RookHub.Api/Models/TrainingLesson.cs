namespace RookHub.Api.Models;

/// <summary>
/// Eine aufgezeichnete Trainingsstunde (Trainingsvideos, privat): ein Video mit Zusammenfassung, besprochenen Partien,
/// Segmenten mit Stellung und Trainerkommentar, Fragen und Notizen. Gehört genau EINEM Konto (<see cref="UserId"/>);
/// sehen darf sie nur dieses Konto und ein Admin — nie öffentlich, nie in Sitemap oder Suche.
/// Eingelesen aus je einer JSON-Datei pro Datum (<c>TrainingVideoImporter</c>); ein Re-Import desselben Datums für
/// dasselbe Konto ersetzt die Stunde samt allen Kindzeilen.
/// </summary>
public class TrainingLesson
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Datum der Stunde (zugleich der Schlüssel der Datei, <c>2026-10-27.json</c>).</summary>
    public DateOnly Date { get; set; }

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
