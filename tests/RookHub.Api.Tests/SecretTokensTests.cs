using System.Text.RegularExpressions;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Einmal-Token, ihr Hash und der BCrypt-Faktor an EINER Stelle (Codereview 2026-09-29, A1-014). Vorher standen
/// Erzeugung und SHA-256-Hex je dreimal (API-Token, Passwort-Reset, Anmelde-Uebergabe) und der Faktor 12 an drei
/// Stellen — ein Anheben in AuthService liess per Reset gesetzte Passwoerter still auf dem alten Faktor.
/// </summary>
public class SecretTokensTests
{
    [Fact]
    public void Sha256Hex_BleibtImGespeichertenFormat()
    {
        // Spiegeltest: die bestehenden Zeilen (UserApiTokens, PasswordResetTokens, AuthHandoffTokens) tragen genau
        // dieses Format — eine andere Schreibweise (Grossbuchstaben, Base64) liesse jeden alten Token ins Leere laufen.
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", SecretTokens.Sha256Hex("abc"));
        Assert.Equal(SecretTokens.Sha256Hex("rkh_x"), ApiTokenService.ComputeHash("rkh_x"));
    }

    [Fact]
    public void NewRaw_IstBase64UrlOhnePadding()
    {
        var a = SecretTokens.NewRaw();
        var b = SecretTokens.NewRaw();
        Assert.Matches(new Regex("^[A-Za-z0-9_-]{43}$"), a);   // 32 Byte
        Assert.NotEqual(a, b);
        Assert.Matches(new Regex("^[A-Za-z0-9_-]{22}$"), SecretTokens.NewRaw(16));
    }

    [Fact]
    public void PasswordHashing_NutztDenGemeinsamenFaktor()
    {
        var hash = PasswordHashing.Hash("geheim");
        Assert.Matches(new Regex(@"^\$2[abxy]\$" + PasswordHashing.WorkFactor.ToString("00") + @"\$"), hash);
        Assert.True(BCrypt.Net.BCrypt.Verify("geheim", hash));
    }

    private static string ApiRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "api", "RookHub.Api");
    }

    /// <summary>Wachhund: ein BCrypt-Hash entsteht nur in <see cref="PasswordHashing"/> — ein neuer Aufruf mit eigenem
    /// Faktor (oder der Library-Vorgabe) waere genau die Drift, die der Fund beschreibt.</summary>
    [Fact]
    public void BcryptHashPassword_NurInPasswordHashing()
    {
        var root = ApiRoot();
        var treffer = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(p => !p.StartsWith("bin/") && !p.StartsWith("obj/") && p != "Services/PasswordHashing.cs")
            .Where(p => File.ReadAllText(Path.Combine(root, p)).Contains("BCrypt.HashPassword("))
            .ToList();
        Assert.True(treffer.Count == 0,
            "BCrypt.HashPassword ausserhalb von PasswordHashing: " + string.Join(", ", treffer) +
            " — PasswordHashing.Hash verwenden, damit alle Hashes denselben Faktor tragen.");
    }

    /// <summary>Wachhund: die drei Einmal-Token-Dienste bauen Zufall und Hash nicht wieder selbst.</summary>
    [Theory]
    [InlineData("Services/ApiTokenService.cs")]
    [InlineData("Services/PasswordResetService.cs")]
    [InlineData("Services/AuthHandoffService.cs")]
    public void EinmalTokenDienste_NutzenSecretTokens(string pfad)
    {
        var text = File.ReadAllText(Path.Combine(ApiRoot(), pfad));
        Assert.DoesNotContain("SHA256", text.Replace("SHA-256", ""));
        Assert.DoesNotContain("RandomNumberGenerator", text);
        Assert.Contains("SecretTokens.", text);
    }
}
