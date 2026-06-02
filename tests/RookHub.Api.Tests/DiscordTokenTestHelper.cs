using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Erzeugt Discord-Link-Tokens im exakt gleichen Format wie der schach-bot
/// (body.sig, base64url ohne Padding, HMAC-SHA256 über body) — dient als
/// C#-seitiger Round-Trip-Gegenpart zum Python-Token.
/// </summary>
internal static class DiscordTokenTestHelper
{
    public const string Secret = "shared-test-secret-1234567890";

    /// <summary>Weit in der Zukunft (Jahr 2286) — vermeidet Zeitabhängigkeit im Test.</summary>
    public const long FarFuture = 9999999999L;

    /// <summary>In der Vergangenheit (Jahr 2001).</summary>
    public const long Past = 1000000000L;

    public static DiscordLinkService Service(string? secret = Secret)
    {
        var dict = new Dictionary<string, string?>();
        if (secret != null) dict["Discord:LinkSecret"] = secret;
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        return new DiscordLinkService(config);
    }

    public static string Make(string id, string? username, long expUnix, string? secret = Secret)
    {
        var json = $"{{\"id\":\"{Escape(id)}\",\"u\":\"{Escape(username)}\",\"exp\":{expUnix}}}";
        var body = Base64Url(Encoding.UTF8.GetBytes(json));
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? Secret));
        var sig = Base64Url(h.ComputeHash(Encoding.UTF8.GetBytes(body)));
        return body + "." + sig;
    }

    private static string Escape(string? s) => (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Base64Url(byte[] b)
        => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
