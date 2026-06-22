using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public class TournamentSubscription
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    [Required, MaxLength(50)]
    public string CrawlerTournamentId { get; set; } = string.Empty;

    [MaxLength(300)]
    public string TournamentName { get; set; } = string.Empty;

    /// <summary>
    /// Turnierdatum (Termin) — primär das Ende-Datum aus der Spielersuche, sonst (für Altbestand)
    /// das vom Crawler gemeldete Datum, das der Refresh-Lauf nachträgt. <c>null</c> = noch unbekannt.
    /// Steuert den Refresh-Crawl (laufende/frisch beendete Turniere nachladen) und die
    /// Turnier-Einordnung im Motivations-Bot (anstehend/laufend/beendet).
    /// </summary>
    public DateOnly? EventDate { get; set; }

    public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;
}
