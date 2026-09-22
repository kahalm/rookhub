using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Lochfinder-Dienst: Zugriff, Speicher, Token-Wahl, Drossel und Zeitbudget. Die Rechnung
/// selbst prüft <see cref="RepertoireReachTests"/>.</summary>
public class RepertoireExplorerServiceTests : IDisposable
{
    private const string StartKey = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq";

    /// <summary>Explorer-Antwort der Grundstellung: 1.e4 60 %, 1.d4 30 %, 1.c4 10 %.</summary>
    private const string StartJson = """
        {"white":400,"draws":200,"black":400,"moves":[
          {"uci":"e2e4","san":"e4","averageRating":1900,"white":240,"draws":120,"black":240,"game":null,"opening":{"eco":"B00","name":"King's Pawn"}},
          {"uci":"d2d4","san":"d4","averageRating":1900,"white":120,"draws":60,"black":120,"game":null,"opening":{"eco":"A40","name":"Queen's Pawn Game"}},
          {"uci":"c2c4","san":"c4","averageRating":1900,"white":40,"draws":20,"black":40,"game":null,"opening":null}]}
        """;

    private readonly AppDbContext _db;
    private readonly StubHandler _handler = new();
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero));
    private readonly LichessExplorerGate _gate;
    private readonly EncryptionService _encryption;
    private readonly Dictionary<string, string?> _settings = new() { ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!" };

    public RepertoireExplorerServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _gate = new LichessExplorerGate(_time, refillInterval: TimeSpan.Zero);   // Kontingent prüft ein eigener Test
        _encryption = new EncryptionService(new ConfigurationBuilder().AddInMemoryCollection(_settings).Build());
    }

    public void Dispose() => _db.Dispose();

    private RepertoireExplorerService Service()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(_settings).Build();
        var client = new LichessExplorerClient(
            new HttpClient(_handler) { BaseAddress = new Uri(LichessExplorerClient.BaseUrl) },
            _gate, NullLogger<LichessExplorerClient>.Instance);
        return new RepertoireExplorerService(_db, TestServices.Repertoire(_db), client, _gate, _encryption,
            config, NullLogger<RepertoireExplorerService>.Instance, _time);
    }

    private async Task<(int UserId, int RepertoireId)> SeedAsync(string pgn, string? userToken = "lip_user")
    {
        var user = new AppUser { Username = "u", Email = "u@x.y", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        if (userToken is not null)
        {
            _db.LichessEngineCredentials.Add(new LichessEngineCredential { UserId = user.Id, EncryptedToken = _encryption.Encrypt(userToken) });
            await _db.SaveChangesAsync();
        }
        var repService = TestServices.Repertoire(_db);
        var rep = await repService.CreateAsync(user.Id, new CreateRepertoireDto { Name = "Schwarz", Kind = RepertoireKind.Opening });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(pgn));
        await repService.UploadFileAsync(rep.Id, user.Id, "rep.pgn", stream);
        return (user.Id, rep.Id);
    }

    private const string BlackVsE4 = "[Event \"x\"]\n[White \"Linie\"]\n[Black \"Sizilianisch\"]\n\n1. e4 c5 *";

    private static ExplorerAnalysisRequestDto Request(string? color = null) => new()
    {
        Color = color,
        ChapterColors = new() { ["Sizilianisch"] = "b" },
        Database = "lichess",
        Ratings = new() { 2000, 1800 },
        Speeds = new() { "rapid", "blitz" },
        ThresholdPercent = 1,
        IncludeLineFrequencies = true,
    };

    [Fact]
    public async Task FetchesUncachedPosition_StoresIt_AndReportsHoles()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4);
        _handler.Respond(StartKey, StartJson);

        var result = await Service().AnalyzeAsync(userId, repId, Request(), CancellationToken.None);

        Assert.True(result.Complete);
        Assert.Equal(1, result.PositionsAnalyzed);
        Assert.Equal(new[] { "d4", "c4" }, result.Holes.Select(h => h.San));
        var d4 = result.Holes[0];
        Assert.Equal("b", d4.Color);
        Assert.Equal(0.3, d4.Share, 6);
        Assert.Equal(1000, d4.PositionGames);
        Assert.Equal("Queen's Pawn Game", d4.Opening);
        Assert.Empty(d4.Path);

        // Anfrage: Elo und Tempo in Explorer-Reihenfolge, Nutzer-Token (kein Server-Token gesetzt).
        var url = Assert.Single(_handler.Urls);
        Assert.Contains("ratings=1800,2000", url);
        Assert.Contains("speeds=blitz,rapid", url);
        Assert.Equal("Bearer lip_user", _handler.LastAuth);

        var row = Assert.Single(_db.LichessExplorerCacheEntries);
        Assert.Equal("lichess|1800,2000|blitz,rapid|" + StartKey, row.CacheKey);
    }

    [Fact]
    public async Task SecondRun_UsesCache_WithoutAnyRequest()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4);
        _handler.Respond(StartKey, StartJson);
        await Service().AnalyzeAsync(userId, repId, Request(), CancellationToken.None);

        var again = await Service().AnalyzeAsync(userId, repId, Request(), CancellationToken.None);

        Assert.Single(_handler.Urls);
        Assert.Equal(2, again.Holes.Count);
    }

    [Fact]
    public async Task StaleCacheEntry_IsFetchedAgain_AndUpdatedInPlace()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4);
        _db.LichessExplorerCacheEntries.Add(new LichessExplorerCacheEntry
        {
            CacheKey = "lichess|1800,2000|blitz,rapid|" + StartKey,
            Json = new ExplorerPositionStats(100, new() { new("e2e4", "e4", 100, null, null) }).ToJson(),
            FetchedAt = _time.GetUtcNow().UtcDateTime - RepertoireExplorerService.CacheTtl - TimeSpan.FromDays(1),
        });
        await _db.SaveChangesAsync();
        _handler.Respond(StartKey, StartJson);

        var result = await Service().AnalyzeAsync(userId, repId, Request(), CancellationToken.None);

        Assert.Single(_handler.Urls);
        Assert.Equal(2, result.Holes.Count);
        var row = Assert.Single(_db.LichessExplorerCacheEntries);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, row.FetchedAt);
    }

    [Fact]
    public async Task ServerToken_WinsOverUserToken()
    {
        _settings["LichessExplorer:Token"] = "lip_server";
        var (userId, repId) = await SeedAsync(BlackVsE4);
        _handler.Respond(StartKey, StartJson);

        await Service().AnalyzeAsync(userId, repId, Request(), CancellationToken.None);

        Assert.Equal("Bearer lip_server", _handler.LastAuth);
    }

    [Fact]
    public async Task NoToken_ReportsTokenMissing_WithoutRequest()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4, userToken: null);

        var result = await Service().AnalyzeAsync(userId, repId, Request(), CancellationToken.None);

        Assert.True(result.TokenMissing);
        Assert.False(result.Complete);
        Assert.Equal(1, result.PositionsPending);
        Assert.Empty(_handler.Urls);
    }

    [Fact]
    public async Task RateLimit_StopsTheRun_AndBlocksTheGateForAMinute()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4);
        _handler.Status = HttpStatusCode.TooManyRequests;

        var result = await Service().AnalyzeAsync(userId, repId, Request(), CancellationToken.None);

        Assert.True(result.RateLimited);
        Assert.Equal(60, result.RetryAfterSeconds);
        Assert.False(result.Complete);

        // Während der Pause fragt auch ein neuer Lauf nicht an.
        _handler.Status = HttpStatusCode.OK;
        _handler.Respond(StartKey, StartJson);
        var during = await Service().AnalyzeAsync(userId, repId, Request(), CancellationToken.None);
        Assert.True(during.RateLimited);
        Assert.Single(_handler.Urls);

        _time.Advance(TimeSpan.FromSeconds(61));
        var after = await Service().AnalyzeAsync(userId, repId, Request(), CancellationToken.None);
        Assert.True(after.Complete);
    }

    [Fact]
    public async Task RejectedToken_IsReported()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4);
        _handler.Status = HttpStatusCode.Unauthorized;

        var result = await Service().AnalyzeAsync(userId, repId, Request(), CancellationToken.None);

        Assert.True(result.TokenInvalid);
        Assert.False(result.Complete);
    }

    [Fact]
    public async Task ExhaustedBudget_LeavesPositionsPending()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4);
        _handler.Respond(StartKey, StartJson);
        var service = Service();
        service.Budget = TimeSpan.Zero;

        var result = await service.AnalyzeAsync(userId, repId, Request(), CancellationToken.None);

        Assert.False(result.Complete);
        Assert.Equal(1, result.PositionsPending);
        Assert.Empty(_handler.Urls);
    }

    [Fact]
    public async Task ChapterColors_DecideWhichSideIsAnalyzed()
    {
        // Dasselbe Kapitel als WEISS-Kapitel: 1.e4 ist dann der eigene Zug (die Grundstellung wird
        // nicht abgefragt), gefragt wird die Stellung danach — dort antwortet das Repertoire mit c5.
        var (userId, repId) = await SeedAsync(BlackVsE4);
        var req = Request();
        req.ChapterColors = new() { ["Sizilianisch"] = "w" };

        var asWhite = await Service().AnalyzeAsync(userId, repId, req, CancellationToken.None);
        var url = Assert.Single(_handler.Urls);
        Assert.Contains("fen=rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq", url);
        Assert.True(asWhite.Complete);

        // Farbfilter „nur Schwarz" findet bei einem reinen Weiß-Repertoire nichts.
        req.Color = "b";
        var onlyBlack = await Service().AnalyzeAsync(userId, repId, req, CancellationToken.None);
        Assert.Equal(0, onlyBlack.PositionsAnalyzed + onlyBlack.PositionsPending);
    }

    [Fact]
    public async Task LineFrequencies_AreKeyedByTheLinesEndPosition()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4);
        _handler.Respond(StartKey, StartJson);

        var result = await Service().AnalyzeAsync(userId, repId, Request(), CancellationToken.None);

        var (key, p) = Assert.Single(result.LineFrequencies!);
        Assert.Equal("rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq", key);
        Assert.Equal(0.6, p, 6);
    }

    [Fact]
    public async Task ForeignRepertoire_IsNotFound()
    {
        var (_, repId) = await SeedAsync(BlackVsE4);
        var other = new AppUser { Username = "o", Email = "o@x.y", PasswordHash = "h" };
        _db.AppUsers.Add(other);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Service().AnalyzeAsync(other.Id, repId, Request(), CancellationToken.None));
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(51)]
    public async Task ThresholdOutOfRange_IsRejected(double percent)
    {
        var (userId, repId) = await SeedAsync(BlackVsE4);
        var req = Request();
        req.ThresholdPercent = percent;

        await Assert.ThrowsAsync<ArgumentException>(() => Service().AnalyzeAsync(userId, repId, req, CancellationToken.None));
    }

    [Fact]
    public async Task Gate_SpendsItsBurst_ThenWaitsForTheRefill()
    {
        // Gegen den echten Explorer gemessen: 21 Anfragen dicht hintereinander → 429.
        var gate = new LichessExplorerGate(burst: 1, refillInterval: TimeSpan.FromMilliseconds(200));
        var client = new LichessExplorerClient(
            new HttpClient(_handler) { BaseAddress = new Uri(LichessExplorerClient.BaseUrl) },
            gate, NullLogger<LichessExplorerClient>.Instance);
        var query = ExplorerQuery.Create("masters", null, null);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await client.FetchAsync("8/8/8/8/8/8/8/K6k w - - 0 1", query, "t", CancellationToken.None);
        await client.FetchAsync("8/8/8/8/8/8/8/K6k w - - 0 1", query, "t", CancellationToken.None);
        await client.FetchAsync("8/8/8/8/8/8/8/K6k w - - 0 1", query, "t", CancellationToken.None);

        Assert.True(clock.ElapsedMilliseconds >= 380, $"nur {clock.ElapsedMilliseconds} ms für drei Anfragen");
        Assert.Equal(3, _handler.Urls.Count);
    }

    [Fact]
    public async Task Gate_AfterA429_TheBucketIsEmpty_NotFull()
    {
        // Nach der Minute Pause kämen sonst sofort wieder 15 Anfragen am Stück — und der nächste 429.
        var gate = new LichessExplorerGate(_time, burst: 15, refillInterval: TimeSpan.FromSeconds(4));
        gate.Block();
        Assert.NotNull(gate.BlockedFor);

        _time.Advance(LichessExplorerGate.RateLimitPause + TimeSpan.FromSeconds(4));
        Assert.Null(gate.BlockedFor);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await gate.PaceAsync(CancellationToken.None);   // genau eine nachgelaufene Anfrage
        Assert.True(clock.ElapsedMilliseconds < 1000);
    }

    [Fact]
    public void Query_ValidatesAndOrders()
    {
        Assert.Equal("masters|", ExplorerQuery.Create("masters", new[] { 1234 }, new[] { "x" }).CachePrefix);
        Assert.Equal("lichess|1600,2200|bullet,classical|",
            ExplorerQuery.Create(null, new[] { 2200, 1600, 1600 }, new[] { "classical", "bullet" }).CachePrefix);
        Assert.Throws<ArgumentException>(() => ExplorerQuery.Create("lichess", new[] { 1700 }, new[] { "blitz" }));
        Assert.Throws<ArgumentException>(() => ExplorerQuery.Create("lichess", new[] { 1600 }, new[] { "hyper" }));
        Assert.Throws<ArgumentException>(() => ExplorerQuery.Create("lichess", Array.Empty<int>(), new[] { "blitz" }));
        Assert.Throws<ArgumentException>(() => ExplorerQuery.Create("chessbase", new[] { 1600 }, new[] { "blitz" }));
    }

    /// <summary>Uhr zum Vorstellen (Drossel-Pause, Speicherdauer).</summary>
    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>Antwortet je Stellung (über den fen-Parameter) mit festem JSON; merkt sich Anfragen.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _byKey = new();
        public HttpStatusCode Status = HttpStatusCode.OK;
        public readonly List<string> Urls = new();
        public string? LastAuth;

        public void Respond(string positionKey, string json) => _byKey[positionKey] = json;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            Urls.Add(url);
            LastAuth = request.Headers.Authorization?.ToString();
            if (Status != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(Status));

            var fen = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["fen"] ?? "";
            var json = _byKey.TryGetValue(RepertoireReach.Key(fen), out var j) ? j : """{"white":0,"draws":0,"black":0,"moves":[]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
