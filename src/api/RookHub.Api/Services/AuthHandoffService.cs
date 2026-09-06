using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Anmelde-Uebergabe zwischen den Oberflaechen desselben Kontos (RookHub ↔ Turnierseite).
///
/// <para><b>Warum es das braucht:</b> Das JWT liegt im <c>localStorage</c>, und der gehoert zur
/// ORIGIN. Zwei Subdomains sind zwei Origins — wer auf der einen angemeldet ist, ist es auf der
/// anderen nicht, obwohl dasselbe Konto dahintersteht. Der Sprung nimmt deshalb einen Einmal-Code
/// mit, den die Gegenseite gegen ein eigenes Token eintauscht.</para>
///
/// <para><b>Warum nicht einfach das JWT weiterreichen:</b> Es liefe 30–90 Tage und stuende damit im
/// Browserverlauf, in Proxy-Logs und in jedem Lesezeichen. Der Code hier lebt Sekunden und ist genau
/// einmal einloesbar; wer ihn spaeter findet, findet nichts Verwertbares.</para>
/// </summary>
public class AuthHandoffService
{
    private readonly AppDbContext _db;
    private readonly AuthService _auth;
    private readonly ILogger<AuthHandoffService> _logger;

    /// <summary>Lebensdauer des Codes. Er ueberbrueckt einen Klick, keine Sitzung.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public AuthHandoffService(AppDbContext db, AuthService auth, ILogger<AuthHandoffService> logger)
    {
        _db = db;
        _auth = auth;
        _logger = logger;
    }

    /// <summary>Legt einen Einmal-Code fuer den aufrufenden Nutzer an; der Rohwert kommt NUR hier heraus.</summary>
    public async Task<string> IssueAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException("User not found.");

        // Abgelaufene/verbrauchte Codes DIESES Nutzers wegraeumen — sonst waechst die Tabelle mit jedem
        // Sprung. Ein eigener Aufraeum-Dienst waere fuer Zeilen mit 60 s Lebensdauer uebertrieben.
        var stale = await _db.AuthHandoffTokens
            .Where(t => t.UserId == userId && (t.UsedAt != null || t.ExpiresAt < DateTime.UtcNow))
            .ToListAsync(ct);
        if (stale.Count > 0) _db.AuthHandoffTokens.RemoveRange(stale);

        var raw = GenerateRawCode();
        _db.AuthHandoffTokens.Add(new AuthHandoffToken
        {
            UserId = user.Id,
            TokenHash = ComputeHash(raw),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(Lifetime),
        });
        await _db.SaveChangesAsync(ct);
        return raw;
    }

    /// <summary>
    /// Loest einen Code ein und liefert eine frische Anmeldung. <c>null</c>, wenn der Code unbekannt,
    /// abgelaufen, schon verbraucht oder das Konto geloescht ist — die Gegenseite zeigt dann einfach
    /// die Anmeldemaske. Bewusst OHNE Unterscheidung nach aussen: der Grund hilft nur beim Raten.
    /// </summary>
    public async Task<AuthResponseDto?> RedeemAsync(string? raw, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var hash = ComputeHash(raw.Trim());
        var token = await _db.AuthHandoffTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.UsedAt != null || token.ExpiresAt < DateTime.UtcNow)
            return null;

        // ZUERST entwerten, dann das Token bauen: bricht die Token-Erzeugung ab, ist der Code trotzdem
        // verbraucht — lieber ein Sprung, der in der Anmeldemaske endet, als ein zweimal einloesbarer Code.
        token.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == token.UserId && u.DeletedAt == null, ct);
        if (user is null) return null;

        _logger.LogInformation("AuthHandoff: eingeloest fuer User {UserId}", user.Id);
        return await _auth.IssueTokenAsync(user);
    }

    private static string GenerateRawCode()
    {
        var buf = new byte[32];
        RandomNumberGenerator.Fill(buf);
        return Convert.ToBase64String(buf).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string ComputeHash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
}
