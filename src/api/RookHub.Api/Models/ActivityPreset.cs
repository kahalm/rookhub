using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Wiederverwendbare Vorlage für eine Offline-Trainings-Aktivität (z.B. „Coaching mit Trainer X",
/// „Buch Y lesen", „Turniervorbereitung"). Vom User selbst gepflegt; per Dashboard-„+"-Knopf schnell
/// mit einem Klick als Timer startbar. Speichert nur den Anzeigenamen + die Kategorie
/// (<see cref="ManualActivityKind"/>) — keine Zeit/Dauer.
///
/// Der Timer erzeugt beim Stoppen einen <see cref="ManualActivity"/>-Eintrag (mit Dauer in Minuten
/// bzw. Partienzahl bei OtbGame), die Vorlage bleibt unverändert und kann erneut gestartet werden.
/// </summary>
public class ActivityPreset
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Anzeigename der Vorlage, z.B. „Coaching mit Trainer X".</summary>
    [MaxLength(100)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Kategorie, in der die spätere Aktivität aggregiert wird. Timer arbeitet nur mit
    /// den Minuten-Arten (<see cref="ManualActivityKind.OfflinePuzzle"/> /
    /// <see cref="ManualActivityKind.OfflineStudy"/> / <see cref="ManualActivityKind.Coaching"/>).
    /// OtbGame ist als Vorlage nicht sinnvoll timer-basiert und wird serverseitig abgelehnt.</summary>
    public ManualActivityKind Kind { get; set; }

    /// <summary>Optionales Thema (Eröffnung/Mittelspiel/Endspiel/Taktik/Sonstiges) — wird beim Start
    /// in den Timer und beim Stop in den <see cref="ManualActivity"/>-Eintrag übernommen.</summary>
    public ChessableTheme? Theme { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
