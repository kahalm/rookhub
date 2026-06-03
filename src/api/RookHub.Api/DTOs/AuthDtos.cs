using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

public class RegisterDto
{
    [Required, MinLength(3), MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    // Bewusst minimal: nur Mindestlänge (>= 4), keine Komplexitätsregeln.
    [Required, MinLength(4), MaxLength(1024)]
    public string Password { get; set; } = string.Empty;
}

public class LoginDto
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>„Eingeloggt bleiben" — verlängert die Token-Gültigkeit (30 Tage statt 1 Tag).</summary>
    public bool RememberMe { get; set; }
}

public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int UserId { get; set; }
    public bool IsAdmin { get; set; }
}
