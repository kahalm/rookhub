using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Freitextsuche von <c>/games</c> gegen ECHTES MariaDB: InMemory rechnet LIKE und den regulären Ausdruck in .NET und sähe
/// weder eine fehlende Übersetzung von <c>Regex.IsMatch</c> noch einen Unterschied zwischen .NET und PCRE2.
/// </summary>
public class SavedGameSearchSqlTests(SavedGameSearchSqlFixture fixture)
    : IAsyncLifetime, IClassFixture<SavedGameSearchSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> UserAsync()
    {
        await using var db = fixture.Schema.NewContext();
        var u = new AppUser { Username = "searcher", Email = "s@test.invalid", PasswordHash = "x" };
        db.AppUsers.Add(u);
        await db.SaveChangesAsync();
        return u.Id;
    }

    private async Task<int> AddAsync(int userId, string token, string white, string pgn)
    {
        await using var db = fixture.Schema.NewContext();
        var g = new SavedGame { UserId = userId, Source = "pgn", White = white, Black = "Bert", Pgn = pgn, ShareToken = token, MoveCount = 2, HeadersScanned = true };
        db.SavedGames.Add(g);
        await db.SaveChangesAsync();
        return g.Id;
    }

    private async Task<List<int>> SearchAsync(int userId, string q)
    {
        await using var db = fixture.Schema.NewContext();
        return (await new SavedGameListing(db).ListAsync(userId, 200, q)).Select(g => g.Id).ToList();
    }

    [MySqlFact]
    public async Task Search_TranslatesAndBehavesLikeInMemory()
    {
        var user = await UserAsync();
        var carlsen = await AddAsync(user, "t1", "Magnus Carlsen", "[Event \"Wijk (A+B)\"]\n[Site \"Tröpolach\"]\n[ECO \"C65\"]\n[Result \"*\"]\n\n1. e4 {Kasparov} e5 *\n");
        var crlf = await AddAsync(user, "t2", "dr_who", "[White \"x\"]\r\n[Event \"Dortmund\"]\r\n[ECO \"B90\"]\r\n\r\n1. e4 *\r\n");
        await AddAsync(user, "t3", "drXwho", "[Event \"Open\"]\n\n1. d4 *\n");

        Assert.Equal(new[] { carlsen }, await SearchAsync(user, "CARLSEN wijk c65"));
        Assert.Equal(new[] { carlsen }, await SearchAsync(user, "(a+b)"));
        Assert.Equal(new[] { carlsen }, await SearchAsync(user, "tröpolach"));
        Assert.Equal(new[] { crlf }, await SearchAsync(user, "dortmund b90"));
        Assert.Equal(new[] { crlf }, await SearchAsync(user, "dr_who"));
        Assert.Empty(await SearchAsync(user, "kasparov"));
        Assert.Empty(await SearchAsync(user, "%"));
        Assert.Empty(await SearchAsync(user, "carlsen dortmund"));
    }
}

public sealed class SavedGameSearchSqlFixture() : MariaDbClassFixture("gamesearch", withApp: false);
