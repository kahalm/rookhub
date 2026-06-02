using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.Tests;

public class DiscordLinkServiceTests
{
    [Fact]
    public void Verify_ValidToken_ReturnsIdentity()
    {
        var svc = DiscordTokenTestHelper.Service();
        var token = DiscordTokenTestHelper.Make("123456789012345678", "Cooluser", DiscordTokenTestHelper.FarFuture);

        var id = svc.Verify(token);

        Assert.NotNull(id);
        Assert.Equal("123456789012345678", id!.Id);
        Assert.Equal("Cooluser", id.Username);
    }

    [Fact]
    public void Verify_EmptyUsername_ReturnsNullUsername()
    {
        var svc = DiscordTokenTestHelper.Service();
        var token = DiscordTokenTestHelper.Make("42", "", DiscordTokenTestHelper.FarFuture);

        var id = svc.Verify(token);

        Assert.NotNull(id);
        Assert.Equal("42", id!.Id);
        Assert.Null(id.Username);
    }

    [Fact]
    public void Verify_ExpiredToken_ReturnsNull()
    {
        var svc = DiscordTokenTestHelper.Service();
        var token = DiscordTokenTestHelper.Make("42", "x", DiscordTokenTestHelper.Past);

        Assert.Null(svc.Verify(token));
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsNull()
    {
        var svc = DiscordTokenTestHelper.Service();
        // Token mit ANDEREM Secret signiert → Signatur passt nicht.
        var token = DiscordTokenTestHelper.Make("42", "x", DiscordTokenTestHelper.FarFuture, secret: "a-different-secret");

        Assert.Null(svc.Verify(token));
    }

    [Fact]
    public void Verify_TamperedBody_ReturnsNull()
    {
        var svc = DiscordTokenTestHelper.Service();
        var token = DiscordTokenTestHelper.Make("42", "x", DiscordTokenTestHelper.FarFuture);
        var dot = token.LastIndexOf('.');
        // Body verändern, Signatur belassen → muss abgelehnt werden.
        var tampered = token[..dot] + "AA." + token[(dot + 1)..];

        Assert.Null(svc.Verify(tampered));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("noseparator")]
    [InlineData(".onlysig")]
    [InlineData("onlybody.")]
    public void Verify_Malformed_ReturnsNull(string? token)
    {
        var svc = DiscordTokenTestHelper.Service();
        Assert.Null(svc.Verify(token));
    }

    [Fact]
    public void Verify_AcceptsKnownPythonToken_RoundTrip()
    {
        // Golden-Vektor: vom schach-bot (Python core/discord_link.make_link_token) erzeugt mit
        // secret="shared-test-secret-1234567890", id="123456789012345678", u="Cooluser", exp=9999999999.
        // Verankert den Cross-Language-Round-Trip (Python signiert → C# verifiziert).
        // Identischer Vektor in schach-bot/tests/test_discord_link.py.
        const string pythonToken =
            "eyJpZCI6IjEyMzQ1Njc4OTAxMjM0NTY3OCIsInUiOiJDb29sdXNlciIsImV4cCI6OTk5OTk5OTk5OX0" +
            ".U2wXL2W7i08klm58xTSHnpy4S6FE0RYmvurRKuQrIsY";
        var svc = DiscordTokenTestHelper.Service();   // gleiches Secret

        var id = svc.Verify(pythonToken);

        Assert.NotNull(id);
        Assert.Equal("123456789012345678", id!.Id);
        Assert.Equal("Cooluser", id.Username);
    }

    [Fact]
    public void Verify_FeatureDisabled_ReturnsNull()
    {
        // Kein Secret konfiguriert → Feature inaktiv, jeder Token abgelehnt.
        var svc = DiscordTokenTestHelper.Service(secret: null);
        var token = DiscordTokenTestHelper.Make("42", "x", DiscordTokenTestHelper.FarFuture);

        Assert.False(svc.Enabled);
        Assert.Null(svc.Verify(token));
    }
}
