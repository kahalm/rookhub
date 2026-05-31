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

    public bool IsAdmin { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int PuzzleElo { get; set; } = 1500;

    public UserProfile? Profile { get; set; }
    public ICollection<Repertoire> Repertoires { get; set; } = new List<Repertoire>();
    public ICollection<TournamentSubscription> TournamentSubscriptions { get; set; } = new List<TournamentSubscription>();
    public ICollection<TournamentFavorite> TournamentFavorites { get; set; } = new List<TournamentFavorite>();
    public ICollection<TournamentUserSetting> TournamentUserSettings { get; set; } = new List<TournamentUserSetting>();
    public ICollection<EndlessProgress> EndlessProgresses { get; set; } = new List<EndlessProgress>();
    public ICollection<EndlessSession> EndlessSessions { get; set; } = new List<EndlessSession>();
    public ICollection<UserGroup> Groups { get; set; } = new List<UserGroup>();
}
