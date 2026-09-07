using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// EIN Spielort eines Turniers.
///
/// <para><b>Warum eine eigene Tabelle:</b> chess-results fuehrt den Spielort als Freitext, und bei
/// Ligen stehen dort mehrere — „Mayrhofen, St.Veit", „Leoben; Klagenfurt; St. Veit; Graz",
/// „Bad Haering/Schwaz/Jenbach/Absam/Kufstein". Ein einzelnes Koordinatenpaar am Eintrag kann das
/// nicht abbilden: es setzte den Pin auf EINEN der Orte und liess die uebrigen verschwinden — und
/// die Umkreissuche fand ein Turnier nicht, das zur Haelfte vor der Haustuer stattfindet. Gemessen
/// auf dem Dev-Stand: 270 Eintraege nennen mehrere Orte.</para>
///
/// <para>Die Koordinaten am Eintrag selbst bleiben als HAUPT-Spielort bestehen (der erste) — die
/// Kalender- und Detailansicht braucht einen einzelnen Bezugspunkt, und der Bounding-Box-Vorfilter
/// der Umkreissuche laeuft weiterhin ueber den Index auf dem Eintrag.</para>
/// </summary>
public class TournamentDirectoryVenue
{
    public int Id { get; set; }

    public int TournamentDirectoryEntryId { get; set; }
    public TournamentDirectoryEntry? Entry { get; set; }

    /// <summary>Reihenfolge im Ortstext — 0 ist der Haupt-Spielort.</summary>
    public int Ordinal { get; set; }

    /// <summary>Der aufgeloeste Ortsname (aus dem Gazetteer), nicht der Rohtext.</summary>
    [MaxLength(200)]
    public string Name { get; set; } = "";

    /// <summary>Der Abschnitt des Ortstexts, aus dem dieser Ort kommt — fuer die Nachvollziehbarkeit.</summary>
    [MaxLength(300)]
    public string? SourceText { get; set; }

    public double Lat { get; set; }
    public double Lon { get; set; }

    public GeoSource GeoSource { get; set; }
}
