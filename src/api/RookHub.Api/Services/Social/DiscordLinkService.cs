using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Social;

/// <summary>
/// Verifiziert die vom schach-bot signierten Discord-Verknüpfungs-Tokens.
/// Format (identisch im Bot): token = body + "." + sig
///   body = base64url(utf8(JSON {"id","u","exp"}))   (ohne Padding)
///   sig  = base64url(HMAC_SHA256(secret, body))      (ohne Padding)
///
/// Freiwerden einer Discord-ID (Codereview A1-011): Der Bot hängt an JEDEN Link in einer DM ein neues Token,
/// gültig bis exp (Bot-<c>DEFAULT_TTL</c>, 30 Tage). Wird die ID frei (Trennen, Wechsel auf eine andere ID,
/// Kontolöschung — <see cref="MarkReleasedAsync"/>), vermerkt der Dienst das bisherige Konto und den Zeitpunkt.
/// Tokens, die bis dahin ausgestellt wurden (<c>exp &lt;= freiAm + maxAlter</c>), löst danach nur noch dieses
/// Konto ein (<see cref="IsReservedForOtherAsync"/>) — sonst verknüpfte, wer einen weitergeleiteten Rätsellink hat,
/// die fremde Discord-ID mit dem eigenen Konto. Ein frisches <c>/link</c> danach ist für jedes Konto gültig.
/// Der Vermerk steht in der Tabelle <c>DiscordLinkReleases</c> (eine Zeile je ID) und überlebt Neustarts — bis
/// 0.829 lebte er nur im Speicher, und nach einem Neustart galten die alten Tokens wieder für jeden. Abgelaufene
/// Vermerke (<c>freiAm + maxAlter</c> vorbei) zählen nicht mehr und räumt das nächste Freiwerden ab. Eine NIE
/// verknüpfte ID schützt er nicht (dagegen hilft nur eine kürzere Bot-TTL).
/// Scoped (DbContext der Anfrage): <see cref="MarkReleasedAsync"/> legt den Vermerk nur in den Kontext, das
/// Speichern übernimmt der Aufrufer im SELBEN <c>SaveChanges</c> wie das Freigeben — beides oder nichts.
/// </summary>
public class DiscordLinkService
{
    /// <summary>Standard für <c>Discord:LinkTokenMaxAgeDays</c> = Bot-<c>DEFAULT_TTL</c> (30 Tage).</summary>
    public const double DefaultTokenMaxAgeDays = 30;

    private readonly string? _secret;
    private readonly AppDbContext _db;
    /// <summary>Längste Gültigkeit, die der Bot einem Token gibt: ein Token mit <c>exp &lt;= t + maxAlter</c>
    /// wurde spätestens zum Zeitpunkt t ausgestellt.</summary>
    private readonly TimeSpan _tokenMaxAge;

    public DiscordLinkService(IConfiguration config, AppDbContext db)
    {
        _db = db;
        // Ein Platzhalter aus den Beispiel-Dateien zählt wie „leer" = Feature aus: mit dem öffentlich
        // bekannten Wert könnte sich jeder ein Link-Token für eine fremde Discord-ID signieren.
        _secret = SecretConfigCheck.Usable(config["Discord:LinkSecret"]);
        // Muss mindestens so lang sein wie die TTL im Bot, sonst gelten alte Tokens wieder für jeden.
        _tokenMaxAge = TimeSpan.FromDays(
            double.TryParse(config["Discord:LinkTokenMaxAgeDays"], NumberStyles.Float, CultureInfo.InvariantCulture,
                out var days) && days > 0 && days <= 3650
                ? days
                : DefaultTokenMaxAgeDays);
    }

    public bool Enabled => !string.IsNullOrEmpty(_secret);

    public record DiscordIdentity(string Id, string? Username)
    {
        /// <summary>Ablauf laut Token (<c>exp</c>).</summary>
        public DateTimeOffset ExpiresAt { get; init; }
    }

    /// <summary>Verifiziert Token (Signatur + Ablauf). null = ungültig/abgelaufen/Feature aus.</summary>
    public DiscordIdentity? Verify(string? token)
    {
        if (string.IsNullOrEmpty(_secret) || string.IsNullOrWhiteSpace(token)) return null;

        var dot = token.LastIndexOf('.');
        if (dot <= 0 || dot >= token.Length - 1) return null;
        var body = token[..dot];
        var sig = token[(dot + 1)..];

        var expected = Base64UrlEncode(HmacSha256(_secret, body));
        if (!FixedTimeEquals(sig, expected)) return null;

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(body));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) return null;
            var exp = root.TryGetProperty("exp", out var expEl) && expEl.TryGetInt64(out var e) ? e : 0;
            if (exp <= 0 || DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return null;   // abgelaufen
            var user = root.TryGetProperty("u", out var uEl) ? uEl.GetString() : null;
            return new DiscordIdentity(id!, string.IsNullOrWhiteSpace(user) ? null : user)
            {
                ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(exp),
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Die Discord-ID <paramref name="discordId"/> war bis eben mit <paramref name="userId"/> verknüpft und wird
    /// frei. Legt den Vermerk (Upsert je ID: der jüngere Freiwerde-Zeitpunkt gewinnt) in den Kontext und räumt dabei die
    /// abgelaufenen ab — gespeichert wird erst mit dem <c>SaveChanges</c> des Aufrufers, das die ID freigibt.</summary>
    public async Task MarkReleasedAsync(string discordId, int userId)
    {
        var now = DateTime.UtcNow;
        var expiredBefore = now - _tokenMaxAge;
        _db.DiscordLinkReleases.RemoveRange(await _db.DiscordLinkReleases
            .Where(r => r.ReleasedAt < expiredBefore && r.DiscordId != discordId)
            .ToListAsync());

        var release = await _db.DiscordLinkReleases.FindAsync(discordId);
        if (release == null)
            _db.DiscordLinkReleases.Add(new DiscordLinkRelease { DiscordId = discordId, UserId = userId, ReleasedAt = now });
        else if (release.ReleasedAt <= now)
        {
            release.UserId = userId;
            release.ReleasedAt = now;
        }
    }

    /// <summary>Wurde das Token ausgestellt, als die Discord-ID noch einem ANDEREN Konto gehörte? Dann löst es nur
    /// dieses Konto ein (Wiederverknüpfen nach eigener Trennung); für alle anderen gilt es wie ein ungültiges.
    /// Ein Vermerk ohne Konto (hart gelöscht) sperrt die alten Tokens für jedes Konto.</summary>
    public async Task<bool> IsReservedForOtherAsync(DiscordIdentity identity, int userId)
    {
        var release = await _db.DiscordLinkReleases.AsNoTracking()
            .FirstOrDefaultAsync(r => r.DiscordId == identity.Id);
        if (release == null || release.UserId == userId) return false;
        var reservedUntil = new DateTimeOffset(DateTime.SpecifyKind(release.ReleasedAt, DateTimeKind.Utc)) + _tokenMaxAge;
        // Abgelaufen (reservedUntil vorbei) folgt schon aus exp > jetzt — ausdrücklich, damit es nicht am Verify hängt.
        return reservedUntil >= DateTimeOffset.UtcNow && identity.ExpiresAt <= reservedUntil;
    }

    private static byte[] HmacSha256(string secret, string message)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return h.ComputeHash(Encoding.UTF8.GetBytes(message));
    }

    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        switch (t.Length % 4) { case 2: t += "=="; break; case 3: t += "="; break; }
        return Convert.FromBase64String(t);
    }
}
