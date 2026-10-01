using System.Security.Cryptography;
using System.Text;

namespace RookHub.Api.Services;

/// <summary>
/// Einmal-Geheimnisse, deren Rohwert genau einmal herausgeht und die in der Datenbank nur als SHA-256-Hash liegen
/// (API-Token, Passwort-Reset, Anmelde-Uebergabe). EINE Stelle fuer Zufall, Schreibweise und Hash — vorher stand
/// jede der beiden Funktionen dreimal im Code (Codereview 2026-09-29, A1-014). Das Format ist unveraendert:
/// Rohwert Base64URL ohne Padding, Hash SHA-256-Hex in Kleinbuchstaben (so liegen die bestehenden Zeilen).
/// </summary>
public static class SecretTokens
{
    /// <summary>Vorgabe: 32 Byte = 256 Bit → 43 Zeichen Base64URL.</summary>
    public const int DefaultBytes = 32;

    /// <summary>Neuer Rohwert aus <paramref name="bytes"/> kryptografischen Zufallsbytes, Base64URL ohne Padding.</summary>
    public static string NewRaw(int bytes = DefaultBytes)
    {
        var buf = new byte[bytes];
        RandomNumberGenerator.Fill(buf);
        return Convert.ToBase64String(buf)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>SHA-256-Hex (Kleinbuchstaben, 64 Zeichen) eines Rohwerts — so wird er gespeichert und nachgeschlagen.</summary>
    public static string Sha256Hex(string raw) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
