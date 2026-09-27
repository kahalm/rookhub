using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public class TournamentFavorite
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    [Required, MaxLength(50)]
    public string CrawlerTournamentId { get; set; } = string.Empty;

    public int? PlayerSnr { get; set; }

    public int? TeamSnr { get; set; }

    public DateTime FavoritedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Ein Spieler, dessen Stern der Nutzer in diesem Turnier selbst entfernt hat.
///
/// <para><b>Warum es das braucht.</b> Man selbst, die Freunde und die verfolgten Spieler werden
/// in jedem Turnier automatisch Favorit — beim Oeffnen der Turnierseite und im naechtlichen
/// Abgleich. Ohne diese Zeile kaeme ein entfernter Stern beim naechsten Oeffnen zurueck, und
/// „nicht mehr Favorit" liesse sich gar nicht ausdruecken. Wer den Stern wieder setzt, loescht
/// die Zeile.</para>
/// </summary>
public class TournamentFavoriteDismissal
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    [Required, MaxLength(50)]
    public string CrawlerTournamentId { get; set; } = string.Empty;

    public int PlayerSnr { get; set; }

    public DateTime DismissedAt { get; set; } = DateTime.UtcNow;
}
