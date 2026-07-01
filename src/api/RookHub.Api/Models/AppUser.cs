using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public class AppUser
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    // Optional (nullable). Unique-Index erlaubt mehrere NULLs (MySQL/MariaDB).
    [MaxLength(255)]
    public string? Email { get; set; }

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Rotiert bei Passwort-Reset/-Änderung und invalidiert damit alle bereits ausgegebenen JWTs,
    /// die den alten Stempel als <c>sstamp</c>-Claim tragen. <c>null</c> = noch kein Stempel
    /// (Alt-Bestand) → die zugehörigen Tokens werden „grandfathered" (kein Massen-Logout beim Deploy).
    /// </summary>
    [MaxLength(64)]
    public string? SecurityStamp { get; set; }

    public bool IsAdmin { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gesetzt, wenn der Account gelöscht/anonymisiert wurde (DSGVO). Solche Accounts
    /// können sich nicht mehr einloggen; PII ist entfernt, Solve-Statistik bleibt anonym erhalten.</summary>
    public DateTime? DeletedAt { get; set; }

    public int PuzzleElo { get; set; } = 1500;

    public int? PuzzleEloViz1 { get; set; }  // Level 1 (Default: 1400)
    public int? PuzzleEloViz2 { get; set; }  // Level 2 (Default: 1300)
    public int? PuzzleEloViz3 { get; set; }  // Level 3 (Default: 1200)
    public int? PuzzleEloViz4 { get; set; }  // Level 4 (Default: 1100)

    public UserProfile? Profile { get; set; }
    public ICollection<Repertoire> Repertoires { get; set; } = new List<Repertoire>();
    public ICollection<TournamentSubscription> TournamentSubscriptions { get; set; } = new List<TournamentSubscription>();
    public ICollection<TournamentFavorite> TournamentFavorites { get; set; } = new List<TournamentFavorite>();
    public ICollection<TournamentUserSetting> TournamentUserSettings { get; set; } = new List<TournamentUserSetting>();
    public ICollection<EndlessProgress> EndlessProgresses { get; set; } = new List<EndlessProgress>();
    public ICollection<EndlessSession> EndlessSessions { get; set; } = new List<EndlessSession>();
    public ICollection<UserGroup> Groups { get; set; } = new List<UserGroup>();
    public ICollection<CourseProgress> CourseProgresses { get; set; } = new List<CourseProgress>();
    public ICollection<CoursePuzzleResult> CoursePuzzleResults { get; set; } = new List<CoursePuzzleResult>();
    public ICollection<CoursePin> CoursePins { get; set; } = new List<CoursePin>();
}
