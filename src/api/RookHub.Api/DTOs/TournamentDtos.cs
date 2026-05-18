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
    public string CrawlerTournamentId { get; set; } = string.Empty;
    public string TournamentName { get; set; } = string.Empty;
}
