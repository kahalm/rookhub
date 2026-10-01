using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
///   User-Enumeration) — der Controller antwortet immer neutral mit 200 und ruft es als
///   Hintergrundarbeit auf, damit auch die Antwortzeit (SMTP-Runde) nichts verraet.
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
    /// <summary>Derselbe Cache, aus dem <see cref="AuthUserValidation"/> den Auth-Zustand liest —
    /// nach dem Rotieren des Stempels muss der Eintrag weg, sonst bleiben fremde Sitzungen bis zu
    /// 60 s gültig. Optional, damit Tests den Service ohne Cache bauen können.</summary>
    private readonly IMemoryCache? _authCache;

    public PasswordResetService(AppDbContext db, IEmailSender email, IConfiguration config,
        ILogger<PasswordResetService> logger, IMemoryCache? authCache = null)
    {
        _db = db;
        _email = email;
        _config = config;
        _logger = logger;
        _authCache = authCache;
    }

    /// <summary>
    /// Erzeugt — sofern die Adresse zu einem aktiven Konto gehoert — ein Reset-Token und
    /// schickt den Link per Mail. Gibt nie etwas ueber die Existenz der Adresse preis: bei
    /// unbekannter/fehlender Adresse passiert still nichts. Mail-Fehler werden geloggt, nicht
    /// nach aussen gereicht.
    /// </summary>
    /// <param name="site">Seite der Anfrage (<c>kidhub</c>/<c>turnier</c>/<c>leaguehub</c>, sonst RookHub) — Link-Basis,
    /// Betreff und Absender; siehe <see cref="ResolveSite"/>.</param>
    /// <param name="lang">Sprache der Oberflaeche — siehe <see cref="IsEnglish"/>.</param>
    public async Task RequestResetAsync(string email, string? site = null, string? lang = null, CancellationToken ct = default)
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
        var mailSite = ResolveSite(site);
        var english = IsEnglish(lang);
        var link = BuildResetLink(rawToken, mailSite.BaseUrlKey);
        var (subject, html, text) = BuildEmail(user.Username, link, mailSite, english);
        // RookHub selbst behaelt den konfigurierten Absendernamen (Email:FromName), die anderen Seiten nennen sich selbst.
        var fromName = mailSite == RookHubSite ? null : english ? mailSite.NameEn : mailSite.NameDe;
        try
        {
            await _email.SendAsync(user.Email!, subject, html, text, fromName, ct);
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
        // API-Tokens (`rkh_…`) kennen den Stempel nicht und laufen ohne Angabe nie ab: der Reset ist
        // DER Weg nach einer Kontoübernahme und muss auch diese Zugänge entwerten, sonst behält ein
        // Angreifer über sein Extension-Token Zugriff (Repertoire-PGNs, Share-Links, Schreibwege).
        _db.UserApiTokens.RemoveRange(await _db.UserApiTokens.Where(t => t.UserId == user.Id).ToListAsync(ct));
        token.UsedAt = now;

        // Alle weiteren offenen Tokens des Users ebenfalls entwerten.
        var others = await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null && t.Id != token.Id)
            .ToListAsync(ct);
        foreach (var t in others) t.UsedAt = now;

        await _db.SaveChangesAsync(ct);
        // Gecachten Auth-Zustand verwerfen: sonst laufen die alten Sitzungen des Angreifers noch bis
        // zu 60 s weiter, obwohl der Widerruf längst in der Datenbank steht.
        if (_authCache is not null) AuthUserValidation.Invalidate(_authCache, user.Id);
        _logger.LogInformation("PasswordReset: password changed for user {UserId}", user.Id);
    }

    /// <summary>Eine Seite, von der „Passwort vergessen" kommen kann (UX-031): aus welchem Konfigurationsschluessel
    /// der Link seine Basis nimmt und wie die Mail die Seite und das Konto nennt.</summary>
    private sealed record ResetMailSite(string? BaseUrlKey, string NameDe, string NameEn, string AccountDe, string AccountEn);

    private static readonly ResetMailSite RookHubSite =
        new(null, "RookHub", "RookHub", "dein RookHub-Konto", "your RookHub account");

    /// <summary>
    /// FESTE Liste: der Client nennt nur einen Schluessel, die Basis-URL kommt allein aus der Konfiguration
    /// (sonst liesse sich ein fremder Link in eine echte Mail einschleusen). Unbekannt oder leer = RookHub.
    /// KidHub und LeagueHub sagen dazu, dass es dasselbe Konto wie bei RookHub ist — der Anmeldename in der Mail
    /// (und bis zur eigenen Basis-URL der Link auf RookHub) wirkte sonst wie eine fremde Mail.
    /// </summary>
    private static ResetMailSite ResolveSite(string? site) => site?.Trim().ToLowerInvariant() switch
    {
        "kidhub" => new("App:KidHubBaseUrl", "KidHub", "KidHub",
            "dein KidHub-Konto (dasselbe Konto wie bei RookHub)", "your KidHub account (the same account as on RookHub)"),
        "leaguehub" => new("App:LeagueHubBaseUrl", "LeagueHub", "LeagueHub",
            "dein LeagueHub-Konto (dasselbe Konto wie bei RookHub)", "your LeagueHub account (the same account as on RookHub)"),
        "turnier" => new("App:TurnierBaseUrl", "RookHub Turniere", "RookHub Tournaments",
            "dein RookHub-Konto", "your RookHub account"),
        _ => RookHubSite,
    };

    /// <summary>Deutsch und Englisch. Ohne Angabe Deutsch (wie bisher); eine andere ausdrueckliche Sprache
    /// (hr, hu, …) bekommt Englisch — wie die Oberflaechen selbst, die fuer fehlende Texte auf Englisch fallen.</summary>
    private static bool IsEnglish(string? lang)
        => !string.IsNullOrWhiteSpace(lang) && !lang.Trim().StartsWith("de", StringComparison.OrdinalIgnoreCase);

    private string BuildResetLink(string rawToken, string? siteBaseUrlKey)
    {
        // Basis-URL der Seite, von der die Anfrage kam — ohne eigene Konfiguration die von RookHub.
        var baseUrl = siteBaseUrlKey is null ? null : _config[siteBaseUrlKey]?.Trim().TrimEnd('/');
        // Basis-URL des Frontends; Fallback auf relativ, falls nicht konfiguriert (Link dann
        // nur in der Mail kaputt — wird per Warnung sichtbar gemacht).
        if (string.IsNullOrEmpty(baseUrl))
            baseUrl = _config["App:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
            _logger.LogWarning("PasswordReset: App:BaseUrl not configured — reset link will be relative.");
        return $"{baseUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}";
    }

    private static (string subject, string html, string text) BuildEmail(string username, string link, ResetMailSite site, bool english)
    {
        var minutes = (int)TokenTtl.TotalMinutes;
        var name = System.Net.WebUtility.HtmlEncode(username);
        var href = System.Net.WebUtility.HtmlEncode(link);
        if (english)
        {
            return (
                $"{site.NameEn} — Reset your password",
                $"<p>Hello {name},</p>" +
                $"<p>a password reset was requested for {site.AccountEn}. " +
                $"Your username for signing in is: <strong>{name}</strong>. " +
                $"Click the following link to set a new password (valid for {minutes} minutes):</p>" +
                $"<p><a href=\"{href}\">Reset password now</a></p>" +
                $"<p style=\"color:#888;font-size:0.9em\">If the link does not work, copy this address into your browser:<br>{href}</p>" +
                "<p>If this wasn't you, you can ignore this email — your password stays unchanged.</p>" +
                $"<p>— {site.NameEn}</p>",
                $"Hello {username},\n\n" +
                $"a password reset was requested for {site.AccountEn}.\n" +
                $"Your username for signing in is: {username}\n" +
                $"Open the following link to set a new password (valid for {minutes} minutes):\n\n" +
                $"{link}\n\n" +
                "If this wasn't you, you can ignore this email — your password stays unchanged.\n\n" +
                $"— {site.NameEn}");
        }
        var subject = $"{site.NameDe} — Passwort zurücksetzen";
        var text =
            $"Hallo {username},\n\n" +
            $"für {site.AccountDe} wurde ein Zurücksetzen des Passworts angefordert.\n" +
            $"Dein Benutzername für die Anmeldung lautet: {username}\n" +
            $"Öffne den folgenden Link, um ein neues Passwort zu setzen (gültig für {minutes} Minuten):\n\n" +
            $"{link}\n\n" +
            "Wenn du das nicht warst, kannst du diese E-Mail ignorieren — dein Passwort bleibt unverändert.\n\n" +
            $"— {site.NameDe}";
        var html =
            $"<p>Hallo {name},</p>" +
            $"<p>für {site.AccountDe} wurde ein Zurücksetzen des Passworts angefordert. " +
            $"Dein Benutzername für die Anmeldung lautet: <strong>{name}</strong>. " +
            $"Klicke auf den folgenden Link, um ein neues Passwort zu setzen (gültig für {minutes} Minuten):</p>" +
            $"<p><a href=\"{href}\">Passwort jetzt zurücksetzen</a></p>" +
            $"<p style=\"color:#888;font-size:0.9em\">Falls der Link nicht funktioniert, kopiere diese Adresse in den Browser:<br>{href}</p>" +
            "<p>Wenn du das nicht warst, kannst du diese E-Mail ignorieren — dein Passwort bleibt unverändert.</p>" +
            $"<p>— {site.NameDe}</p>";
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
