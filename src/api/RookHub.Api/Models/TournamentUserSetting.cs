using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public class TournamentUserSetting
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    [Required, MaxLength(50)]
    public string CrawlerTournamentId { get; set; } = string.Empty;

    public bool ShowFavoritesOnly { get; set; }
}
