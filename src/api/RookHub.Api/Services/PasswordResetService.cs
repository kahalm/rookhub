using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Passwort vergessen"-Flow: Reset-Link per E-Mail anfordern (<see cref="RequestResetAsync"/>)
/// und neues Passwort mit dem Token aus der Mail setzen (<see cref="ResetPasswordAsync"/>).
///
/// Sicherheit:
/// - Der Roh-Token wird NIE gespeichert (nur SHA-256-Hex) und nur per Mail an die hinterlegte
///   Adresse geschickt → wer die Mail nicht hat, kann nicht zuruecksetzen.
/// - Tokens sind einmalig (<c>UsedAt</c>) und laufen nach <see cref="TokenTtl"/> ab.
/// - <see cref="RequestResetAsync"/> verraet NICHT, ob eine Adresse existiert (keine
///   User-Enumeration) — der Controller antwortet immer neutral mit 200.
/// </summary>
public class PasswordResetService
{
    public static readonly TimeSpan TokenTtl = TimeSpan.FromHours(1);
    private const int TokenBytes = 32;            // → ~43 Char Base64URL
    private const int BcryptWorkFactor = 12;      // identisch zu AuthService

    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(AppDbContext db, IEmailSender email, IConfiguration config, ILogger<PasswordResetService> logger)
    {
        _db = db;
        _email = email;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Erzeugt — sofern die Adresse zu einem aktiven Konto gehoert — ein Reset-Token und
    /// schickt den Link per Mail. Gibt nie etwas ueber die Existenz der Adresse preis: bei
    /// unbekannter/fehlender Adresse passiert still nichts. Mail-Fehler werden geloggt, nicht
    /// nach aussen gereicht.
    /// </summary>
    public async Task RequestResetAsync(string email, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await _db.AppUsers
            .FirstOrDefaultAsync(u => u.Email == normalized && u.DeletedAt == null, ct);
        if (user == null)
        {
            _logger.LogInformation("PasswordReset: request for unknown/inactive email (no action)");
            return;
        }

        // ERST die Mail verschicken, DANN alte Tokens entwerten + das neue persistieren.
        // Umgekehrt (entwerten+committen vor dem Versand) liess ein SMTP-Ausfall den User mit
        // NULL funktionierenden Links zurueck: der bereits zugestellte alte Link war entwertet,
        // der neue kam nie an (Send-Fehler wird bewusst geschluckt, s. u.).
        var rawToken = GenerateRawToken();
        var link = BuildResetLink(rawToken);
        var (subject, html, text) = BuildEmail(user.Username, link);
        try
        {
            await _email.SendAsync(user.Email!, subject, html, text, ct);
        }
        catch (Exception ex)
        {
            // Nicht nach aussen reichen (Enumeration/UX) — aber sichtbar fuers Monitoring.
            // Nichts entwertet/persistiert → ein frueher zugestellter Link bleibt gueltig.
            _logger.LogError(ex, "PasswordReset: sending mail failed for user {UserId} — existing tokens kept", user.Id);
            return;
        }

        // Frueher angeforderte, noch offene Tokens des Users entwerten (nur das jeweils neueste gilt).
        var open = await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var t in open) t.UsedAt = now;

        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = ComputeHash(rawToken),
            CreatedAt = now,
            ExpiresAt = now.Add(TokenTtl),
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("PasswordReset: token issued + mail dispatched for user {UserId}", user.Id);
    }

    /// <summary>
    /// Setzt das Passwort anhand eines gueltigen, nicht abgelaufenen, noch nicht verwendeten
    /// Tokens. Wirft <see cref="UnauthorizedAccessException"/>, wenn das Token ungueltig/abgelaufen
    /// /verbraucht ist oder der User nicht (mehr) aktiv ist.
    /// </summary>
    public async Task ResetPasswordAsync(string rawToken, string newPassword, CancellationToken ct = default)
    {
        var hash = ComputeHash(rawToken);
        var now = DateTime.UtcNow;
        var token = await _db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token == null || token.UsedAt != null || token.ExpiresAt < now)
            throw new UnauthorizedAccessException("Invalid or expired reset token.");

        var user = token.User;
        if (user == null || user.DeletedAt != null)
            throw new UnauthorizedAccessException("Invalid or expired reset token.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, BcryptWorkFactor);
        // Security-Stamp rotieren → bestehende JWTs (mit altem sstamp-Claim) werden ungültig.
        user.SecurityStamp = AuthService.NewSecurityStamp();
        token.UsedAt = now;

        // Alle weiteren offenen Tokens des Users ebenfalls entwerten.
        var others = await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null && t.Id != token.Id)
            .ToListAsync(ct);
        foreach (var t in others) t.UsedAt = now;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("PasswordReset: password changed for user {UserId}", user.Id);
    }

    private string BuildResetLink(string rawToken)
    {
        // Basis-URL des Frontends; Fallback auf relativ, falls nicht konfiguriert (Link dann
        // nur in der Mail kaputt — wird per Warnung sichtbar gemacht).
        var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
            _logger.LogWarning("PasswordReset: App:BaseUrl not configured — reset link will be relative.");
        return $"{baseUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}";
    }

    private static (string subject, string html, string text) BuildEmail(string username, string link)
    {
        var minutes = (int)TokenTtl.TotalMinutes;
        const string subject = "RookHub — Passwort zurücksetzen";
        var text =
            $"Hallo {username},\n\n" +
            "für dein RookHub-Konto wurde ein Zurücksetzen des Passworts angefordert.\n" +
            $"Öffne den folgenden Link, um ein neues Passwort zu setzen (gültig für {minutes} Minuten):\n\n" +
            $"{link}\n\n" +
            "Wenn du das nicht warst, kannst du diese E-Mail ignorieren — dein Passwort bleibt unverändert.\n\n" +
            "— RookHub";
        var html =
            $"<p>Hallo {System.Net.WebUtility.HtmlEncode(username)},</p>" +
            "<p>für dein RookHub-Konto wurde ein Zurücksetzen des Passworts angefordert. " +
            $"Klicke auf den folgenden Link, um ein neues Passwort zu setzen (gültig für {minutes} Minuten):</p>" +
            $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(link)}\">Passwort jetzt zurücksetzen</a></p>" +
            $"<p style=\"color:#888;font-size:0.9em\">Falls der Link nicht funktioniert, kopiere diese Adresse in den Browser:<br>{System.Net.WebUtility.HtmlEncode(link)}</p>" +
            "<p>Wenn du das nicht warst, kannst du diese E-Mail ignorieren — dein Passwort bleibt unverändert.</p>" +
            "<p>— RookHub</p>";
        return (subject, html, text);
    }

    private static string GenerateRawToken()
    {
        var buf = new byte[TokenBytes];
        RandomNumberGenerator.Fill(buf);
        return Convert.ToBase64String(buf)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>SHA-256-Hex (lowercase) eines Roh-Tokens (identisch zu ApiTokenService).</summary>
    private static string ComputeHash(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
