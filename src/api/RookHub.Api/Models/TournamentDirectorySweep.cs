using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Buchfuehrung je Foederation. Zwei Aufgaben: die Wochenrotation waehlt daraus die am laengsten
/// nicht besuchten Foederationen aus, und der Admin sieht, welche Sweeps zuletzt gescheitert sind.
/// Der Schluessel ist der Foederationscode - es gibt genau eine Zeile pro Foederation.
/// </summary>
public class TournamentDirectorySweep
{
    [Key, MaxLength(3)]
    public string Federation { get; set; } = string.Empty;

    /// <summary>
    /// Nur bei einem ERFOLGREICHEN Lauf gesetzt. Ein Fehlschlag laesst den Zeitstempel alt, damit
    /// die Rotation die Foederation gleich wieder vornimmt - und damit die Verschwunden-Erkennung
    /// nicht auf einer halben Trefferliste aufsetzt.
    /// </summary>
    public DateTime? LastSweptAt { get; set; }

    public DateTime? LastAttemptedAt { get; set; }

    public int LastRowCount { get; set; }

    /// <summary>Fehlertext des letzten Fehlschlags; null nach einem erfolgreichen Lauf.</summary>
    [MaxLength(500)]
    public string? LastError { get; set; }

    public int ConsecutiveFailures { get; set; }
}
