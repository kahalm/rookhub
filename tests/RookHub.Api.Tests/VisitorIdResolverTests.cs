using RookHub.Api.Logging;

namespace RookHub.Api.Tests;

public class VisitorIdResolverTests
{
    [Fact]
    public void Authenticated_UsesUsernameWithPrefix()
        => Assert.Equal("u:admin", VisitorIdResolver.Resolve(true, "admin", null));

    [Fact]
    public void Authenticated_UsernameWinsOverHeader()
        => Assert.Equal("u:admin", VisitorIdResolver.Resolve(true, "admin", "abcdef12-3456"));

    /// <summary>F1-012: Die Anon-Session-Id schützt anonyme Puzzle-/Endlos-/E-Book-Stände — ins Log kommt nur ein gekürzter
    /// SHA-256 (16 Hex), stabil je Besucher (Unique Visits zählen weiter), aber nicht zurückrechenbar.</summary>
    [Fact]
    public void Anonymous_ValidHeader_LogsAPseudonym_NotTheSessionId()
    {
        const string id = "abcdef12-3456-7890-abcd-ef0123456789";
        var resolved = VisitorIdResolver.Resolve(false, null, id);

        Assert.Equal("a:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(id)))[..16], resolved);
        Assert.DoesNotContain("abcdef12", resolved);
        Assert.Equal(resolved, VisitorIdResolver.Resolve(false, null, id.ToUpperInvariant()));   // Schreibweise egal
    }

    [Fact]
    public void Authenticated_ButNoUsername_FallsBackToHeader()
        => Assert.Equal(VisitorIdResolver.Resolve(false, null, "00112233445566778899aabbccddeeff"),
            VisitorIdResolver.Resolve(true, "", "00112233445566778899aabbccddeeff"));

    [Theory]
    [InlineData("not a guid!")]   // ungueltige Zeichen
    [InlineData("../etc/passwd")] // Injection-Versuch
    [InlineData("deadbeef")]      // zu kurz (< 32 Zeichen → IDOR-Härtung)
    [InlineData("0123456789012345678901234567890123456789")] // > 36 Zeichen
    public void Anonymous_InvalidHeader_ReturnsNull(string header)
        => Assert.Null(VisitorIdResolver.Resolve(false, null, header));

    [Fact]
    public void Anonymous_NoHeader_ReturnsNull()
        => Assert.Null(VisitorIdResolver.Resolve(false, null, null));
}
