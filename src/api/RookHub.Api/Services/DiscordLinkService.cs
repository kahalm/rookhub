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
/// Ein eingelöstes Token gehört danach dem Konto, das es eingelöst hat (<see cref="IsRedeemedByOther"/>,
/// <see cref="MarkRedeemed"/>): der Bot hängt es an Links in DMs, und wer so einen Link weitergeleitet bekommt
/// oder auf einem geteilten Gerät findet, verknüpfte sonst nach einer Trennung die fremde Discord-ID mit dem
/// eigenen Konto (Codereview A1-011). Der Vermerk lebt im Speicher bis zum Ablauf des Tokens — wie die
/// Login-Bremse überlebt er keinen Neustart; ohne eigene Tabelle (Migration) bewusst nur so.
/// </summary>
public class DiscordLinkService
{
    private readonly string? _secret;
    /// <summary>SHA-256 des eingelösten Tokens → Konto, das es eingelöst hat. Eigener Cache, damit kein
    /// fremder <c>Compact</c>/Größendeckel die Vermerke vor dem Ablauf verdrängt.</summary>
    private readonly MemoryCache _redeemed = new(new MemoryCacheOptions());

    public DiscordLinkService(IConfiguration config)
    {
        // Ein Platzhalter aus den Beispiel-Dateien zählt wie „leer" = Feature aus: mit dem öffentlich
        // bekannten Wert könnte sich jeder ein Link-Token für eine fremde Discord-ID signieren.
        _secret = SecretConfigCheck.Usable(config["Discord:LinkSecret"]);
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

    /// <summary>Hat ein ANDERES Konto dieses Token schon eingelöst? Dasselbe Konto darf es erneut (Name nachziehen,
    /// Verknüpfung nach eigener Trennung wiederherstellen).</summary>
    public bool IsRedeemedByOther(string token, int userId)
        => _redeemed.TryGetValue(RedeemedKey(token), out int owner) && owner != userId;

    /// <summary>Nach erfolgreicher Verknüpfung: das Token gehört ab jetzt <paramref name="userId"/>, bis es abläuft.
    /// Ein schon vorhandener Vermerk bleibt (der erste Einlöser behält es).</summary>
    public void MarkRedeemed(string token, DiscordIdentity identity, int userId)
        => _redeemed.GetOrCreate(RedeemedKey(token), entry =>
        {
            entry.AbsoluteExpiration = identity.ExpiresAt;
            return userId;
        });

    private static string RedeemedKey(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

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
