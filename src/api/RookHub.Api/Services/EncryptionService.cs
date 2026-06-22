using System.Security.Cryptography;
using System.Text;

namespace RookHub.Api.Services;

/// <summary>
/// Symmetrische Verschlüsselung sensibler per-User-Secrets, die RookHub in der DB hält
/// (aktuell: Chessable-Bearer). Schlüssel kommt aus <c>Encryption:Key</c>.
///
/// Neues Format (v2): <b>AES-GCM</b> (authentifiziert → erkennt Manipulation/falschen Schlüssel),
/// Key via <c>SHA256(key)</c> (32 Byte, kein schwaches Null-Padding). Ablage: <c>"v2:"</c> +
/// base64(nonce | tag | cipher).
///
/// Alt-Format (ohne Präfix): AES-CBC ohne MAC, Key via <c>PadRight('0')[..32]</c>. Wird zum
/// <b>Entschlüsseln weiterhin unterstützt</b>, damit bereits gespeicherte Bearer lesbar bleiben;
/// neu geschrieben wird ausschließlich v2. <see cref="TryDecrypt"/> liefert null statt zu werfen
/// (z. B. nach Key-Rotation → die Credentials-Seite 500t dann nicht mehr).
/// </summary>
public class EncryptionService
{
    private const int NonceSize = 12;   // AES-GCM Standard-Nonce
    private const int TagSize = 16;     // AES-GCM Auth-Tag
    private const string V2Prefix = "v2:";

    private readonly byte[] _key;        // SHA256(key) → 32 Byte (v2)
    private readonly byte[] _legacyKey;  // PadRight(32,'0')[..32] (Alt-CBC)

    public EncryptionService(IConfiguration configuration)
    {
        var keyString = configuration["Encryption:Key"]
            ?? throw new InvalidOperationException("Encryption:Key not configured");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(keyString));
        _legacyKey = Encoding.UTF8.GetBytes(keyString.PadRight(32, '0')[..32]);
    }

    /// <summary>Verschlüsselt mit AES-GCM. Ergebnis: <c>"v2:" + base64(nonce|tag|cipher)</c>.</summary>
    public string Encrypt(string plainText)
    {
        var plain = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(_key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag);

        var combined = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(combined, 0);
        tag.CopyTo(combined, NonceSize);
        cipher.CopyTo(combined, NonceSize + TagSize);
        return V2Prefix + Convert.ToBase64String(combined);
    }

    /// <summary>Entschlüsselt v2-(GCM) und Alt-(CBC) Ciphertexts. Wirft bei ungültigen Daten/falschem Schlüssel.</summary>
    public string Decrypt(string cipherText)
        => cipherText.StartsWith(V2Prefix, StringComparison.Ordinal)
            ? DecryptGcm(cipherText[V2Prefix.Length..])
            : DecryptLegacyCbc(cipherText);

    /// <summary>Wie <see cref="Decrypt"/>, aber liefert null statt zu werfen (robust gegen Key-Rotation/korrupte Daten).</summary>
    public string? TryDecrypt(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return null;
        try { return Decrypt(cipherText); }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private string DecryptGcm(string base64)
    {
        var combined = Convert.FromBase64String(base64);
        if (combined.Length < NonceSize + TagSize)
            throw new CryptographicException("Ciphertext too short.");
        var nonce = combined.AsSpan(0, NonceSize);
        var tag = combined.AsSpan(NonceSize, TagSize);
        var cipher = combined.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(_key, TagSize))
            aes.Decrypt(nonce, cipher, tag, plain);   // wirft bei Tag-Mismatch
        return Encoding.UTF8.GetString(plain);
    }

    private string DecryptLegacyCbc(string cipherText)
    {
        var fullCipher = Convert.FromBase64String(cipherText);
        if (fullCipher.Length < 16)
            throw new CryptographicException("Ciphertext too short.");
        using var aes = Aes.Create();
        aes.Key = _legacyKey;
        aes.IV = fullCipher[..16];
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(fullCipher, 16, fullCipher.Length - 16);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
