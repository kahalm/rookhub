using RookHub.Api.DTOs.Library;
using RookHub.Api.Models;
using RookHub.Api.Services.Library;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Meisterpartien-Suche (1.038.0) gegen ECHTES MariaDB: InMemory kennt weder MATCH … AGAINST noch die Stoppwörter des
/// Volltext-Index, und die ECO-Bereiche/Jahresfilter laufen dort in .NET statt in SQL.
/// </summary>
public class LibraryGameSearchSqlTests(LibraryGameSearchSqlFixture fixture)
    : IAsyncLifetime, IClassFixture<LibraryGameSearchSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> AddAsync(string white, string black, short? year, string result, string eco, string evt,
        int score = 50, LibraryGameStatus status = LibraryGameStatus.New, string annotator = "Aagaard, Jacob")
    {
        await using var db = fixture.Schema.NewContext();
        var g = new LibraryGame
        {
            White = white, Black = black, PlayedYear = year, Result = result, Eco = eco, Event = evt, Score = score,
            Status = status, Annotator = annotator, SourceFile = "t.pgn", MovesHash = Guid.NewGuid().ToString("N"), Pgn = "1. e4 *",
            SearchText = LibraryGameReader.SearchTextOf(white, black, evt, annotator),
        };
        db.LibraryGames.Add(g);
        await db.SaveChangesAsync();
        return g.Id;
    }

    private async Task<LibraryGameSearchPageDto> SearchAsync(LibraryGameSearchQuery q)
    {
        await using var db = fixture.Schema.NewContext();
        return await new LibraryGameSearchService(db).SearchAsync(q);
    }

    private async Task<List<int>> IdsAsync(LibraryGameSearchQuery q) => (await SearchAsync(q)).Items.Select(i => i.Id).ToList();

    [MySqlFact]
    public async Task Search_TranslatesAndFiltersLikeInMemory()
    {
        var a = await AddAsync("Kasparov, Garry", "Karpov, Anatoly", 1985, "1-0", "B44", "World-ch Moscow", score: 80);
        var b = await AddAsync("Karpov, Anatoly", "Kasparov, Garry", 1990, "1/2-1/2", "C92", "World-ch Lyon/New York", score: 60);
        var c = await AddAsync("Fischer, Robert", "Spassky, Boris", 1972, "0-1", "E56", "The Match of the Century", score: 70);
        await AddAsync("Tal, Mihail", "Botvinnik, Mikhail", 1960, "1-0", "B18", "World-ch", annotator: "Kasparov, Garry");
        await AddAsync("Kasparov, Garry", "Deep Blue", 1997, "0-1", "B17", "Man vs Machine", status: LibraryGameStatus.Duplicate);

        // Spieler: Weiß ODER Schwarz, nicht der Kommentator; Dubletten fallen heraus.
        Assert.Equal(new[] { a, b }, await IdsAsync(new() { Player = "KASPAROV" }));
        Assert.Equal(new[] { a, b }, await IdsAsync(new() { Player = "kaspa karpov" }));
        // Freitext trifft auch den Kommentator.
        Assert.Equal(3, (await SearchAsync(new() { Q = "kasparov" })).Total);
        // Stoppwort „the“ steht nicht im Volltext-Index: es filtert nicht (sonst gäbe es NIE einen Treffer) und wird gemeldet.
        var century = await SearchAsync(new() { Event = "the century" });
        Assert.Equal(new[] { c }, century.Items.Select(i => i.Id));
        Assert.Equal(new[] { "the" }, century.Ignored);
        Assert.Equal(new[] { a, b }, await IdsAsync(new() { Event = "world-ch", YearFrom = 1980 }));
        Assert.Equal(new[] { c }, await IdsAsync(new() { YearFrom = 1970, YearTo = 1975 }));
        Assert.Equal(new[] { b }, await IdsAsync(new() { Result = "draw" }));
        Assert.Equal(new[] { a }, await IdsAsync(new() { Eco = "B4" }));
        Assert.Equal(new[] { b }, await IdsAsync(new() { Eco = "C60-C99" }));
        Assert.Empty(await IdsAsync(new() { Player = "100%" }));
        Assert.Equal(new[] { b, a }, await IdsAsync(new() { Player = "karpov", Sort = "newest" }));
        Assert.Equal(new[] { c }, await IdsAsync(new() { Player = "fischer", Eco = "E20-E59", Result = "0-1", YearTo = 1980 }));
    }
}

public sealed class LibraryGameSearchSqlFixture() : MariaDbClassFixture("libsearch", withApp: false);
