using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>
/// Regeln für ein NEU gesetztes Passwort (Registrierung, Ändern, Reset) — Login prüft weiter nichts,
/// Altbestand bleibt also nutzbar. FALLE: die frühere Untergrenze von 4 Zeichen erlaubte vierstellige
/// PINs; der gesamte Raum (10 000 Kombinationen) ist online durchprobierbar, denn der Auth-Rate-Limiter
/// bremst nur pro IP, nicht pro Konto. Bewusst weiterhin OHNE Zeichenklassen-Zwang: nur Länge plus
/// Ausschluss der Allerweltspasswörter, die ein Wörterbuch-Angriff zuerst durchgeht.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PasswordPolicyAttribute : ValidationAttribute
{
    public const int MinimumLength = 8;

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password123", "passwort", "passwort1", "12345678", "123456789",
        "1234567890", "qwertyui", "qwerty123", "qwertz123", "iloveyou", "letmein1", "welcome1",
        "abc12345", "admin123", "sunshine", "princess", "football", "baseball", "dragon12",
        "monkey123", "schach123", "rookhub1", "rookhub123",
    };

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string password || password.Length == 0)
            return ValidationResult.Success;                   // Leerfall meldet [Required]
        if (password.Length < MinimumLength)
            return new ValidationResult($"Password must be at least {MinimumLength} characters long.");
        if (Common.Contains(password) || password.Distinct().Count() == 1)
            return new ValidationResult("Password is too common, please choose a different one.");
        return ValidationResult.Success;
    }
}

public class RegisterDto
{
    [Required, MinLength(3), MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    // Optional: leer/weggelassen = keine Email hinterlegt. Wenn angegeben, muss es
    // ein gueltiges Email-Format sein (EmailAddress laesst null durch, "" nicht ->
    // das Frontend sendet bei leerem Feld null).
    [EmailAddress, MaxLength(255)]
    public string? Email { get; set; }

    // Bewusst minimal: Mindestlänge + Sperrliste (siehe PasswordPolicyAttribute), keine Zeichenklassen.
    [Required, MinLength(PasswordPolicyAttribute.MinimumLength), MaxLength(1024), PasswordPolicy]
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
    /// <summary>True, wenn dieses Token von einem Admin per „Als Nutzer einsteigen" erzeugt wurde.</summary>
    public bool Impersonating { get; set; }
    /// <summary>Benutzername des Admins, der die Impersonation gestartet hat (nur bei Impersonating).</summary>
    public string? ImpersonatorUsername { get; set; }
}

public class ChangePasswordDto
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(PasswordPolicyAttribute.MinimumLength), MaxLength(1024), PasswordPolicy]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>„Passwort vergessen" — Schritt 1: Reset-Link per E-Mail anfordern.</summary>
public class ForgotPasswordDto
{
    [Required, EmailAddress, MaxLength(255)]
    public string Email { get; set; } = string.Empty;
}

/// <summary>„Passwort vergessen" — Schritt 2: neues Passwort mit dem Token aus der E-Mail setzen.</summary>
public class ResetPasswordDto
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, MinLength(PasswordPolicyAttribute.MinimumLength), MaxLength(1024), PasswordPolicy]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Einloesung eines Anmelde-Uebergabe-Codes (siehe <c>AuthHandoffService</c>).</summary>
public class HandoffExchangeDto
{
    [Required, MaxLength(200)]
    public string Code { get; set; } = string.Empty;
}
