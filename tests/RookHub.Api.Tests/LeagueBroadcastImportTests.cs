using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// Lichess-Übertragungen in die Spielerkarten (0.608.0). Die Antworten sind nachgebaut — Felder wie in
/// <c>/api/broadcast/search</c>, <c>/api/broadcast/{id}</c> und dem PGN-Export einer Übertragung, nachgesehen am 2026-09-30
/// (Kufsteiner Open 2026: Datum in den Partien 30.05., das Turnier selbst 31.07.).
/// </summary>
public class LeagueBroadcastImportTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private const string Moves = "1. e4 d6 2. d4 e5 3. Nf3 exd4 4. Nxd4 g6 5. Nc3 Bg7 6. Be3 a6 7. Qd2 Nc6 8. O-O-O Nxd4 9. Bxd4 Nf6 10. f3 Be6";

    private static string Game(string white, string black, string result, string date = "2026.05.30", string? whiteFide = "4100484",
        string? blackFide = "1610198", string moves = Moves, string extra = "") =>
        $"[Event \"Kufstein Open\"]\n[Site \"Kufstein\"]\n[Date \"{date}\"]\n[Round \"1.1\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n"
        + $"[Result \"{result}\"]\n" + (whiteFide is null ? "" : $"[WhiteFideId \"{whiteFide}\"]\n")
        + (blackFide is null ? "" : $"[BlackFideId \"{blackFide}\"]\n") + extra
        + "[UTCDate \"2026.05.30\"]\n[GameURL \"https://lichess.org/broadcast/x/round-1/CuCY3Ob7/2ggzEnEx\"]\n\n"
        + $"{moves} {{ [%eval 0.18] [%clk 1:30:54] }} {result}\n\n";

    // ── Lesen ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IdsOf_TourLink_RoundLink_BareId()
    {
        Assert.Equal(("FX5cToFp", (string?)null), LeagueBroadcastImport.IdsOf("https://lichess.org/broadcast/schach-tirol-open-2026/FX5cToFp"));
        Assert.Equal(((string?)null, "CuCY3Ob7"),
            LeagueBroadcastImport.IdsOf("https://lichess.org/broadcast/100-jahre-schachklub-kufstein/round-1/CuCY3Ob7?x=1"));
        Assert.Equal(("favpBItT", (string?)null), LeagueBroadcastImport.IdsOf(" favpBItT "));
        Assert.Null(LeagueBroadcastImport.IdsOf("https://lichess.org/@/MaxMuster"));
        Assert.Null(LeagueBroadcastImport.IdsOf("https://lichess.org/broadcast/nur-slug"));
        Assert.Null(LeagueBroadcastImport.IdsOf(""));
    }

    [Fact]
    public void DateOf_NormalizesDashes_RejectsUnknown()
    {
        Assert.Equal("2024.08.24", LeagueBroadcastImport.DateOf("2024-08-24"));
        Assert.Equal("2024.08.24", LeagueBroadcastImport.DateOf("2024.08.24"));
        Assert.Null(LeagueBroadcastImport.DateOf("2024.??.??"));
        Assert.Null(LeagueBroadcastImport.DateOf(null));
    }

    [Fact]
    public void ParseSearch_And_TourDetail()
    {
        var (tours, next) = LeagueBroadcastImport.ParseSearch("""
            {"currentPage":1,"nextPage":2,"currentPageResults":[
              {"tour":{"id":"FX5cToFp","name":"Schach Tirol Open 2026","info":{"location":"Innsbruck, Austria"},"dates":[1787407200000,1787990400000]}},
              {"tour":{"id":"k8IPiRCv","name":"Chess Festival Innsbruck"}}]}
            """);
        Assert.Equal(2, next);
        Assert.Equal(("FX5cToFp", "Schach Tirol Open 2026", "Innsbruck, Austria"), (tours[0].Id, tours[0].Name, tours[0].Location));
        Assert.Equal(new DateTime(2026, 8, 22), tours[0].Start!.Value.Date);
        Assert.Equal(new DateTime(2026, 8, 29), tours[0].End!.Value.Date);
        Assert.Null(tours[1].Start);
        Assert.Null(LeagueBroadcastImport.ParseSearch("""{"currentPageResults":[],"nextPage":null}""").NextPage);

        var (tour, all) = LeagueBroadcastImport.ParseTourDetail("""
            {"tour":{"id":"favpBItT","name":"Kufstein Open","dates":[1785510000000,1785679200000]},
             "rounds":[{"id":"a","finished":true},{"id":"b","finished":true}]}
            """);
        Assert.Equal(("favpBItT", true), (tour!.Id, all));
        Assert.False(LeagueBroadcastImport.ParseTourDetail("""{"tour":{"id":"x1234567"},"rounds":[{"finished":true},{"ongoing":true}]}""").AllFinished);
        Assert.False(LeagueBroadcastImport.ParseTourDetail("""{"tour":{"id":"x1234567"},"rounds":[]}""").AllFinished);
    }

    [Fact]
    public void CleanPgn_KeepsFinishedStandardGamesWithFideIds_WithoutEvalsAndClocks()
    {
        var pgn = Game("Glek, Igor", "Hoebarth, Guenter", "1-0", date: "2026-05-30")
                  + Game("A, B", "C, D", "*")                                                    // läuft noch
                  + Game("E, F", "G, H", "0-1", whiteFide: null, blackFide: null)                 // ohne FIDE-ID
                  + Game("I, J", "K, L", "1/2-1/2", extra: "[Variant \"Chess960\"]\n")
                  + Game("M, N", "O, P", "0-1", date: "????.??.??");
        var (clean, n) = LeagueBroadcastImport.CleanPgn(pgn);
        Assert.Equal(2, n);
        Assert.Contains("[Date \"2026.05.30\"]", clean);                        // Bindestriche → Punkte; ohne Datum: UTCDate
        Assert.DoesNotContain("%eval", clean);
        Assert.DoesNotContain("%clk", clean);
        Assert.DoesNotContain("UTCDate", clean);
        Assert.Contains("[WhiteFideId \"4100484\"]", clean);
        Assert.Contains("[GameURL \"https://lichess.org/broadcast/x/round-1/CuCY3Ob7/2ggzEnEx\"]", clean);
        Assert.Contains("1. e4 d6 2. d4 e5", clean);
        Assert.Contains("10. f3 Be6 1-0", clean);
    }

    // ── Einspielen ─────────────────────────────────────────────────────────────────────────────

    private sealed class FakeHttp(Func<string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public readonly List<string> Urls = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(answer(request.RequestUri!.ToString()));
        }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };

    private LeagueBroadcastImport Importer(FakeHttp http) =>
        new(_db, new HttpClient(http), NullLogger<LeagueBroadcastImport>.Instance) { Pause = TimeSpan.Zero };

    private async Task SeedAsync()
    {
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 1, Team = "Kufstein 1", Name = "Hoebarth, Guenter", NameKey = "hoebarth, guenter", FideId = "1610198" });
        await _db.SaveChangesAsync();
    }

    private static string Detail(string id, bool finished, long startMs = 1785510000000) =>
        "{\"tour\":{\"id\":\"" + id + "\",\"name\":\"Kufstein Open\",\"info\":{\"location\":\"Kufstein, Austria\"},\"dates\":["
        + startMs + "," + (startMs + 172800000) + "]},\"rounds\":[{\"id\":\"r1\",\"finished\":" + (finished ? "true" : "false") + "}]}";

    [Fact]
    public async Task Import_AddsNewGames_SkipsTheSameGameWithAnotherDate_AndMarksFinished()
    {
        await SeedAsync();
        // Dieselbe Partie steht schon aus chess-results da — mit dem RICHTIGEN Datum (31.07.).
        await new LeagueProfileStore(_db).ImportGamesAsync(Game("Glek, Igor", "Hoebarth, Guenter", "1-0", date: "2026.07.31"), "chess-results", default);
        _db.ChangeTracker.Clear();
        var newGame = Game("Hoebarth, Guenter", "Muster, Max", "0-1", whiteFide: "1610198", blackFide: "999",
            moves: "1. d4 Nf6 2. c4 e6 3. Nc3 Bb4 4. Qc2 O-O 5. a3 Bxc3+ 6. Qxc3 b6 7. Bg5 Bb7 8. f3 h6");
        var http = new FakeHttp(u => u.EndsWith(".pgn")
            ? Ok(Game("Glek, Igor", "Hoebarth, Guenter", "1-0") + newGame)
            : Ok(Detail("favpBItT", finished: true)));
        var b = new LeagueBroadcast { TourId = "favpBItT", Name = "x", FoundAt = DateTime.UtcNow };
        _db.LeagueBroadcasts.Add(b);
        await _db.SaveChangesAsync();

        await Importer(http).ImportAsync(b, default);

        var saved = await _db.LeagueBroadcasts.AsNoTracking().SingleAsync();          // aus der DB — nicht das Objekt im Speicher
        Assert.Equal((2, true, (string?)null, "Kufstein, Austria"), (saved.Games, saved.Finished, saved.Error, saved.Location));
        var profile = await _db.LeaguePlayerProfiles.AsNoTracking().SingleAsync(p => p.FideId == "1610198");
        Assert.Equal(2, profile.GameCount);                                    // nicht drei: die Glek-Partie nur einmal
        Assert.Contains("[LeagueSource \"Lichess-Übertragung\"]", profile.Pgn);
        Assert.Contains("Muster, Max", profile.Pgn);
    }

    [Fact]
    public async Task RunOnce_DiscoversRecentTours_ImportsStarted_WaitsForFuture()
    {
        await SeedAsync();
        var now = DateTimeOffset.UtcNow;
        long Ms(DateTimeOffset d) => d.ToUnixTimeMilliseconds();
        static string T(string id, string name, params long[] dates) =>
            "{\"tour\":{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"dates\":[" + string.Join(",", dates) + "]}}";
        var search = "{\"currentPage\":1,\"nextPage\":null,\"currentPageResults\":["
            + T("recent01", "Tirol Open", Ms(now.AddDays(-20)), Ms(now.AddDays(-12))) + ","
            + T("future01", "Kufstein Open", Ms(now.AddDays(10)), Ms(now.AddDays(12))) + ","
            + T("old00001", "Festival 2015", Ms(now.AddYears(-10))) + "]}";
        var http = new FakeHttp(u => u.Contains("/search") ? Ok(search)
            : u.EndsWith(".pgn") ? Ok(Game("Glek, Igor", "Hoebarth, Guenter", "1-0"))
            : Ok(Detail("recent01", finished: true, Ms(now.AddDays(-20)))));
        var imp = Importer(http);
        Assert.False(await imp.RunOnceAsync(TimeSpan.FromMinutes(5), discover: true, default));

        var rows = await _db.LeagueBroadcasts.AsNoTracking().OrderBy(b => b.TourId).ToListAsync();
        Assert.Equal(new[] { "future01", "recent01" }, rows.Select(b => b.TourId));        // zu alt: nicht vorgemerkt
        Assert.Equal(DefaultSearches(), http.Urls.Count(u => u.Contains("/search")));
        Assert.True(rows.Single(b => b.TourId == "recent01").Finished);
        Assert.Null(rows.Single(b => b.TourId == "future01").ImportedAt);               // noch nicht begonnen

        http.Urls.Clear();
        Assert.False(await imp.RunOnceAsync(TimeSpan.FromMinutes(5), discover: false, default));
        Assert.Empty(http.Urls);                                                        // fertig ist fertig, künftig wartet
        Assert.Single(await imp.ListAsync(default), x => x.ToString()!.Contains("recent01"));
    }

    private static int DefaultSearches() => LeagueBroadcastImport.DefaultQueries.Length;

    [Fact]
    public async Task Add_ByRoundLink_ResolvesTheTour_AndImports_InvalidLinkIsRefused()
    {
        await SeedAsync();
        var running = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();                // läuft gerade
        var http = new FakeHttp(u => u.Contains("/-/-/CuCY3Ob7") || u.EndsWith("/favpBItT") ? Ok(Detail("favpBItT", finished: false, running))
            : u.EndsWith(".pgn") ? Ok(Game("Glek, Igor", "Hoebarth, Guenter", "1-0")) : new HttpResponseMessage(HttpStatusCode.NotFound));
        var imp = Importer(http);
        Assert.Equal("invalidUrl", (await imp.AddAsync("https://lichess.org/@/x", default)).Reason);
        Assert.Equal("notFound", (await imp.AddAsync("https://lichess.org/broadcast/x/ZZZZZZZZ", default)).Reason);

        var (b, reason) = await imp.AddAsync("https://lichess.org/broadcast/kufstein/round-1/CuCY3Ob7", default);
        Assert.Null(reason);
        var saved = await _db.LeagueBroadcasts.AsNoTracking().SingleAsync();
        Assert.Equal(("favpBItT", true, 1, false), (saved.TourId, saved.Manual, saved.Games, saved.Finished));   // läuft noch
    }

    [Fact]
    public void SameGameKey_IgnoresDate_NeedsTenPlies()
    {
        var a = LeagueProfileBuilder.Parse(Game("Glek, Igor", "Hoebarth, Guenter", "1-0", date: "2026.05.30"), "x")[0];
        var b = LeagueProfileBuilder.Parse(Game("Glek, I.", "Hoebarth, G.", "1-0", date: "2026.07.31"), "y")[0];
        Assert.Equal(LeagueProfileStore.SameGameKey(a), LeagueProfileStore.SameGameKey(b));
        var shortGame = LeagueProfileBuilder.Parse(Game("A, B", "C, D", "1-0", moves: "1. e4 e5 2. Nf3 Nc6"), "x")[0];
        Assert.Null(LeagueProfileStore.SameGameKey(shortGame));
    }
}
