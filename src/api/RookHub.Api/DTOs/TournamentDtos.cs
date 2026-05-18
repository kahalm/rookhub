using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

public class TournamentSubscriptionDto
{
    public int Id { get; set; }
    public string CrawlerTournamentId { get; set; } = string.Empty;
    public string TournamentName { get; set; } = string.Empty;
    public DateTime SubscribedAt { get; set; }
}

public class CreateSubscriptionDto
{
    [Required, MaxLength(50), RegularExpression(@"^\d{1,10}$", ErrorMessage = "CrawlerTournamentId must be a numeric ID (1-10 digits).")]
    public string CrawlerTournamentId { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string TournamentName { get; set; } = string.Empty;
}
