using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Spaced-Repetition-Zustand EINER Repertoire-Trainings-LINIE für einen User (seit v0.245: die
/// SR-Einheit ist die ganze PGN-Linie, nicht mehr die Einzelstellung). Identität ist die
/// <see cref="CardKey"/> = stabiler Linien-Schlüssel (Hash der Zugfolge, vom Frontend berechnet)
/// je (User, Repertoire). Scheduling = feste <see cref="Level"/>-Leiter (1–9) mit pro Stufe
/// konfigurierbaren Intervallen; richtig = +1 Stufe, falsch (irgendwo in der Linie) = zurück auf
/// Stufe 1. Die Baum-/Zuglogik liegt im Frontend (chess.js).
/// </summary>
public class RepertoireCardState
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int RepertoireId { get; set; }
    public Repertoire? Repertoire { get; set; }

    /// <summary>Stabiler Linien-Schlüssel (Hash der normalisierten Zugfolge der ganzen Linie, vom
    /// Frontend berechnet) je (User, Repertoire).</summary>
    [Required, MaxLength(120)]
    public string CardKey { get; set; } = string.Empty;

    /// <summary>Kurzes Linien-Label (z.B. der White-Header) — nur für Anzeige/Debug.</summary>
    [MaxLength(120)]
    public string ExpectedMove { get; set; } = string.Empty;

    /// <summary>Aktuelle Stufe der 9-Stufen-Leiter (1–9). 0 = im Pool, aber noch nie abgefragt.</summary>
    public int Level { get; set; }

    /// <summary>Ob die Linie im Übungspool ist (durch „Learn"/„In Pool aufnehmen" aktiviert). Noch
    /// nicht gelernte Linien haben KEINE Zeile bzw. InPool=false und werden nicht abgefragt.</summary>
    public bool InPool { get; set; }

    /// <summary>Pausiert = fällt NICHT in den Übungspool (unabhängig von <see cref="DueAt"/>), bis
    /// der User die Linie/das Kapitel wieder aktiviert.</summary>
    public bool Paused { get; set; }

    /// <summary>Anzahl korrekt gelöster Wiederholungen (Statistik).</summary>
    public int Reps { get; set; }

    /// <summary>Anzahl Fehlversuche insgesamt (Statistik).</summary>
    public int Lapses { get; set; }

    // Vestigial (aus der SM-2-Ära): bleiben in der DB, werden vom Level-Scheduler nicht genutzt.
    public double IntervalDays { get; set; }
    public double Ease { get; set; } = 2.5;

    public DateTime DueAt { get; set; }
    public DateTime? LastReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
