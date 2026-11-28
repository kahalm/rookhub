using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Library;
using RookHub.Api.Models;
using RookHub.Api.Services.Library;

namespace RookHub.Api.Tests.Library;

/// <summary>
/// Meisterpartien-Suche (1.038.0) gegen InMemory: Filterregeln, Deckel, Sichtbarkeit. Die echte Volltext-Abfrage prüft
/// <c>LibraryGameSearchSqlTests</c> gegen MariaDB.
/// </summary>
public class LibraryGameSearchServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly LibraryGameSearchService _svc;

    public LibraryGameSearchServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _svc = new LibraryGameSearchService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> AddAsync(string white, string black, short? year = 1951, string? result = "1-0", string? eco = "C65",
        string? evt = "Wijk aan Zee", int score = 50, LibraryGameStatus status = LibraryGameStatus.New, string? annotator = "Aagaard, Jacob")
    {
        var g = new LibraryGame
        {
            White = white, Black = black, PlayedYear = year, Result = result, Eco = eco, Event = evt, Score = score, Status = status,
            Annotator = annotator, SourceTitle = "CBM 104", Pgn = "1. e4 *",
            SearchText = LibraryGameReader.SearchTextOf(white, black, evt, annotator),
        };
        _db.LibraryGames.Add(g);
        await _db.SaveChangesAsync();
        return g.Id;
    }

    private async Task<List<int>> IdsAsync(LibraryGameSearchQuery q) => (await _svc.SearchAsync(q)).Items.Select(i => i.Id).ToList();

    [Fact]
    public async Task Player_MatchesWhiteOrBlack_NotTheAnnotatorOrEvent()
    {
        var asWhite = await AddAsync("Carlsen, Magnus", "Anand, Viswanathan");
        var asBlack = await AddAsync("Giri, Anish", "Carlsen, Magnus");
        await AddAsync("Tal, Mihail", "Petrosian, Tigran", annotator: "Carlsen, Magnus");
        await AddAsync("Tal, Mihail", "Smyslov, Vassily", evt: "Carlsen Invitational");

        Assert.Equal(new[] { asWhite, asBlack }.OrderBy(i => i), (await IdsAsync(new() { Player = "carlsen" })).OrderBy(i => i));
        // Mehrere Wörter: jedes bei Weiß ODER Schwarz — „Carlsen Anand“ = die Partien der beiden gegeneinander.
        Assert.Equal(new[] { asWhite }, await IdsAsync(new() { Player = "Carlsen Anand" }));
        // Der Freitext trifft dagegen auch Kommentator und Turnier.
        Assert.Equal(4, (await _svc.SearchAsync(new() { Q = "carlsen" })).Total);
    }

    [Fact]
    public async Task Event_Year_Result_Eco_Filter()
    {
        var a = await AddAsync("Kasparov, Garry", "Karpov, Anatoly", year: 1985, result: "1-0", eco: "B44", evt: "World-ch Moscow");
        var b = await AddAsync("Karpov, Anatoly", "Kasparov, Garry", year: 1990, result: "1/2-1/2", eco: "C92", evt: "World-ch Lyon/New York");
        var c = await AddAsync("Fischer, Robert", "Spassky, Boris", year: 1972, result: "0-1", eco: "E56", evt: "Reykjavik");

        Assert.Equal(new[] { a, b }.OrderBy(i => i), (await IdsAsync(new() { Event = "world-ch" })).OrderBy(i => i));
        Assert.Equal(new[] { a, c }.OrderBy(i => i), (await IdsAsync(new() { YearFrom = 1970, YearTo = 1985 })).OrderBy(i => i));
        // Vertauschter Bereich wird gerade gerückt.
        Assert.Equal(new[] { a, c }.OrderBy(i => i), (await IdsAsync(new() { YearFrom = 1985, YearTo = 1970 })).OrderBy(i => i));
        Assert.Equal(new[] { b }, await IdsAsync(new() { Result = "draw" }));
        Assert.Equal(new[] { c }, await IdsAsync(new() { Result = "black" }));
        Assert.Equal(new[] { a }, await IdsAsync(new() { Result = "1-0" }));
        Assert.Equal(new[] { a }, await IdsAsync(new() { Eco = "b4" }));
        Assert.Equal(new[] { b }, await IdsAsync(new() { Eco = "C60-C99" }));
        Assert.Equal(new[] { a, b }.OrderBy(i => i), (await IdsAsync(new() { Eco = "C99 – B20" })).OrderBy(i => i));
        Assert.Equal(new[] { c }, await IdsAsync(new() { Player = "fischer", Eco = "E", YearTo = 1980 }));
    }

    [Fact]
    public async Task DuplicatesAndRejected_StayHidden()
    {
        var keep = await AddAsync("Morphy, Paul", "Anderssen, Adolf");
        await AddAsync("Morphy, Paul", "Anderssen, Adolf", status: LibraryGameStatus.Duplicate);
        await AddAsync("Morphy, Paul", "Anderssen, Adolf", status: LibraryGameStatus.Rejected);
        Assert.Equal(new[] { keep }, await IdsAsync(new() { Player = "morphy" }));
        Assert.Equal(1, (await _svc.SearchAsync(new())).Total);
    }

    [Fact]
    public async Task Sort_BestNewestOldest()
    {
        var old = await AddAsync("A1", "B1", year: 1900, score: 90);
        var mid = await AddAsync("A2", "B2", year: 1950, score: 10);
        var young = await AddAsync("A3", "B3", year: 2000, score: 50);

        Assert.Equal(new[] { old, young, mid }, await IdsAsync(new()));
        Assert.Equal(new[] { young, mid, old }, await IdsAsync(new() { Sort = "newest" }));
        Assert.Equal(new[] { old, mid, young }, await IdsAsync(new() { Sort = "OLDEST" }));
    }

    [Fact]
    public async Task Paging_IsCapped_AndReportsRowsAndYear()
    {
        for (var i = 0; i < 7; i++) await AddAsync($"Spieler{i}", "Gegner", score: i);
        var page = await _svc.SearchAsync(new() { Page = 2, PageSize = 3 });
        Assert.Equal(7, page.Total);
        Assert.Equal(3, page.Items.Count);
        Assert.Equal(2, page.Page);
        Assert.Equal((short)1951, (short)page.Items[0].PlayedYear!.Value);
        Assert.Equal("CBM 104", page.Items[0].SourceTitle);

        var big = await _svc.SearchAsync(new() { PageSize = 10_000, Page = int.MaxValue });
        Assert.Equal(LibraryGameSearchService.MaxPageSize, big.PageSize);
        Assert.Equal(LibraryGameSearchService.MaxOffset / LibraryGameSearchService.MaxPageSize, big.MaxPage);
        Assert.Equal(big.MaxPage, big.Page);
        Assert.Empty(big.Items);
    }

    [Fact]
    public async Task ShortWords_AndBadInput_AreReportedAsIgnored()
    {
        var id = await AddAsync("Li, Chao", "So, Wesley");
        var page = await _svc.SearchAsync(new() { Player = "Li So wesley", Eco = "Sizilianisch", Result = "maybe" });
        Assert.Equal(new[] { id }, page.Items.Select(i => i.Id));
        Assert.Equal(new[] { "li", "so", "Sizilianisch", "maybe" }.OrderBy(s => s), page.Ignored.OrderBy(s => s));
    }

    [Fact]
    public void Parse_CapsWordsAndLength()
    {
        var input = LibraryGameSearchInput.Parse(new() { Q = string.Join(' ', Enumerable.Range(0, 20).Select(i => $"w{i:D2}")) });
        Assert.Equal(LibraryGameSearchInput.MaxTerms, input.AllWords.Count);
        Assert.Equal(20 - LibraryGameSearchInput.MaxTerms, input.Ignored.Count);

        var longText = LibraryGameSearchInput.Parse(new() { Player = new string('x', 500) });
        Assert.Equal(LibraryGameSearchInput.MaxFieldLength, longText.PlayerWords.Single().Length);
    }

    [Fact]
    public void FulltextTerm_EveryWordRequiredAsWordStart_OperatorsNeverReachIt()
    {
        var input = LibraryGameSearchInput.Parse(new() { Player = "+Saint-Amant*", Event = "(the) \"cup\"" });
        Assert.Equal("+saint* +amant* +cup*", LibraryGameSearchInput.FulltextTerm(input.AllWords));
        Assert.Equal(new[] { "the" }, input.Ignored);   // InnoDB-Stoppwort: steht nicht im Index
    }

    [Fact]
    public void Reader_FillsPlayedYear_FromDateOrEventDate()
    {
        var onlyYear = LibraryGameReader.From("x", new Dictionary<string, string> { ["Date"] = "1904.??.??" }, "1. e4 e5 *");
        Assert.Null(onlyYear!.PlayedOn);
        Assert.Equal((short)1904, onlyYear.PlayedYear);
        var fromEvent = LibraryGameReader.From("x", new Dictionary<string, string> { ["Date"] = "????.??.??", ["EventDate"] = "1921.09.05" }, "1. e4 e5 *");
        Assert.Equal((short)1921, fromEvent!.PlayedYear);
    }

    [Theory]
    [InlineData("1904.??.??", (short)1904)]
    [InlineData("2004.12.30", (short)2004)]
    [InlineData("????.??.??", null)]
    [InlineData("0999.01.01", null)]
    [InlineData("19", null)]
    [InlineData(null, null)]
    public void YearOf_ReadsTheYearEvenWithoutDay(string? date, short? expected)
        => Assert.Equal(expected, LibraryGameReader.YearOf(date));
}
