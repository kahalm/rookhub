namespace RookHub.Api.Models;

/// <summary>
/// Ein Spieltermin eines Turniers — eine Runde mit ihrem Datum.
///
/// <para><b>Warum es diese Tabelle gibt.</b> Start- und Enddatum sagen bei einer Liga NICHT, wann
/// gespielt wird: „26.09.2026 bis 17.04.2027" sind elf Runden mit zwei bis fuenf Wochen Abstand.
/// Der Kalender zeichnet ein mehrtaegiges Turnier aber an JEDEM Tag zwischen Start und Ende — die
/// Liga stand damit an rund 200 Tagen, an denen nichts stattfindet, und verdeckte die Turniere,
/// die es wirklich gibt. Am Dev-Stand gemessen: 610 offene Eintraege laufen laenger als acht
/// Tage, 527 davon ueber 40 Tage.</para>
///
/// <para>Gefuellt vom <c>TournamentRoundPlanService</c> aus der chess-results-Ansicht art=14 —
/// EIN Seitenabruf je Turnier, deshalb gedeckelt und nur fuer die langlaufenden Eintraege. Hat
/// ein Eintrag keine Zeilen hier, gilt im Kalender wie bisher der ganze Zeitraum; bei einem
/// Wochenend-Open ist das auch richtig.</para>
/// </summary>
public class TournamentDirectoryRound
{
    public int Id { get; set; }

    public int TournamentDirectoryEntryId { get; set; }
    public TournamentDirectoryEntry Entry { get; set; } = null!;

    /// <summary>Rundennummer, wie chess-results sie fuehrt (1-basiert).</summary>
    public int Number { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>
    /// Uhrzeit als ROHTEXT („14:00 Uhr"). Fuer den Kalender zaehlt der Tag; eine halb geparste
    /// Uhrzeit waere nur eine Fehlerquelle, und die Anzeige will den Text ohnehin so.
    /// </summary>
    [System.ComponentModel.DataAnnotations.MaxLength(40)]
    public string? TimeText { get; set; }
}
