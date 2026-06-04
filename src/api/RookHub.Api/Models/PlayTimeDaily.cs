using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Aggregierte externe Spielzeit eines Users je UTC-Tag und Plattform (Lichess/chess.com).
/// Wird vom <c>PlayTimeSyncService</c> befüllt und speist die Kategorie „Spielen" im
/// Trainingsziele-Tracker. Unique (UserId, Date, Platform).
/// </summary>
public class PlayTimeDaily
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>UTC-Datum (DATE-Spalte, keine Uhrzeit).</summary>
    public DateOnly Date { get; set; }

    /// <summary>"lichess" | "chesscom".</summary>
    [MaxLength(16)]
    public string Platform { get; set; } = string.Empty;

    /// <summary>Summe der an diesem Tag auf dieser Plattform gespielten Sekunden.</summary>
    public int Seconds { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
