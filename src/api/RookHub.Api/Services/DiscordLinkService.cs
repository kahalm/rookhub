using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RookHub.Api.Services;

/// <summary>
/// Verifiziert die vom schach-bot signierten Discord-Verknüpfungs-Tokens.
/// Format (identisch im Bot): token = body + "." + sig
///   body = base64url(utf8(JSON {"id","u","exp"}))   (ohne Padding)
///   sig  = base64url(HMAC_SHA256(secret, body))      (ohne Padding)
/// </summary>
public class DiscordLinkService
{
    private readonly string? _secret;

    public DiscordLinkService(IConfiguration config)
    {
        _secret = config["Discord:LinkSecret"];
    }

    public bool Enabled => !string.IsNullOrEmpty(_secret);

    public record DiscordIdentity(string Id, string? Username);

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
            return new DiscordIdentity(id!, string.IsNullOrWhiteSpace(user) ? null : user);
        }
        catch
        {
            return null;
        }
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
