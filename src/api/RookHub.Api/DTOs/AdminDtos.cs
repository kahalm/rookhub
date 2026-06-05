namespace RookHub.Api.DTOs;

public class AdminUserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Namen der Gruppen, in denen der User Mitglied ist.</summary>
    public List<string> Groups { get; set; } = new();
}
