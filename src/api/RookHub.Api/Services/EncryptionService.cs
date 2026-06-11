using System.Security.Cryptography;
using System.Text;

namespace RookHub.Api.Services;

/// <summary>
/// Symmetrische AES-Verschlüsselung für sensible per-User-Secrets, die RookHub
/// in der DB hält (aktuell: Chessable-Bearer). Schlüssel kommt aus
/// <c>Encryption:Key</c>. IV wird je Verschlüsselung neu erzeugt und vor dem
/// Cipher abgelegt.
/// </summary>
public class EncryptionService
{
    private readonly byte[] _key;

    public EncryptionService(IConfiguration configuration)
    {
        var keyString = configuration["Encryption:Key"]
            ?? throw new InvalidOperationException("Encryption:Key not configured");
        _key = Encoding.UTF8.GetBytes(keyString.PadRight(32, '0')[..32]);
    }

    public string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + cipherBytes.Length];
        aes.IV.CopyTo(result, 0);
        cipherBytes.CopyTo(result, aes.IV.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        var fullCipher = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = _key;

        var iv = fullCipher[..16];
        var cipher = fullCipher[16..];

        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
