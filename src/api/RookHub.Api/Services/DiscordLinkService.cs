using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace RookHub.Api.Services;

/// <summary>
/// Verifiziert die vom schach-bot signierten Discord-Verknüpfungs-Tokens.
/// Format (identisch im Bot): token = body + "." + sig
///   body = base64url(utf8(JSON {"id","u","exp"}))   (ohne Padding)
///   sig  = base64url(HMAC_SHA256(secret, body))      (ohne Padding)
///
/// Freiwerden einer Discord-ID (Codereview A1-011): Der Bot hängt an JEDEN Link in einer DM ein neues Token,
/// gültig bis exp (Bot-<c>DEFAULT_TTL</c>, 30 Tage). Wird die ID frei (Trennen, Wechsel auf eine andere ID,
/// Kontolöschung — <see cref="MarkReleased"/>), merkt sich der Dienst das bisherige Konto und den Zeitpunkt.
/// Tokens, die bis dahin ausgestellt wurden (<c>exp &lt;= freiAm + maxAlter</c>), löst danach nur noch dieses
/// Konto ein (<see cref="IsReservedForOther"/>) — sonst verknüpfte, wer einen weitergeleiteten Rätsellink hat,
/// die fremde Discord-ID mit dem eigenen Konto. Ein frisches <c>/link</c> danach ist für jedes Konto gültig.
/// Der Vermerk lebt im Speicher bis <c>freiAm + maxAlter</c> und überlebt wie die Login-Bremse keinen Neustart;
/// ohne eigene Tabelle (Migration) bewusst nur so. Eine NIE verknüpfte ID schützt er nicht (dagegen hilft nur
/// eine kürzere Bot-TTL).
/// </summary>
public class DiscordLinkService
{
    /// <summary>Standard für <c>Discord:LinkTokenMaxAgeDays</c> = Bot-<c>DEFAULT_TTL</c> (30 Tage).</summary>
    public const double DefaultTokenMaxAgeDays = 30;

    private readonly string? _secret;
    /// <summary>Längste Gültigkeit, die der Bot einem Token gibt: ein Token mit <c>exp &lt;= t + maxAlter</c>
    /// wurde spätestens zum Zeitpunkt t ausgestellt.</summary>
    private readonly TimeSpan _tokenMaxAge;
    /// <summary>Discord-ID → bisheriges Konto + Zeitpunkt des Freiwerdens. Eigener Cache, damit kein
    /// fremder <c>Compact</c>/Größendeckel die Vermerke vor dem Ablauf verdrängt.</summary>
    private readonly MemoryCache _released = new(new MemoryCacheOptions());

    private sealed record Release(int UserId, DateTimeOffset At);

    public DiscordLinkService(IConfiguration config)
    {
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

    /// <summary>Die Discord-ID <paramref name="discordId"/> war bis eben mit <paramref name="userId"/> verknüpft und ist
    /// jetzt frei (erst NACH dem erfolgreichen Speichern aufrufen). Ein neueres Freiwerden ersetzt den Vermerk.</summary>
    public void MarkReleased(string discordId, int userId)
    {
        var at = DateTimeOffset.UtcNow;
        _released.Set(discordId, new Release(userId, at), at + _tokenMaxAge);
    }

    /// <summary>Wurde das Token ausgestellt, als die Discord-ID noch einem ANDEREN Konto gehörte? Dann löst es nur
    /// dieses Konto ein (Wiederverknüpfen nach eigener Trennung); für alle anderen gilt es wie ein ungültiges.</summary>
    public bool IsReservedForOther(DiscordIdentity identity, int userId)
        => _released.TryGetValue(identity.Id, out Release? release)
           && release!.UserId != userId
           && identity.ExpiresAt <= release.At + _tokenMaxAge;

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
