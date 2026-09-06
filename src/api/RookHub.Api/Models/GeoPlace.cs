using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public enum GeoPlaceKind
{
    PostalCode = 0,
    City = 1,
    Region = 2,
}

/// <summary>
/// Ortslexikon aus dem GeoNames-Export (CC BY 4.0). Liegt lokal, weil die Spielorte als Freitext
/// kommen und ein oeffentlicher Geocoder fuer diese Menge weder erlaubt noch schnell genug waere.
/// </summary>
public class GeoPlace
{
    public int Id { get; set; }

    /// <summary>ISO-3166-1-alpha-2 (AT, DE, ...) - nicht der FIDE-Code.</summary>
    [Required, MaxLength(2)]
    public string Country { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? PostalCode { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Kleingeschrieben und umlautgefaltet - danach wird gesucht.</summary>
    [Required, MaxLength(200)]
    public string NameNormalized { get; set; } = string.Empty;

    public double Lat { get; set; }
    public double Lon { get; set; }

    public GeoPlaceKind Kind { get; set; }

    /// <summary>Entscheidet bei mehrdeutigen Ortsnamen (drei Mal "Neustadt"): der groesste gewinnt.</summary>
    public int Population { get; set; }
}
