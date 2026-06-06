using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

public class RegisterDto
{
    [Required, MinLength(3), MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    // Optional: leer/weggelassen = keine Email hinterlegt. Wenn angegeben, muss es
    // ein gueltiges Email-Format sein (EmailAddress laesst null durch, "" nicht ->
    // das Frontend sendet bei leerem Feld null).
    [EmailAddress, MaxLength(255)]
    public string? Email { get; set; }

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

public class ChangePasswordDto
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(4), MaxLength(1024)]
    public string NewPassword { get; set; } = string.Empty;
}
