using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

public class TournamentFavoriteDto
{
    public int Id { get; set; }
    public string CrawlerTournamentId { get; set; } = string.Empty;
    public int? PlayerSnr { get; set; }
    public int? TeamSnr { get; set; }
    public DateTime FavoritedAt { get; set; }
}

public class CreateTournamentFavoriteDto
{
    [Required, MaxLength(50), RegularExpression(@"^\d{1,10}$", ErrorMessage = "CrawlerTournamentId must be a numeric ID.")]
    public string CrawlerTournamentId { get; set; } = string.Empty;

    [Required]
    public int PlayerSnr { get; set; }
}

public class CreateTeamFavoriteDto
{
    [Required, MaxLength(50), RegularExpression(@"^\d{1,10}$", ErrorMessage = "CrawlerTournamentId must be a numeric ID.")]
    public string CrawlerTournamentId { get; set; } = string.Empty;

    [Required]
    public int TeamSnr { get; set; }
}

public class TournamentSettingsDto
{
    public bool ShowFavoritesOnly { get; set; }
}
