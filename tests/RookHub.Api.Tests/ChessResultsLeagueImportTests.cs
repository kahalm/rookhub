using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// chess-results-Liga je Saison über den Crawler einspielen (Österreichische Bundesliga, 0.720.0): Probelauf zählt, Import legt
/// Turnier-Zeile + Seiten an (stabile Brettpaarungs-Ids), Fehlerfälle. Fixture <c>Fixtures/ChessResults/league-1234567.json</c> hat die
/// Form der Crawler-Antwort (aufgenommen an der 2. Bundesliga West 2026/27) mit erfundenen Vereinen und Namen: vier Teams, Runden
/// Fr–So, Runde 1 gespielt, vier Bretter, 24 Gemeldete (20 mit FIDE-ID).
/// </summary>
public class ChessResultsLeagueImportTests : IDisposable
{
    private const int Tnr = 1234567;
    private static readonly DateTime Now = new(2026, 10, 8, 6, 0, 0, DateTimeKind.Utc);
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly List<string> _calls = new();
    public void Dispose() => _db.Dispose();

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ChessResults", $"league-{Tnr}.json"), Encoding.UTF8);

    private ChessResultsLeagueImport Import(Func<HttpRequestMessage, HttpResponseMessage>? crawler = null)
    {
        crawler ??= r => r.RequestUri!.AbsolutePath == $"/api/league/{Tnr}"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture(), Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
        var client = new HttpClient(new StubHandler(r => { _calls.Add(r.RequestUri!.AbsolutePath); return crawler(r); }))
            { BaseAddress = new Uri("http://crawler/") };
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        return new ChessResultsLeagueImport(_db, league, new OneClientFactory(client), NullLogger<ChessResultsLeagueImport>.Instance, () => Now);
    }

    private static ChessResultsLeagueImport.Request Req(int level = LeagueLevels.Bundesliga2, string season = "2026/27") =>
        new(Tnr, season, level, "2. Bundesliga", "West");

    [Fact]
    public async Task DryRun_CountsWithoutWriting()
    {
        // 91001 steht schon in einer TMM-Meldeliste (Landesliga), 90001 hat schon eine Spielerkarte.
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 7, Season = "2026/27", Level = LeagueLevels.TirolLandesliga, League = "Landesliga", Stage = "Liga" });
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = "Almstadt 2", Name = "Doppel, Dora", NameKey = "doppel, dora", FideId = "910001" });
        _db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = "90001", Name = "Bergdorfer, Anton" });
        await _db.SaveChangesAsync();

        var r = await Import().ImportAsync(Req(), dryRun: true, default);

        Assert.True(r.DryRun);
        Assert.Equal(("2. Bundesliga West 2026/27", "2026/27", 2), (r.Name, r.Season, r.Level));
        var c = r.Counts;
        Assert.Equal((3, 1, 6, 4), (c.Rounds, c.RoundsPlayed, c.Matches, c.Teams));
        Assert.Equal((24, 8, 0, 4), (c.BoardGames, c.BoardGamesPlayed, c.BoardPlayersUnmatched, c.Boards));
        Assert.Equal((24, 20), (c.Players, c.PlayersWithFide));
        Assert.Equal(19, c.FideNotInLeagues);           // 910001 kennt die Landesliga schon
        Assert.Equal(19, c.FideWithoutCard);            // 90001 hat eine Karte
        Assert.Equal(("16.10.2026", "18.10.2026", 1), (c.FirstRound, c.LastRound, c.RoundBlocks));   // Fr–So = ein Block
        Assert.Equal(new[] { $"/api/league/{Tnr}" }, _calls);
        Assert.False(await _db.LeagueTournaments.AnyAsync(t => t.Tnr == Tnr));
        Assert.False(await _db.LeagueMatches.AnyAsync());
    }

    [Fact]
    public async Task Import_WritesTheLeague_AndAgainKeepsTheBoardIds()
    {
        var r = await Import().ImportAsync(Req(), dryRun: false, default);

        Assert.False(r.DryRun);
        var t = await _db.LeagueTournaments.SingleAsync(x => x.Tnr == Tnr);
        Assert.Equal(("2. Bundesliga West 2026/27", "2026/27", 2, "2. Bundesliga", "West", "Liga"), (t.Name, t.Season, t.Level, t.League, t.Grp, t.Stage));
        Assert.Equal((null, null, 3, "16.10.2026", "18.10.2026"), (t.Source, t.SourceRef, t.Rounds, t.Start, t.End));
        Assert.Equal(Now, t.UpdatedAt);
        Assert.Equal(6, await _db.LeagueMatches.CountAsync(m => m.Tnr == Tnr));
        Assert.Equal(3, await _db.LeagueRounds.CountAsync(x => x.Tnr == Tnr));
        Assert.Equal(24, await _db.LeaguePlayers.CountAsync(p => p.Tnr == Tnr));
        // Brettpaarung ↔ Meldeliste verknüpft (FIDE-ID, Meldebrett, Elo)
        var g = await _db.LeagueGames.SingleAsync(x => x.Tnr == Tnr && x.Round == 1 && x.MatchNo == 1 && x.Board == 1);
        Assert.Equal(("90001", 1, 2260), (g.HomeFide, g.HomeRb, g.HomeElo));
        Assert.Equal(1, r.Views);                                        // die laufende Saison ist gerechnet
        var view = await _db.LeagueViews.SingleAsync(v => v.Tnr == Tnr);
        Assert.Contains("\"level\":2", view.Json);

        var ids = await _db.LeagueGames.Where(x => x.Tnr == Tnr).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();
        await Import().ImportAsync(Req(), dryRun: false, default);
        Assert.Equal(ids, await _db.LeagueGames.Where(x => x.Tnr == Tnr).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync());
        Assert.Equal(1, await _db.LeagueTournaments.CountAsync(x => x.Tnr == Tnr));
    }

    [Fact]
    public async Task Import_OlderSeason_NoViewForIt()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 7, Season = "2026/27", Level = LeagueLevels.TirolLandesliga, League = "Landesliga", Stage = "Liga" });
        await _db.SaveChangesAsync();
        await Import().ImportAsync(Req(LeagueLevels.Bundesliga2, "2025/26"), dryRun: false, default);
        Assert.Equal("2025/26", (await _db.LeagueTournaments.SingleAsync(x => x.Tnr == Tnr)).Season);
        Assert.False(await _db.LeagueViews.AnyAsync(v => v.Tnr == Tnr));   // nur die laufende Saison bekommt Ansichten
    }

    [Theory]
    [InlineData(0, "2026/27", 2, "2. Bundesliga")]
    [InlineData(900_002_573, "2026/27", 2, "2. Bundesliga")]   // Ligamanager-Bereich
    [InlineData(Tnr, "2026/28", 2, "2. Bundesliga")]
    [InlineData(Tnr, "26/27", 2, "2. Bundesliga")]
    [InlineData(Tnr, "2026/27", 0, "2. Bundesliga")]
    [InlineData(Tnr, "2026/27", 7, "2. Bundesliga")]          // Bayern-Stufe, keine Tiroler
    [InlineData(Tnr, "2026/27", 2, " ")]
    public async Task Import_InvalidRequest_Throws_WithoutAsking(int tnr, string season, int level, string league)
    {
        await Assert.ThrowsAsync<ChessResultsLeagueImport.InvalidException>(() =>
            Import().ImportAsync(new(tnr, season, level, league), dryRun: true, default));
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task Import_NumberOfAnotherSource_Conflict()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = Tnr, Season = "2026/27", Level = 3, League = "X", Source = LigamanagerSource.Source });
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<ChessResultsLeagueImport.ConflictException>(() => Import().ImportAsync(Req(), dryRun: false, default));
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task Import_EmptyPages_NotFound()
    {
        var empty = $"{{\"tnr\":{Tnr},\"matches\":[],\"games\":[],\"roundDates\":{{}},\"roster\":[],\"stats\":[]}}";
        await Assert.ThrowsAsync<ChessResultsLeagueImport.NotFoundException>(() =>
            Import(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(empty, Encoding.UTF8, "application/json") })
                .ImportAsync(Req(), dryRun: true, default));
    }

    [Fact]
    public async Task Import_EmptyRosterWithExistingRoster_Incomplete_KeepsTheLeague()
    {
        await Import().ImportAsync(Req(), dryRun: false, default);
        var node = System.Text.Json.Nodes.JsonNode.Parse(Fixture())!;
        node["roster"] = new System.Text.Json.Nodes.JsonArray();         // Drosselseite mitten im Lauf: Meldeliste leer
        var noRoster = node.ToJsonString();
        await Assert.ThrowsAsync<ChessResultsLeagueImport.IncompleteException>(() =>
            Import(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(noRoster, Encoding.UTF8, "application/json") })
                .ImportAsync(Req(), dryRun: false, default));
        Assert.Equal(24, await _db.LeaguePlayers.CountAsync(p => p.Tnr == Tnr));
    }

    // ---- Endpunkt ----------------------------------------------------------------------------------

    [Fact]
    public async Task Endpoint_MapsTheOutcomes()
    {
        var ctl = new LeagueController(new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance), null!, null!);
        var ok = await ctl.ChessResultsImport(new(Tnr, "2026/27", 2, "2. Bundesliga", "West", null, null), dryRun: true, Import(), default);
        var res = Assert.IsType<ChessResultsLeagueImport.ImportResult>(Assert.IsType<OkObjectResult>(ok).Value);
        Assert.True(res.DryRun);

        Assert.IsType<BadRequestObjectResult>(await ctl.ChessResultsImport(new(Tnr, "2026/27", null, "2. Bundesliga", null, null, null), false, Import(), default));
        Assert.IsType<BadRequestObjectResult>(await ctl.ChessResultsImport(new(Tnr, "2026", 2, "2. Bundesliga", null, null, null), false, Import(), default));
        Assert.IsType<NotFoundObjectResult>(await ctl.ChessResultsImport(new(Tnr, "2026/27", 2, "2. Bundesliga", null, null, null), false,
            Import(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"tnr\":{Tnr},\"matches\":[],\"games\":[],\"roundDates\":{{}},\"roster\":[],\"stats\":[]}}", Encoding.UTF8, "application/json"),
            }), default));
        var down = await ctl.ChessResultsImport(new(Tnr, "2026/27", 2, "2. Bundesliga", null, null, null), false,
            Import(_ => throw new HttpRequestException("weg")), default);
        Assert.Equal(503, Assert.IsType<ObjectResult>(down).StatusCode);
    }

    private sealed class OneClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(f(request));
    }
}
