using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.DTOs.Accounts;

namespace RookHub.Api.Services.Accounts;

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
        await RemoveStaleAsync(userId, ct);

        var raw = SecretTokens.NewRaw();
        _db.AuthHandoffTokens.Add(new AuthHandoffToken
        {
            UserId = user.Id,
            TokenHash = SecretTokens.Sha256Hex(raw),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(Lifetime),
        });
        await _db.SaveChangesAsync(ct);
        return raw;
    }

    /// <summary>
    /// Raeumt abgelaufene/verbrauchte Codes des Nutzers in einem EIGENEN Speichern weg, bevor der neue
    /// Code dazukommt. Das Aufraeumen darf einen Wettlauf verlieren: zwei gleichzeitige Spruenge desselben
    /// Kontos (zwei Tabs, Doppeltipp) laden dieselben Altzeilen, der erste loescht sie, der zweite traf
    /// frueher 0 Zeilen → <see cref="DbUpdateConcurrencyException"/> → 500, und der Sprung kam ohne Code an
    /// (d8-rk-auth-handoff-500). Hat jemand anderes die Zeilen schon geloescht, ist das Ziel erreicht —
    /// die Eintraege werden losgelassen, und der neue Code wird trotzdem ausgegeben.
    /// </summary>
    private async Task RemoveStaleAsync(int userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var stale = await _db.AuthHandoffTokens
            .Where(t => t.UserId == userId && (t.UsedAt != null || t.ExpiresAt < now))
            .ToListAsync(ct);
        if (stale.Count == 0) return;

        _db.AuthHandoffTokens.RemoveRange(stale);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Nicht nur die gemeldeten Eintraege: ein Teil der Liste kann schon geloescht sein, der Rest nicht
            // gespeichert. Alle loslassen — was liegen bleibt, raeumt der naechste Sprung weg.
            foreach (var t in stale) _db.Entry(t).State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Loest einen Code ein und liefert eine frische Anmeldung. <c>null</c>, wenn der Code unbekannt,
    /// abgelaufen, schon verbraucht oder das Konto geloescht ist — die Gegenseite zeigt dann einfach
    /// die Anmeldemaske. Bewusst OHNE Unterscheidung nach aussen: der Grund hilft nur beim Raten.
    /// </summary>
    /// <param name="authTimeOf">Anmeldezeitpunkt der Sitzung je Konto (aus dem geteilten Cookie, P-14) — das neue Token
    /// erbt ihn; <c>null</c>/kein Wert = jetzt (ohne Elterndomaene gibt es kein Cookie).</param>
    public async Task<AuthResponseDto?> RedeemAsync(string? raw, CancellationToken ct = default, Func<int, DateTime?>? authTimeOf = null)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var hash = SecretTokens.Sha256Hex(raw.Trim());
        var token = await _db.AuthHandoffTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.UsedAt != null || token.ExpiresAt < DateTime.UtcNow)
            return null;

        // ZUERST entwerten, dann das Token bauen: bricht die Token-Erzeugung ab, ist der Code trotzdem
        // verbraucht — lieber ein Sprung, der in der Anmeldemaske endet, als ein zweimal einloesbarer Code.
        // Bedingt und atomar (A1-008): eine gleichzeitige zweite Einloesung trifft keine Zeile mehr.
        var id = token.Id;
        if (!await OneTimeCodes.TryConsumeAsync(_db, _db.AuthHandoffTokens.Where(t => t.Id == id && t.UsedAt == null),
                t => t.UsedAt, DateTime.UtcNow, ct))
            return null;

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == token.UserId && u.DeletedAt == null, ct);
        if (user is null) return null;

        _logger.LogInformation("AuthHandoff: eingeloest fuer User {UserId}", user.Id);
        return await _auth.IssueTokenAsync(user, authTime: authTimeOf?.Invoke(user.Id));
    }
}
