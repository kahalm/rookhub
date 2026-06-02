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

    [Fact]
    public void Anonymous_ValidHeader_UsesSessionWithPrefix()
        => Assert.Equal("a:abcdef12-3456-7890", VisitorIdResolver.Resolve(false, null, "abcdef12-3456-7890"));

    [Fact]
    public void Authenticated_ButNoUsername_FallsBackToHeader()
        => Assert.Equal("a:deadbeef", VisitorIdResolver.Resolve(true, "", "deadbeef"));

    [Theory]
    [InlineData("not a guid!")]   // ungueltige Zeichen
    [InlineData("../etc/passwd")] // Injection-Versuch
    [InlineData("0123456789012345678901234567890123456789")] // > 36 Zeichen
    public void Anonymous_InvalidHeader_ReturnsNull(string header)
        => Assert.Null(VisitorIdResolver.Resolve(false, null, header));

    [Fact]
    public void Anonymous_NoHeader_ReturnsNull()
        => Assert.Null(VisitorIdResolver.Resolve(false, null, null));
}
