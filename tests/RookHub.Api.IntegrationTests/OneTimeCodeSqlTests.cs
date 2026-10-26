using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Einmal-Codes gegen ECHTES MariaDB (Codereview A1-008): das bedingte <c>UPDATE … WHERE UsedAt IS NULL</c> (ExecuteUpdate,
/// InMemory kennt es nicht) lässt von zwei gleichzeitigen Einlösungen nur eine gelingen — auch wenn die zweite die Zeile
/// schon offen geladen hat.
/// </summary>
public class OneTimeCodeSqlTests(OneTimeCodeSqlFixture fixture) : IAsyncLifetime, IClassFixture<OneTimeCodeSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static AuthHandoffService Handoff(AppDbContext db)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "TestSecretKeyThatIsAtLeast32Characters!", ["Jwt:Issuer"] = "TestIssuer", ["Jwt:Audience"] = "TestAudience",
        }).Build();
        return new AuthHandoffService(db, new AuthService(db, config, NullLogger<AuthService>.Instance),
            NullLogger<AuthHandoffService>.Instance);
    }

    [MySqlFact]
    public async Task Handoff_SecondRedeemWithAStaleRow_FailsOnMariaDb()
    {
        await using var dbA = fixture.Schema.NewContext();
        var user = new AppUser { Username = "race", Email = "race@test.invalid", PasswordHash = "x" };
        dbA.AppUsers.Add(user);
        await dbA.SaveChangesAsync();
        var code = await Handoff(dbA).IssueAsync(user.Id);
        Assert.Null((await dbA.AuthHandoffTokens.SingleAsync()).UsedAt);   // A hat die Zeile offen geladen

        await using (var dbB = fixture.Schema.NewContext())
            Assert.NotNull(await Handoff(dbB).RedeemAsync(code));

        Assert.Null(await Handoff(dbA).RedeemAsync(code));
    }
}

public sealed class OneTimeCodeSqlFixture() : MariaDbClassFixture("onetime", withApp: false);
