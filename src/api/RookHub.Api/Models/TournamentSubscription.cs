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

    public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;
}
