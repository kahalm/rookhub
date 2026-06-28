using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Spaced-Repetition-Zustand EINER Repertoire-Trainingskarte für einen User. Eine Karte =
/// eine Stellung, in der der trainierte Spieler am Zug ist, samt erwartetem Repertoirezug.
/// Identität ist die <see cref="CardKey"/> (normalisierte Stellungs-FEN OHNE Zugzähler, vom
/// Frontend berechnet) je (User, Repertoire). Die Baum-/Zuglogik liegt im Frontend (chess.js);
/// das Backend persistiert nur den SM-2-Scheduling-Zustand.
/// </summary>
public class RepertoireCardState
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int RepertoireId { get; set; }
    public Repertoire? Repertoire { get; set; }

    /// <summary>Stabiler Stellungs-Schlüssel = FEN VOR dem erwarteten Zug, ohne Halbzug-/Zugzähler
    /// (4 Felder), damit Transpositionen dieselbe Karte teilen.</summary>
    [Required, MaxLength(120)]
    public string CardKey { get; set; } = string.Empty;

    /// <summary>Erwarteter Repertoirezug (SAN) — für Anzeige/Feedback; bei Repertoire-Änderung aktualisiert.</summary>
    [MaxLength(16)]
    public string ExpectedMove { get; set; } = string.Empty;

    /// <summary>Erfolgreiche Wiederholungen in Folge (SM-2).</summary>
    public int Reps { get; set; }

    /// <summary>Anzahl Fehlversuche/„again" insgesamt.</summary>
    public int Lapses { get; set; }

    /// <summary>Aktuelles Intervall in Tagen (kann &lt;1 sein für Relearn).</summary>
    public double IntervalDays { get; set; }

    /// <summary>SM-2-Leichtigkeitsfaktor (Default 2.5, geklemmt [1.3, 3.0]).</summary>
    public double Ease { get; set; } = 2.5;

    public DateTime DueAt { get; set; }
    public DateTime? LastReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
