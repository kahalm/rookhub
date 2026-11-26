using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers.Og;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Og;
using RookHub.Api.Services.Tournaments.Live;
using RookHub.Api.Tests.Tournaments.Live;

namespace RookHub.Api.Tests.Og;

/// <summary>
/// Link-Vorschau der Live-Bretter <c>/tournaments/{id}/live?round=&amp;team=&amp;player=</c> (Wunsch 2026-11-26: eine
/// bestimmte Ansicht an Gäste teilen). Titel mit Begegnung, Turnier und Runde; Stand und Bretterzahl nur aus dem, was der
/// Server ohnehin im Speicher hat — ein Vorschau-Abrufer öffnet nie einen Lichess-Strom und stößt keine Suche an.
/// </summary>
public class OgLiveBoardsTests : IDisposable
{
    private const string Linked = "9904438";
    private const string Unlinked = "9904439";
    private const string RoundId = "L9900001";
    private const string Base = "https://tournament.oberschmid.homes";

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeLichessLiveSource _src = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public OgLiveBoardsTests()
    {
        _db.TournamentBroadcastLinks.Add(new TournamentBroadcastLink { CrawlerTournamentId = Linked, TourId = "IjDgZVUR", TourName = "BL" });
        _db.SaveChanges();
        _src.Tours["IjDgZVUR"] = $$"""{"tour":{"id":"IjDgZVUR","name":"1. Bundesliga Austria 2026/2027"},"rounds":[{"id":"{{RoundId}}","name":"Round 1","ongoing":true}]}""";
        _src.RoundPgn[RoundId] = LivePgn.Game("g1", "Aditya Mittal", "Hagner, Bennet", "1. d4 { [%clk 1:30:59] }",
            whiteFide: "35042025", blackFide: "16224477", whiteTeam: "Pinggau-Friedberg", blackTeam: "SK Schwaz");
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    private sealed class FakeData : ILiveTournamentData
    {
        public int RoundCalls;
        public Task<CrTournament?> GetTournamentAsync(string id, CancellationToken ct) =>
            Task.FromResult<CrTournament?>(new CrTournament(id, "Turnier", "01.01.2020", null, 9));
        public Task<IReadOnlyList<CrPlayer>> GetPlayersAsync(string id, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CrPlayer>>([new CrPlayer("Aditya, Mittal", "35042025", 2616, "Pinggau-Friedberg", 2),
                new CrPlayer("Hagner, Bennet", "16224477", 2462, "SK Schwaz", 1)]);
        public async Task<CrRound> GetRoundAsync(string id, int round, CancellationToken ct)
        {
            RoundCalls++;
            return new(true, await GetPlayersAsync(id, ct), [], [new CrMatch(1, "Pinggau-Friedberg", "SK Schwaz", null, null)]);
        }
    }

    private readonly FakeData _data = new();

    private OgMetaService Meta(BroadcastLiveHub? hub) => new(null!, null!, null!, null!, _db, NullLogger<OgMetaService>.Instance,
        live: new TournamentLiveService(_db, _data, new BroadcastFinder(_src, _cache, NullLogger<BroadcastFinder>.Instance) { Pause = TimeSpan.Zero },
            _src, _cache, NullLogger<TournamentLiveService>.Instance),
        liveData: _data, liveHub: hub);

    [Theory]
    [InlineData("/tournaments/20/live", "20")]
    [InlineData("/tournaments/20/live?team=Schachklub%20Schwaz&round=2", "20")]
    [InlineData("/tournaments/1404438/live?player=Mittal", "1404438")]
    public void ParsePath_LiveOnTournamentSite(string path, string id)
    {
        Assert.Equal((OgMetaService.LiveKind, id), OgMetaService.ParsePath(path, OgIndexHtmlProvider.TurnierSite));
        Assert.Null(OgMetaService.ParsePath(path));                                       // RookHub kennt die Route nicht
        Assert.Null(OgMetaService.ParsePath(path, OgIndexHtmlProvider.LeagueHubSite));
    }

    [Fact]
    public async Task Team_FeedOpen_TitleWithMatchAndRound_DescriptionWithScoreAndBoards()
    {
        await using var hub = new BroadcastLiveHub(_src, null, NullLogger<BroadcastLiveHub>.Instance);
        await using var viewer = hub.Subscribe(RoundId, finished: false, wantsEval: false);
        for (var i = 0; i < 250 && hub.Peek(RoundId) is not { Count: > 0 }; i++) await Task.Delay(20);
        var opened = _src.StreamsOpened;

        var page = await Meta(hub).ResolvePageAsync($"/tournaments/{Linked}/live?round=1&team=SK%20Schwaz", Base, default,
            OgIndexHtmlProvider.TurnierSite);

        Assert.NotNull(page);
        Assert.Equal("Live: Pinggau-Friedberg – SK Schwaz · 1. Bundesliga Austria 2026/2027, Runde 1", page!.Title);
        Assert.Contains("Stand 0 : 0", page.Description);
        Assert.Contains("1 Brett übertragen", page.Description);
        Assert.Contains("Runde läuft gerade", page.Description);
        Assert.Equal($"{Base}/tournaments/{Linked}/live?round=1&team=SK%20Schwaz", page.CanonicalUrl);
        Assert.Equal($"{Base}/api/og/img/tournament/{Linked}.png", page.ImageUrl);
        Assert.Equal(opened, _src.StreamsOpened);                                       // die Vorschau öffnet keinen Strom
    }

    [Fact]
    public async Task NoTeam_NoViewers_GenericTitle_NoLichessStream()
    {
        await using var hub = new BroadcastLiveHub(_src, null, NullLogger<BroadcastLiveHub>.Instance);
        var page = await Meta(hub).ResolvePageAsync($"/tournaments/{Linked}/live", Base, default, OgIndexHtmlProvider.TurnierSite);

        Assert.NotNull(page);
        Assert.Equal("Live-Bretter · 1. Bundesliga Austria 2026/2027, Runde 1", page!.Title);   // ohne ?round= die laufende Runde
        Assert.StartsWith("1 Begegnung · Runde läuft gerade", page.Description);
        Assert.DoesNotContain("übertragen", page.Description);                            // ohne Zuschauer keine Partien im Speicher
        Assert.Equal(0, hub.OpenFeeds);
        Assert.Equal(0, _src.StreamsOpened);
        Assert.Equal(0, _src.RoundPgnCalls);
    }

    [Fact]
    public async Task Player_TitleNamesPlayer()
    {
        var page = await Meta(null).ResolvePageAsync($"/tournaments/{Linked}/live?round=1&player=Mittal,%20Aditya", Base, default,
            OgIndexHtmlProvider.TurnierSite);
        Assert.Equal("Live: Mittal, Aditya · 1. Bundesliga Austria 2026/2027, Runde 1", page!.Title);
    }

    [Fact]
    public async Task NotLinked_NoSearch_GenericCard()
    {
        var page = await Meta(null).ResolvePageAsync($"/tournaments/{Unlinked}/live", Base, default, OgIndexHtmlProvider.TurnierSite);
        Assert.NotNull(page);
        Assert.StartsWith("Live-Bretter · ", page!.Title);
        Assert.Equal(0, _src.SearchCalls);
        Assert.Equal(0, _data.RoundCalls);
        Assert.Null(await Meta(null).ResolvePageAsync("/tournaments/a%27b/live", Base, default, OgIndexHtmlProvider.TurnierSite));
    }

    [Fact]
    public async Task TeamFromUrl_IsEscapedInTags()
    {
        var page = await Meta(null).ResolvePageAsync($"/tournaments/{Linked}/live?team=%22%3E%3Cscript%3E", Base, default,
            OgIndexHtmlProvider.TurnierSite);
        var html = OgController.Inject("<html><head><title>x</title></head><body></body></html>", page!);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("Live: &quot;", html);
    }

    // 1.025.0: hinzugefügte Bretter (?add= derselben Runde, ?ext= fremde Übertragung) stehen im Titel — nur gezählt, kein Lichess-Abruf.
    [Fact]
    public async Task AddedBoards_TitleSaysHowManyMore_NoLichessLookup()
    {
        var page = await Meta(null).ResolvePageAsync(
            $"/tournaments/{Linked}/live?round=1&team=SK%20Schwaz&add=2.3,4.1,bad&ext=fnmEB1kH.RD8sbYw1", Base, default, OgIndexHtmlProvider.TurnierSite);
        Assert.Equal("Live: Pinggau-Friedberg – SK Schwaz + 3 weitere Bretter · 1. Bundesliga Austria 2026/2027, Runde 1", page!.Title);
        var one = await Meta(null).ResolvePageAsync($"/tournaments/{Linked}/live?round=1&player=Mittal&ext=fnmEB1kH.RD8sbYw1", Base, default,
            OgIndexHtmlProvider.TurnierSite);
        Assert.Equal("Live: Mittal + 1 weiteres Brett · 1. Bundesliga Austria 2026/2027, Runde 1", one!.Title);
        Assert.Equal(0, _src.TourOfRoundCalls);
        Assert.Equal(0, _src.RoundPgnCalls);
    }

    [Theory]
    [InlineData("2.3,4.1,7,AbCd1234", 4)]
    [InlineData("2.3,2.3,../x,1234.5", 1)]
    [InlineData(null, 0)]
    public void AddedBoards_CountsValidDistinct(string? csv, int n) => Assert.Equal(n, OgMetaService.AddedBoards(csv));

    [Theory]
    [InlineData(2.5, "2,5")]
    [InlineData(3, "3")]
    [InlineData(0, "0")]
    public void Score_GermanDecimal(decimal v, string expected) => Assert.Equal(expected, OgMetaService.Score(v));

    [Fact]
    public void RoundState_StartTimeInViennaTime()
    {
        var info = new LiveRoundInfo(2, "x", "Round 2", new DateTime(2026, 11, 26, 13, 0, 0, DateTimeKind.Utc), false, false);
        Assert.Equal("Beginn 26.11.2026, 14:00", OgMetaService.RoundState(info));
        Assert.Equal("Runde beendet", OgMetaService.RoundState(info with { Finished = true }));
    }
}
