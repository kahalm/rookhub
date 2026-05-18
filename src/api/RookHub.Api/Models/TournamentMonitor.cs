using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public class TournamentMonitor
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string CrawlerTournamentId { get; set; } = string.Empty;

    public int CrawlerTournamentDbId { get; set; }

    public DateTime ActiveUntil { get; set; }

    public DateTime? LastCheckedAt { get; set; }

    public int LastKnownRounds { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
