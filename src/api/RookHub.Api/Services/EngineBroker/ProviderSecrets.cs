using System.Security.Cryptography;
using System.Text;
using RookHub.Api.Models;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>Kennungen und Geheimnisse des Brokers — an EINER Stelle, damit Format und Länge nicht
/// auseinanderlaufen (die CSV-Spalte der Hintergrund-Engines hängt an der Länge der Kennung).</summary>
public static class ProviderSecrets
{
    private const string Alphanumeric = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary><c>sha256("providerSecret:" + secret)</c> als Hex (klein) — derselbe Selector wie
    /// lila-engine <c>ProviderSecret::selector</c>. Gespeichert wird nur er, nie das Secret.</summary>
    public static string Selector(string providerSecret)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("providerSecret:" + providerSecret));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary><c>rhe_</c> + 12 Zeichen <c>[A-Za-z0-9]</c>.</summary>
    public static string NewEngineId() =>
        ExternalEngineRegistration.IdPrefix
        + RandomNumberGenerator.GetString(Alphanumeric, ExternalEngineRegistration.IdRandomLength);

    /// <summary>Kennung eines abgeholten Auftrags (16 Zeichen, wie lila-engine <c>JobId::random</c>).</summary>
    public static string NewJobId() => RandomNumberGenerator.GetString(Alphanumeric, 16);

    /// <summary>32 Zufallsbytes, base64url ohne Auffüllung (43 Zeichen).</summary>
    public static string NewClientSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static bool IsLocalEngineId(string? engineId) =>
        engineId is not null && engineId.StartsWith(ExternalEngineRegistration.IdPrefix, StringComparison.Ordinal);
}
