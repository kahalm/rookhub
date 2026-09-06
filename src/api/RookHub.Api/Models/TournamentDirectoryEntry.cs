using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Woher die Koordinaten eines Verzeichniseintrags stammen. Steht mit in der Zeile, damit der
/// Admin-Report die Qualitaet sichtbar machen kann: eine Postleitzahl trifft den Ort, ein
/// Bundesland-Zentroid liegt gerne 80 km daneben und darf eine Umkreissuche nicht wie ein
/// exakter Treffer aussehen.
/// </summary>
public enum GeoSource
{
    None = 0,
    PostalCode = 1,
    City = 2,
    Region = 3,
    Manual = 4,
    Nominatim = 5,
}

/// <summary>
/// Ein Turnier aus der chess-results-Turniersuche. Gefuellt vom naechtlichen Sweep, NICHT vom
/// Einzelturnier-Crawl: hier steht nur, DASS und WO ein Turnier stattfindet. Die Teilnehmer- und
/// Paarungsdaten liegen weiterhin in der Crawler-DB und werden erst geholt, wenn jemand das
/// Turnier importiert oder abonniert.
/// </summary>
public class TournamentDirectoryEntry
{
    public int Id { get; set; }

    /// <summary>chess-results-dbkey, identisch mit der ID in tnr&lt;id&gt;.aspx.</summary>
    [Required, MaxLength(20)]
    public string ChessResultsId { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Name { get; set; } = string.Empty;

    /// <summary>FIDE-Foederationscode (AUT, GER, ...) - zugleich der Sweep-Schluessel.</summary>
    [MaxLength(3)]
    public string? Federation { get; set; }

    /// <summary>Bundesland/Region, wie chess-results sie meldet. Fallback fuers Geocoding.</summary>
    [MaxLength(100)]
    public string? State { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// Beginnt das Turnier an einem Samstag oder Sonntag? Beim Schreiben ausgerechnet, weil
    /// <c>DateOnly.DayOfWeek</c> vom MySQL-Provider nicht verlaesslich uebersetzt wird - der Filter
    /// muss aber in SQL laufen, sonst zerbricht die Seitennavigation daran.
    /// </summary>
    public bool StartsOnWeekend { get; set; }

    /// <summary>Spielort als Freitext ("Rifer Hauptstrasse 37 5400 Hallein"). Quelle des Geocodings.</summary>
    [MaxLength(500)]
    public string? LocationText { get; set; }

    [MaxLength(300)]
    public string? TimeControlText { get; set; }

    /// <summary>Aus <see cref="TimeControlText"/> geraten - chess-results liefert keine Kategorie.</summary>
    public TournamentSpeed Speed { get; set; } = TournamentSpeed.Unknown;

    [MaxLength(300)]
    public string? Organizer { get; set; }

    [MaxLength(300)]
    public string? Director { get; set; }

    [MaxLength(300)]
    public string? ChiefArbiter { get; set; }

    public int? Rounds { get; set; }
    public int? PlayerCount { get; set; }

    /// <summary>
    /// Naeherung aus der relativen "Last update"-Angabe. Grob gerundet - taugt zum Sortieren
    /// ("zuletzt geaendert"), nicht als exakter Zeitstempel und nicht zur Aenderungserkennung
    /// (dafuer gibt es <see cref="ChangeHash"/>).
    /// </summary>
    public DateTime? UpstreamUpdatedAt { get; set; }

    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public GeoSource GeoSource { get; set; } = GeoSource.None;

    /// <summary>Der Ort, auf den das Geocoding gefallen ist - fuer die Anzeige "ungefaehr bei ...".</summary>
    [MaxLength(200)]
    public string? GeoPlaceName { get; set; }

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Wie viele Sweeps in Folge diesen Eintrag NICHT mehr geliefert haben. Erst ab zwei gilt er
    /// als abgesagt: ein einzelner fehlgeschlagener oder unvollstaendiger Sweep wuerde sonst
    /// reihenweise Absage-Meldungen ausloesen.
    /// </summary>
    public int MissedSweeps { get; set; }

    /// <summary>Gesetzt, sobald der Eintrag als verschwunden gilt. Bleibt in der Tabelle stehen.</summary>
    public DateTime? RemovedAt { get; set; }

    /// <summary>
    /// Hash ueber die Felder, deren Aenderung eine Benachrichtigung wert ist (Start, Ende, Ort).
    /// Bewusst NICHT ueber alle Felder: eine wachsende Meldeliste ist keine Terminaenderung.
    /// </summary>
    [MaxLength(64)]
    public string? ChangeHash { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Bedenkzeit-Kategorie, aus dem Freitext geraten.</summary>
public enum TournamentSpeed
{
    Unknown = 0,
    Standard = 1,
    Rapid = 2,
    Blitz = 3,
}
