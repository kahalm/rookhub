using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Wann der naechtliche Verlaufs-Durchgang ein Turnier aus einem Verlauf zuletzt beim Crawler
/// angefordert hat.
///
/// <para><b>Warum es das braucht.</b> Ein Turnier aus dem Verlauf hatte bis hierher nur seine
/// Zeile (Platz, Punkte, Performance) — Teilnehmer und Paarungen holte erst ein Klick, und der
/// wartete dann bis zu zwei Minuten. Jetzt holt der Durchgang sie im Voraus. Ohne diese Zeile
/// wuerde ein Turnier, dessen Abruf scheitert (von chess-results entfernt, gesperrt), jede Nacht
/// wieder angefordert und verbraucht den Deckel fuer die, die gelingen wuerden.</para>
/// </summary>
public class HistoryTournamentCrawl
{
    /// <summary>chess-results-Turniernummer.</summary>
    [MaxLength(20)]
    public string ChessResultsId { get; set; } = string.Empty;

    public DateTime LastRequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Wie oft angefordert, OHNE dass das Turnier danach beim Crawler stand.</summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Seit wann das Turnier beim Crawler steht. Gesetzt heisst: nicht mehr nachfragen — ausser es
    /// laeuft gerade, dann holt der Durchgang die neuen Runden.
    /// </summary>
    public DateTime? FoundAt { get; set; }
}
