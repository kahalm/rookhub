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
///
/// Ohne Schlüssel (nur mit <c>Chessable:Enabled=false</c> erlaubt, siehe <see cref="ThrowIfKeyRequiredButMissing"/>)
/// lässt sich der Dienst trotzdem erzeugen und wirft erst beim Ver-/Entschlüsseln: sonst scheiterte schon die
/// Aktivierung jedes Controllers, der ihn irgendwo im Abhängigkeitsbaum hat — die ganze Extension-API, die
/// Engine-Karte und der Lochfinder antworteten 500 (Codereview A3-017).
/// </summary>
public class EncryptionService
{
    private const int NonceSize = 12;   // AES-GCM Standard-Nonce
    private const int TagSize = 16;     // AES-GCM Auth-Tag
    private const string V2Prefix = "v2:";

    private readonly byte[]? _key;        // SHA256(key) → 32 Byte (v2); null = kein Schlüssel konfiguriert
    private readonly byte[]? _legacyKey;  // PadRight(32,'0')[..32] (Alt-CBC)

    public EncryptionService(IConfiguration configuration)
    {
        // Leerstring genauso behandeln wie "fehlt": ein leerer Schlüssel liefe sonst durch und verschlüsselte
        // mit SHA256("") — einem öffentlich bekannten Fixwert. Ein gesetzter, aber leerer Wert bricht schon den
        // Start ab (Program.cs); hier wirft dann jede Ver-/Entschlüsselung statt scheinzuverschlüsseln.
        var keyString = configuration["Encryption:Key"];
        if (string.IsNullOrWhiteSpace(keyString)) return;
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(keyString));
        _legacyKey = Encoding.UTF8.GetBytes(keyString.PadRight(32, '0')[..32]);
    }

    /// <summary>
    /// Startprüfung: ohne <c>Encryption:Key</c> startet RookHub nur mit <c>Chessable:Enabled=false</c>. Der eigene
    /// Chessable-Weg speichert Bearer verschlüsselt und wäre ohne Schlüssel unbenutzbar. Ohne Chessable fällt nur das
    /// Speichern des Lichess-Engine-Zugangs aus (wirft beim Verschlüsseln), der Rest der API läuft.
    /// </summary>
    public static void ThrowIfKeyRequiredButMissing(IConfiguration configuration)
    {
        if (configuration["Encryption:Key"] is null && ChessableSwitch.IsEnabled(configuration))
            throw new InvalidOperationException(
                "Encryption:Key fehlt (ENCRYPTION_KEY). Mit dem eigenen Chessable-Weg (Chessable:Enabled, Vorgabe an) " +
                "ist er Pflicht, weil die Chessable-Bearer verschlüsselt gespeichert werden — einen echten Zufallswert " +
                "setzen (z. B. openssl rand -base64 48) oder Chessable:Enabled=false.");
    }

    private static byte[] RequireKey(byte[]? key) =>
        key ?? throw new InvalidOperationException("Encryption:Key not configured");

    /// <summary>Verschlüsselt mit AES-GCM. Ergebnis: <c>"v2:" + base64(nonce|tag|cipher)</c>.</summary>
    public string Encrypt(string plainText)
    {
        var key = RequireKey(_key);
        var plain = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize))
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

    /// <summary>Wie <see cref="Decrypt"/>, aber liefert null statt zu werfen (robust gegen Key-Rotation/korrupte Daten).
    /// Ohne konfigurierten Schlüssel ebenfalls null — wie nach einer Rotation ist dann nichts lesbar.</summary>
    public string? TryDecrypt(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText) || _key is null) return null;
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
        using (var aes = new AesGcm(RequireKey(_key), TagSize))
            aes.Decrypt(nonce, cipher, tag, plain);   // wirft bei Tag-Mismatch
        return Encoding.UTF8.GetString(plain);
    }

    private string DecryptLegacyCbc(string cipherText)
    {
        var legacyKey = RequireKey(_legacyKey);
        var fullCipher = Convert.FromBase64String(cipherText);
        if (fullCipher.Length < 16)
            throw new CryptographicException("Ciphertext too short.");
        using var aes = Aes.Create();
        aes.Key = legacyKey;
        aes.IV = fullCipher[..16];
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(fullCipher, 16, fullCipher.Length - 16);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
