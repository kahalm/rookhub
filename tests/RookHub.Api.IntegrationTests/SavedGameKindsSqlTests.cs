using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Laschen von <c>/games</c> gegen ECHTES MariaDB: die regulären Ausdrücke des PGN-Kopfs (<see cref="SavedGameKinds"/>)
/// laufen dort in PCRE2, InMemory rechnet sie in .NET; dazu <c>IN</c> über Quellen und Bedenkzeiten samt Zählung.
/// </summary>
public class SavedGameKindsSqlTests(SavedGameKindsSqlFixture fixture)
    : IAsyncLifetime, IClassFixture<SavedGameKindsSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> UserAsync()
    {
        await using var db = fixture.Schema.NewContext();
        var u = new AppUser { Username = "kinds", Email = "k@test.invalid", PasswordHash = "x" };
        db.AppUsers.Add(u);
        await db.SaveChangesAsync();
        return u.Id;
    }

    private async Task<int> AddAsync(int userId, string token, string source, string pgn, int? club = null, string? tc = null)
    {
        await using var db = fixture.Schema.NewContext();
        var g = new SavedGame
        {
            UserId = userId, Source = source, White = "A", Black = "B", Pgn = pgn, ShareToken = token, MoveCount = 2,
            HeadersScanned = true, LeagueClubGameId = club, TimeControl = tc,
        };
        db.SavedGames.Add(g);
        await db.SaveChangesAsync();
        return g.Id;
    }

    private async Task<List<int>> ListAsync(int userId, string kind)
    {
        await using var db = fixture.Schema.NewContext();
        return (await new SavedGameListing(db).ListAsync(userId, 200, null, kind)).Select(g => g.Id).OrderBy(i => i).ToList();
    }

    [MySqlFact]
    public async Task Kinds_TranslateAndBehaveLikeInMemory()
    {
        var user = await UserAsync();
        var com = await AddAsync(user, "k1", "chess.com", "[Event \"Live Chess\"]\n[Site \"Chess.com\"]\n\n1. e4 *\n", club: 9);
        var li = await AddAsync(user, "k2", "lichess", "[Site \"https://lichess.org/x\"]\n\n1. e4 *\n");
        var linked = await AddAsync(user, "k3", "pgn", "1. e4 *\n", club: 1);
        var sheet = await AddAsync(user, "k4", "scoresheet", "1. e4 *\n");
        var board = await AddAsync(user, "k5", "pgn", "[White \"x\"]\r\n[Event \"Schwazer Stadtmst. 2020\"]\r\n\r\n1. e4 *\r\n", tc: "5400+30");
        await AddAsync(user, "k6", "pgn", "[Event \"?\"]\n[Site \"?\"]\n\n1. e4 {[Event \"Liga\"]} *\n");
        await AddAsync(user, "k7", "pgn", "[Event \"iChessOne offline game\"]\n[Site \"?\"]\n\n1. e4 *\n", tc: "-");
        await AddAsync(user, "k8", "pgn", "[Event \"rated bullet game\"]\n[Site \"https://lichess.org/AgKIX7a9\"]\n\n1. e4 *\n", tc: "120+1");
        await AddAsync(user, "k9", "pgn", "[Event \"Blitz Open\"]\n\n1. e4 *\n", tc: "180+2");
        await AddAsync(user, "k10", "rookhub", "1. e4 *\n");

        Assert.Equal(new[] { com, li }, await ListAsync(user, "online"));
        Assert.Equal(new[] { linked, sheet, board }, await ListAsync(user, "otb"));
        Assert.Equal(10, (await ListAsync(user, "all")).Count);

        await using var db = fixture.Schema.NewContext();
        var counts = await new SavedGameListing(db).KindCountsAsync(user);
        Assert.Equal((10, 2, 3), (counts.All, counts.Online, counts.Otb));
    }
}

public sealed class SavedGameKindsSqlFixture() : MariaDbClassFixture("gamekinds", withApp: false);
