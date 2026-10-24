using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Tests.Social;

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

    /// <summary>Neuer Kontext auf einer benannten InMemory-DB — zwei Kontexte auf demselben Namen stehen für
    /// „vor und nach dem Neustart".</summary>
    private static AppDbContext Db(string name)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task ReleaseAsync(DiscordLinkService svc, AppDbContext db, string discordId, int userId)
    {
        await svc.MarkReleasedAsync(discordId, userId);
        await db.SaveChangesAsync();   // wie im ProfileService: im Save des Aufrufers
    }

    [Fact]
    public async Task ReleasedId_TokensIssuedBeforeRelease_BelongToThePreviousAccount()
    {
        // A1-011: Der Bot hängt an JEDEN DM-Link ein neues Token (30 Tage gültig). Nach dem Freiwerden der ID
        // galt jedes davon für jedes Konto — ein Vermerk nur für das zuerst eingelöste Token reichte nicht.
        using var db = Db(Guid.NewGuid().ToString());
        var svc = DiscordTokenTestHelper.Service(db: db);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(DiscordTokenTestHelper.FarFuture),
            svc.Verify(DiscordTokenTestHelper.Make("42", "x", DiscordTokenTestHelper.FarFuture))!.ExpiresAt);
        DiscordLinkService.DiscordIdentity Identity(string id, string name, TimeSpan remaining)
            => svc.Verify(DiscordTokenTestHelper.Make(id, name, DiscordTokenTestHelper.FromNow(remaining)))!;
        var first = Identity("42", "x", TimeSpan.FromDays(20));     // eingelöst vor 10 Tagen
        var later = Identity("42", "y", TimeSpan.FromDays(30));     // neuer Rätsellink, nie per POST eingelöst
        var fresh = Identity("42", "x", TimeSpan.FromDays(31));     // erst NACH dem Freiwerden ausgestellt
        var otherId = Identity("43", "z", TimeSpan.FromDays(30));

        Assert.False(await svc.IsReservedForOtherAsync(later, userId: 2));     // ohne Freiwerden kein Vermerk
        await ReleaseAsync(svc, db, "42", userId: 1);

        Assert.True(await svc.IsReservedForOtherAsync(first, userId: 2));
        Assert.True(await svc.IsReservedForOtherAsync(later, userId: 2));      // auch ein ANDERES Token derselben ID
        Assert.False(await svc.IsReservedForOtherAsync(later, userId: 1));     // der bisherige Inhaber darf wieder
        Assert.False(await svc.IsReservedForOtherAsync(fresh, userId: 2));     // frisches /link: für jedes Konto
        Assert.False(await svc.IsReservedForOtherAsync(otherId, userId: 2));

        await ReleaseAsync(svc, db, "42", userId: 2);                          // ein neueres Freiwerden ersetzt den Vermerk
        Assert.True(await svc.IsReservedForOtherAsync(later, userId: 1));
        Assert.False(await svc.IsReservedForOtherAsync(later, userId: 2));
        Assert.Equal(2, (await db.DiscordLinkReleases.AsNoTracking().SingleAsync()).UserId);   // eine Zeile je ID
    }

    [Fact]
    public async Task ReleasedId_TokenMaxAge_IsConfigurable()
    {
        // Discord:LinkTokenMaxAgeDays folgt der TTL im Bot (z. B. 1 nach dem Wechsel auf 24 h).
        using var db = Db(Guid.NewGuid().ToString());
        var svc = DiscordTokenTestHelper.Service(tokenMaxAgeDays: 1, db: db);
        await ReleaseAsync(svc, db, "42", userId: 1);

        var before = svc.Verify(DiscordTokenTestHelper.Make("42", "x", DiscordTokenTestHelper.FromNow(TimeSpan.FromHours(23))))!;
        var after = svc.Verify(DiscordTokenTestHelper.Make("42", "x", DiscordTokenTestHelper.FromNow(TimeSpan.FromDays(2))))!;
        Assert.True(await svc.IsReservedForOtherAsync(before, userId: 2));
        Assert.False(await svc.IsReservedForOtherAsync(after, userId: 2));
    }

    [Fact]
    public async Task ReleasedId_SurvivesRestart()
    {
        // Bis 0.829 lebte der Vermerk im Speicher des Dienstes: nach einem Neustart galt das weitergeleitete
        // Rätsel-Token wieder für jedes Konto. Jetzt steht er in DiscordLinkReleases.
        var name = Guid.NewGuid().ToString();
        using (var db = Db(name))
            await ReleaseAsync(DiscordTokenTestHelper.Service(db: db), db, "42", userId: 1);

        using var restarted = Db(name);
        var svc = DiscordTokenTestHelper.Service(db: restarted);   // neue Dienst-Instanz, neuer Kontext
        var old = svc.Verify(DiscordTokenTestHelper.Make("42", "x", DiscordTokenTestHelper.FromNow(TimeSpan.FromDays(30))))!;
        Assert.True(await svc.IsReservedForOtherAsync(old, userId: 2));
        Assert.False(await svc.IsReservedForOtherAsync(old, userId: 1));
    }

    [Fact]
    public async Task ReleasedId_ExpiredRelease_NoLongerProtects_AndIsCleanedUpOnNextRelease()
    {
        using var db = Db(Guid.NewGuid().ToString());
        var svc = DiscordTokenTestHelper.Service(db: db);
        // Vor 31 Tagen frei geworden: jedes damals ausgestellte Token (≤ 30 Tage) ist längst abgelaufen.
        db.DiscordLinkReleases.Add(new DiscordLinkRelease { DiscordId = "42", UserId = 1, ReleasedAt = DateTime.UtcNow.AddDays(-31) });
        db.DiscordLinkReleases.Add(new DiscordLinkRelease { DiscordId = "44", UserId = 1, ReleasedAt = DateTime.UtcNow.AddDays(-5) });
        await db.SaveChangesAsync();

        var token = svc.Verify(DiscordTokenTestHelper.Make("42", "x", DiscordTokenTestHelper.FromNow(TimeSpan.FromDays(1))))!;
        Assert.False(await svc.IsReservedForOtherAsync(token, userId: 2));

        await ReleaseAsync(svc, db, "43", userId: 3);   // ein neues Freiwerden räumt die abgelaufenen ab
        Assert.Equal(new[] { "43", "44" },
            (await db.DiscordLinkReleases.AsNoTracking().Select(r => r.DiscordId).ToListAsync()).OrderBy(x => x));
    }

    [Fact]
    public async Task ReleasedId_Upsert_KeepsTheYoungerRelease()
    {
        // Der jüngere Freiwerde-Zeitpunkt gewinnt: ein (z. B. durch Uhrversatz) jüngerer Vermerk wird nicht von
        // einem älteren überschrieben.
        using var db = Db(Guid.NewGuid().ToString());
        var svc = DiscordTokenTestHelper.Service(db: db);
        var younger = DateTime.UtcNow.AddMinutes(10);
        db.DiscordLinkReleases.Add(new DiscordLinkRelease { DiscordId = "42", UserId = 5, ReleasedAt = younger });
        await db.SaveChangesAsync();

        await ReleaseAsync(svc, db, "42", userId: 1);
        var row = await db.DiscordLinkReleases.AsNoTracking().SingleAsync();
        Assert.Equal(5, row.UserId);
        Assert.Equal(younger, row.ReleasedAt);
    }

    [Fact]
    public async Task ReleasedId_WithoutAccount_ProtectsAgainstEveryAccount()
    {
        // FK SetNull: verschwindet das Konto hart, bleibt der Vermerk und sperrt die alten Tokens für alle.
        using var db = Db(Guid.NewGuid().ToString());
        var svc = DiscordTokenTestHelper.Service(db: db);
        db.DiscordLinkReleases.Add(new DiscordLinkRelease { DiscordId = "42", UserId = null, ReleasedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var old = svc.Verify(DiscordTokenTestHelper.Make("42", "x", DiscordTokenTestHelper.FromNow(TimeSpan.FromDays(30))))!;
        Assert.True(await svc.IsReservedForOtherAsync(old, userId: 1));
        Assert.True(await svc.IsReservedForOtherAsync(old, userId: 2));
    }
}
