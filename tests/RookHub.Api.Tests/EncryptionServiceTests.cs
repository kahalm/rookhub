using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class EncryptionServiceTests
{
    private const string Key = "super-secret-test-key";

    private static EncryptionService Make(string key = Key)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Encryption:Key"] = key })
            .Build();
        return new EncryptionService(config);
    }

    /// <summary>Erzeugt einen Ciphertext im ALTEN Format (AES-CBC, PadRight-Key, ohne Präfix).</summary>
    private static string LegacyCbcEncrypt(string plain, string key = Key)
    {
        var legacyKey = Encoding.UTF8.GetBytes(key.PadRight(32, '0')[..32]);
        using var aes = Aes.Create();
        aes.Key = legacyKey;
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plain);
        var cipher = enc.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        var result = new byte[aes.IV.Length + cipher.Length];
        aes.IV.CopyTo(result, 0);
        cipher.CopyTo(result, aes.IV.Length);
        return Convert.ToBase64String(result);
    }

    [Fact]
    public void Encrypt_Decrypt_RoundTrip()
    {
        var svc = Make();
        const string secret = "Bearer abc.def.ghi";
        var cipher = svc.Encrypt(secret);

        Assert.StartsWith("v2:", cipher);
        Assert.NotEqual(secret, cipher);
        Assert.Equal(secret, svc.Decrypt(cipher));
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertextEachTime()
    {
        var svc = Make();
        // Frische Nonce je Aufruf → unterschiedliche Ciphertexts, beide entschlüsselbar.
        Assert.NotEqual(svc.Encrypt("x"), svc.Encrypt("x"));
    }

    [Fact]
    public void Decrypt_ReadsLegacyCbcCiphertext()
    {
        var svc = Make();
        var legacy = LegacyCbcEncrypt("legacy-bearer");
        Assert.Equal("legacy-bearer", svc.Decrypt(legacy));
    }

    [Fact]
    public void TryDecrypt_ReturnsNull_OnGarbageOrNull()
    {
        var svc = Make();
        Assert.Null(svc.TryDecrypt(null));
        Assert.Null(svc.TryDecrypt(""));
        Assert.Null(svc.TryDecrypt("not-base64!!"));
        Assert.Null(svc.TryDecrypt("v2:" + Convert.ToBase64String(new byte[] { 1, 2, 3 })));   // zu kurz
    }

    [Fact]
    public void TryDecrypt_ReturnsNull_OnWrongKey_ForV2()
    {
        var cipher = Make().Encrypt("secret");           // mit Key A
        var other = Make("a-completely-different-key");  // Key B
        Assert.Null(other.TryDecrypt(cipher));           // GCM-Tag schlägt fehl → null, kein Throw
    }

    [Fact]
    public void TryDecrypt_ReturnsValue_OnValidCiphertext()
    {
        var svc = Make();
        var cipher = svc.Encrypt("hello");
        Assert.Equal("hello", svc.TryDecrypt(cipher));
    }
    // ---- Ohne Schlüssel (Codereview A3-017) ----

    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithoutKey_ServiceCanBeCreated_AndOnlyEncryptingThrows(string? key)
    {
        // Früher warf schon der Konstruktor — damit scheiterte die Aktivierung von ExtensionController
        // (über ChessableImportService/RememberedPositionService), EngineController und Lochfinder: jeder Aufruf 500.
        var svc = new EncryptionService(Config(new() { ["Encryption:Key"] = key }));

        var ex = Assert.Throws<InvalidOperationException>(() => svc.Encrypt("secret"));
        Assert.Equal("Encryption:Key not configured", ex.Message);
        Assert.Throws<InvalidOperationException>(() => svc.Decrypt(Make().Encrypt("secret")));
        // Gespeichertes ist ohne Schlüssel nicht lesbar — wie nach einer Rotation null statt Ausnahme.
        Assert.Null(svc.TryDecrypt(null));
        Assert.Null(svc.TryDecrypt(Make().Encrypt("secret")));
    }

    [Fact]
    public void StartCheck_MissingKey_IsOnlyAllowedWithChessableDisabled()
    {
        // Vorgabe Chessable:Enabled=true → ohne Schlüssel kein Start (die Bearer werden verschlüsselt gespeichert).
        var ex = Assert.Throws<InvalidOperationException>(
            () => EncryptionService.ThrowIfKeyRequiredButMissing(Config(new())));
        Assert.Contains("Chessable:Enabled=false", ex.Message);
        Assert.Throws<InvalidOperationException>(() => EncryptionService.ThrowIfKeyRequiredButMissing(
            Config(new() { ["Chessable:Enabled"] = "true" })));

        EncryptionService.ThrowIfKeyRequiredButMissing(Config(new() { ["Chessable:Enabled"] = "false" }));
        EncryptionService.ThrowIfKeyRequiredButMissing(Config(new() { ["Encryption:Key"] = Key }));
    }

    [Fact]
    public void ProgramCs_RunsTheStartCheck()
    {
        // Der Host kommt im Testlauf nicht hoch (echte MariaDB nötig), also hier am Quelltext.
        var src = File.ReadAllText(ProgramCs());
        var blank = src.IndexOf("Encryption:Key ist gesetzt, aber leer", StringComparison.Ordinal);
        var check = src.IndexOf("EncryptionService.ThrowIfKeyRequiredButMissing(builder.Configuration)", StringComparison.Ordinal);
        Assert.True(blank >= 0 && check > blank, "Startprüfung auf fehlenden Encryption:Key fehlt in Program.cs");
    }

    private static string ProgramCs([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "src", "api", "RookHub.Api", "Program.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        Assert.Fail("Program.cs nicht gefunden");
        return "";
    }
}
