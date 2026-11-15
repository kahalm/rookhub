using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Besucher-Herkunft: EINE Zeile je Tag (UTC), Besucher und Oberfläche — die Grundlage der Weltkarte „Besucher nach
/// Ländern“ (<c>docs/api/besucher-herkunft.md</c>). Datensparsam: keine IP-Adresse, kein Benutzername, keine Uhrzeit.
/// </summary>
public class VisitorDay
{
    public int Id { get; set; }

    /// <summary>Kalendertag in UTC.</summary>
    public DateOnly Day { get; set; }

    /// <summary>Pseudonym des Besuchers: HMAC-SHA256 der Besucher-Kennung mit einem Schlüssel des Servers, auf 32
    /// Hex-Zeichen gekürzt. Über die Tage gleich (sonst ließe sich „eindeutig im Zeitraum“ nicht zählen), ohne den
    /// Schlüssel nicht auf Konto oder Browser zurückzuführen.</summary>
    [MaxLength(32)]
    public string VisitorHash { get; set; } = string.Empty;

    /// <summary>Oberfläche laut Host der Anfrage (<c>rookhub</c>, <c>leaguehub</c>, <c>kidhub</c>, <c>clubhub</c>,
    /// <c>turnier</c>).</summary>
    [MaxLength(12)]
    public string Surface { get; set; } = string.Empty;

    /// <summary>ISO-3166-Land (zwei Großbuchstaben) aus der IP-Adresse; leer = unbekannt.</summary>
    [MaxLength(2)]
    public string Country { get; set; } = string.Empty;

    /// <summary>Gezählte Aufrufe dieses Besuchers an diesem Tag auf dieser Oberfläche.</summary>
    public int Hits { get; set; }
}
