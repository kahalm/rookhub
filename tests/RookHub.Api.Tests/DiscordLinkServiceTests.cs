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

    [Fact]
    public void Verify_PlaceholderSecret_FeatureOff()
    {
        // Platzhalter aus den öffentlichen .env-Beispielen = Feature aus: sonst signiert sich jeder, der
        // das Repo kennt, ein Link-Token für eine fremde Discord-ID und verknüpft sie mit seinem Konto.
        const string placeholder = "change_me_shared_with_schach_bot";
        var svc = DiscordTokenTestHelper.Service(secret: placeholder);
        var token = DiscordTokenTestHelper.Make("42", "x", DiscordTokenTestHelper.FarFuture, secret: placeholder);

        Assert.False(svc.Enabled);
        Assert.Null(svc.Verify(token));
    }

    [Fact]
    public void ReleasedId_TokensIssuedBeforeRelease_BelongToThePreviousAccount()
    {
        // A1-011: Der Bot hängt an JEDEN DM-Link ein neues Token (30 Tage gültig). Nach dem Freiwerden der ID
        // galt jedes davon für jedes Konto — ein Vermerk nur für das zuerst eingelöste Token reichte nicht.
        var svc = DiscordTokenTestHelper.Service();
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(DiscordTokenTestHelper.FarFuture),
            svc.Verify(DiscordTokenTestHelper.Make("42", "x", DiscordTokenTestHelper.FarFuture))!.ExpiresAt);
        DiscordLinkService.DiscordIdentity Identity(string id, string name, TimeSpan remaining)
            => svc.Verify(DiscordTokenTestHelper.Make(id, name, DiscordTokenTestHelper.FromNow(remaining)))!;
        var first = Identity("42", "x", TimeSpan.FromDays(20));     // eingelöst vor 10 Tagen
        var later = Identity("42", "y", TimeSpan.FromDays(30));     // neuer Rätsellink, nie per POST eingelöst
        var fresh = Identity("42", "x", TimeSpan.FromDays(31));     // erst NACH dem Freiwerden ausgestellt
        var otherId = Identity("43", "z", TimeSpan.FromDays(30));

        Assert.False(svc.IsReservedForOther(later, userId: 2));     // ohne Freiwerden kein Vermerk
        svc.MarkReleased("42", userId: 1);

        Assert.True(svc.IsReservedForOther(first, userId: 2));
        Assert.True(svc.IsReservedForOther(later, userId: 2));      // auch ein ANDERES Token derselben ID
        Assert.False(svc.IsReservedForOther(later, userId: 1));     // der bisherige Inhaber darf wieder
        Assert.False(svc.IsReservedForOther(fresh, userId: 2));     // frisches /link: für jedes Konto
        Assert.False(svc.IsReservedForOther(otherId, userId: 2));

        svc.MarkReleased("42", userId: 2);                          // ein neueres Freiwerden ersetzt den Vermerk
        Assert.True(svc.IsReservedForOther(later, userId: 1));
        Assert.False(svc.IsReservedForOther(later, userId: 2));
    }

    [Fact]
    public void ReleasedId_TokenMaxAge_IsConfigurable()
    {
        // Discord:LinkTokenMaxAgeDays folgt der TTL im Bot (z. B. 1 nach dem Wechsel auf 24 h).
        var svc = DiscordTokenTestHelper.Service(tokenMaxAgeDays: 1);
        svc.MarkReleased("42", userId: 1);

        var before = svc.Verify(DiscordTokenTestHelper.Make("42", "x", DiscordTokenTestHelper.FromNow(TimeSpan.FromHours(23))))!;
        var after = svc.Verify(DiscordTokenTestHelper.Make("42", "x", DiscordTokenTestHelper.FromNow(TimeSpan.FromDays(2))))!;
        Assert.True(svc.IsReservedForOther(before, userId: 2));
        Assert.False(svc.IsReservedForOther(after, userId: 2));
    }
}
