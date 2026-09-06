using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Ein gespeicherter Umkreis eines Nutzers ("Zuhause 100 km", "Ferienhaus Kaernten 50 km").
/// Traegt Mittelpunkt, Radius und Filter - und damit alles, was der naechtliche Sweep braucht,
/// um ohne Browser zu entscheiden, ob ein neues Turnier diesen Nutzer interessiert.
/// </summary>
public class TournamentSearchProfile
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Was der Nutzer eingetippt hat ("5020 Salzburg") - fuer die Anzeige und zum Nachjustieren.</summary>
    [MaxLength(200)]
    public string? PlaceQuery { get; set; }

    public double Lat { get; set; }
    public double Lon { get; set; }

    public int RadiusKm { get; set; } = 100;

    /// <summary>CSV von Foederationscodes; leer = keine Einschraenkung.</summary>
    [MaxLength(200)]
    public string? Federations { get; set; }

    /// <summary>CSV aus standard/rapid/blitz; leer = alle Bedenkzeiten.</summary>
    [MaxLength(40)]
    public string? Speeds { get; set; }

    /// <summary>Nur Turniere, die an einem Samstag oder Sonntag beginnen.</summary>
    public bool WeekendOnly { get; set; }

    public int? MinPlayers { get; set; }

    /// <summary>Benachrichtigen, wenn der Sweep neue Turniere in diesem Umkreis findet.</summary>
    public bool NotifyNew { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
