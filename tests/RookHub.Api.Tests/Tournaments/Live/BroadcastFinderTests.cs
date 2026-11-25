using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services.Tournaments.Live;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>Automatische Suche der Übertragung: tnr-Link in /top, sonst Suche + FIDE-Abgleich, Ruhezeit nach Fehlschlag.</summary>
public class BroadcastFinderTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);

    private static BroadcastFinder Finder(FakeLichessLiveSource src) =>
        new(src, new MemoryCache(new MemoryCacheOptions()), NullLogger<BroadcastFinder>.Instance) { Pause = TimeSpan.Zero };

    private static CrTournament T(string? date = null) => new("9904438", "1. BUNDESLIGA AUT 2026/2027", date, Now.AddHours(-2), 11);

    [Fact]
    public async Task Top_WithTnrLink_Wins()
    {
        var src = new FakeLichessLiveSource
        {
            Top = """{"active":[{"tour":{"id":"IjDgZVUR","name":"BL","info":{"standings":"https://s3.chess-results.com/tnr9904438.aspx"}}}]}""",
        };
        var tour = await Finder(src).FindAsync(T(), [], Now, default);
        Assert.Equal("IjDgZVUR", tour!.Id);
        Assert.Equal(0, src.SearchCalls);
    }

    [Fact]
    public async Task Search_FideOverlap_Accepts_ElseMissIsCached()
    {
        var src = new FakeLichessLiveSource();
        src.Searches["1. Bundesliga Austria"] = """{"currentPageResults":[{"tour":{"id":"AbCdEfGh","name":"1. Bundesliga Austria 2026/2027","dates":[1795611600000]}}]}""";
        src.Tours["AbCdEfGh"] = """{"tour":{"id":"AbCdEfGh","name":"BL"},"rounds":[{"id":"Rr000001","name":"Round 1","ongoing":true}]}""";
        var ids = new[] { "990101", "990102", "990103", "990104", "990105" };
        var finder = Finder(src);
        src.RoundPgn["Rr000001"] = string.Concat(Enumerable.Range(0, 3).Select(i =>
            LivePgn.Game($"g{i}", "A", "B", "1. e4", whiteFide: $"99020{i}", blackFide: i < 2 ? ids[i] : "990777")));
        Assert.Null(await finder.FindAsync(T(), ids, Now, default));           // 2 von 6 IDs → nein
        var calls = src.SearchCalls;
        Assert.Null(await finder.FindAsync(T(), ids, Now, default));           // Ruhezeit: keine neue Suche
        Assert.Equal(calls, src.SearchCalls);

        var finder2 = Finder(src);
        src.RoundPgn["Rr000001"] = string.Concat(Enumerable.Range(0, 2).Select(i =>
            LivePgn.Game($"g{i}", "A", "B", "1. e4", whiteFide: ids[i * 2], blackFide: ids[i * 2 + 1])));
        Assert.Equal("AbCdEfGh", (await finder2.FindAsync(T(), ids, Now, default))!.Id);
    }

    [Fact]
    public async Task NotCurrent_NoSearch()
    {
        var src = new FakeLichessLiveSource();
        Assert.Null(await Finder(src).FindAsync(T("01.01.2020-05.01.2020"), ["1", "2", "3", "4"], Now, default));
        Assert.Equal(0, src.SearchCalls);
    }

    [Fact]
    public void Queries_ExpandCountryCode_DropYears() =>
        Assert.Equal(["1. Bundesliga Austria", "Bundesliga Austria", "Bundesliga"], BroadcastFinder.Queries("1. BUNDESLIGA AUT 2026/2027"));

    [Theory]
    [InlineData("22.11.2026-30.11.2026", true)]
    [InlineData("20.11.2026", false)]
    [InlineData("26.11.2026", true)]
    public void LooksCurrent_ByDate(string date, bool expected) =>
        Assert.Equal(expected, BroadcastFinder.LooksCurrent(new CrTournament("1", "x", date, null, 1), Now));
}
