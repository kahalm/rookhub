using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Einmal-Code fuer die Anmelde-UEBERGABE zwischen den Oberflaechen desselben Kontos
/// (RookHub ↔ Turnierseite). Beide liegen auf verschiedenen Origins und teilen deshalb den
/// `localStorage` NICHT — ein Sprung von der einen zur anderen wuerde ohne diesen Code in der
/// Anmeldemaske landen, obwohl dasselbe Konto dahintersteht.
///
/// <para>Der Rohwert wandert genau einmal durch die URL und wird NIE gespeichert; in der DB liegt
/// nur sein SHA-256-Hex-Hash (wie bei <see cref="PasswordResetToken"/> und <see cref="UserApiToken"/>).
/// Er lebt nur Sekunden und ist genau einmal einloesbar — ein Code, der in Verlauf, Proxy-Log oder
/// Lesezeichen haengenbleibt, ist danach wertlos.</para>
/// </summary>
public class AuthHandoffToken
{
    public int Id { get; set; }

    /// <summary>Konto, fuer das der Sprung gilt; Cascade-Delete mit dem User.</summary>
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>SHA-256-Hex des Roh-Codes (64 Zeichen), unique-indexed.</summary>
    [Required, MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Ablauf — bewusst kurz (siehe <c>AuthHandoffService.Lifetime</c>).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Gesetzt beim Einloesen; danach ist der Code verbraucht.</summary>
    public DateTime? UsedAt { get; set; }
}
