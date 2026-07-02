using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Laufender Offline-Training-Timer eines Users. Maximal EIN Timer je User (Primary Key = UserId).
/// Ein neuer Start ersetzt einen bereits laufenden — der Client entscheidet, ob ein noch laufender
/// erst gestoppt/verworfen werden muss.
///
/// Beim Stoppen (<see cref="Services.TrainingGoalService.StopTimerAsync"/>) wird der Timer in einen
/// <see cref="ManualActivity"/>-Eintrag umgewandelt und aus dieser Tabelle entfernt. Label + Kind
/// werden hier gesnapshotet (statt via FK auf <see cref="ActivityPreset"/>), damit ein Rename/Delete
/// der Vorlage einen laufenden Timer nicht bricht.
/// </summary>
public class ActivityTimer
{
    /// <summary>Primary Key = User; garantiert genau einen Timer pro User ohne extra Unique-Index.</summary>
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    [MaxLength(100)]
    public string Label { get; set; } = string.Empty;

    public ManualActivityKind Kind { get; set; }

    /// <summary>Vom Preset übernommenes Thema (falls gesetzt) — wandert beim Stop 1:1 in den
    /// <see cref="ManualActivity"/>-Eintrag.</summary>
    public ChessableTheme? Theme { get; set; }

    /// <summary>UTC-Zeitpunkt des Timer-Starts. Beim Stoppen wird optional ein Endzeitpunkt vom
    /// User übergeben (Backdate), sonst gilt „jetzt".</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
}
