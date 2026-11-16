namespace RookHub.Api.Services.TrainingVideos;

/// <summary>
/// Konfiguration der Trainingsvideos (Abschnitt <c>TrainingVideos</c>, ENV <c>TrainingVideos__VideoRoot</c> usw.).
/// Beide Ordner sind nur lesend eingehängt; ohne <see cref="VideoRoot"/> antwortet das Video 404 (Kommentare, Suche und
/// Stundenliste laufen trotzdem), ohne <see cref="DataRoot"/> gibt es nur den Import per Anfrage-Rumpf.
/// </summary>
public class TrainingVideoOptions
{
    public const string Section = "TrainingVideos";

    /// <summary>Ordner der mp4-Dateien (<c>&lt;datum&gt;.mp4</c>, H.264 mit faststart), z. B. <c>/app/trainingvideos</c>.</summary>
    public string? VideoRoot { get; set; }

    /// <summary>Ordner der aufbereiteten JSON-Dateien (<c>&lt;datum&gt;.json</c>) für den Ordner-Import des Admins.</summary>
    public string? DataRoot { get; set; }
}
