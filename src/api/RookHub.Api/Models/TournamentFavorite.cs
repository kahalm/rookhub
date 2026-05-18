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
