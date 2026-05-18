using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public class AppUser
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public UserProfile? Profile { get; set; }
    public ICollection<Repertoire> Repertoires { get; set; } = new List<Repertoire>();
    public ICollection<TournamentSubscription> TournamentSubscriptions { get; set; } = new List<TournamentSubscription>();
}
