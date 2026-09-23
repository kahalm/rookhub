using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
    /// <summary>Der lokale Explorer — eigene Leitung, eigene Antworten.</summary>
    private readonly StubHandler _localHandler = new();
    private bool _localConfigured = true;
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

    public void Dispose()
    {
        _db.Dispose();
        _memory.Dispose();
    }

    private readonly MemoryCache _memory = new(new MemoryCacheOptions());

    private RepertoireExplorerService Service()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(_settings).Build();
        var client = new LichessExplorerClient(
            new HttpClient(_handler) { BaseAddress = new Uri(LichessExplorerClient.BaseUrl) },
            _gate, NullLogger<LichessExplorerClient>.Instance);
        var localHttp = new HttpClient(_localHandler);
        if (_localConfigured) localHttp.BaseAddress = new Uri("http://rookhub-explorer:9002/");
        var local = new LocalExplorerClient(localHttp, NullLogger<LocalExplorerClient>.Instance);
        return new RepertoireExplorerService(_db, TestServices.Repertoire(_db), client, local, _memory, _gate, _encryption,
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

    // ---- Lokale Quelle ----

    private const string BlackLines = "[Event \"x\"]\n[White \"Linie\"]\n[Black \"Sizilianisch\"]\n\n1. e4 c5 2. Nf3 d6 *\n\n"
        + "[Event \"y\"]\n[White \"Linie 2\"]\n[Black \"Sizilianisch\"]\n\n1. d4 d5 2. c4 e6 *";

    private static ExplorerAnalysisRequestDto LocalRequest()
    {
        var r = Request();
        r.Source = "local";
        return r;
    }

    [Fact]
    public async Task LocalSource_NeedsNoToken_TouchesNeitherLichessNorTheDatabase()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4, userToken: null);
        _localHandler.Respond(StartKey, StartJson);

        var result = await Service().AnalyzeAsync(userId, repId, LocalRequest(), CancellationToken.None);

        Assert.True(result.Complete);
        Assert.False(result.TokenMissing);
        Assert.Equal(new[] { "d4", "c4" }, result.Holes.Select(h => h.San));
        var url = Assert.Single(_localHandler.Urls);
        Assert.StartsWith("http://rookhub-explorer:9002/lichess?", url);
        Assert.Null(_localHandler.LastAuth);
        Assert.Empty(_handler.Urls);
        Assert.Empty(_db.LichessExplorerCacheEntries);
    }

    [Fact]
    public async Task LocalSource_SecondRun_ComesFromMemory()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4);
        _localHandler.Respond(StartKey, StartJson);
        await Service().AnalyzeAsync(userId, repId, LocalRequest(), CancellationToken.None);

        var again = await Service().AnalyzeAsync(userId, repId, LocalRequest(), CancellationToken.None);

        Assert.Single(_localHandler.Urls);
        Assert.Equal(2, again.Holes.Count);
    }

    [Fact]
    public async Task LocalSource_FetchesAWholeLayerAtOnce()
    {
        // Schwarz: die Grundstellung (Schicht 0), dann 1.e4 c5 und 1.d4 d5 in derselben Schicht 2.
        var (userId, repId) = await SeedAsync(BlackLines);
        _localHandler.Respond(StartKey, """{"white":50,"draws":0,"black":50,"moves":[{"uci":"e2e4","san":"e4","white":25,"draws":0,"black":25},{"uci":"d2d4","san":"d4","white":25,"draws":0,"black":25}]}""");

        var result = await Service().AnalyzeAsync(userId, repId, LocalRequest(), CancellationToken.None);

        Assert.True(result.Complete);
        Assert.Equal(3, result.PositionsAnalyzed);
        Assert.Equal(3, _localHandler.Urls.Count);
    }

    [Fact]
    public async Task LocalSource_NotConfigured_IsRejected()
    {
        _localConfigured = false;
        var (userId, repId) = await SeedAsync(BlackVsE4);

        await Assert.ThrowsAsync<ArgumentException>(() => Service().AnalyzeAsync(userId, repId, LocalRequest(), CancellationToken.None));
        Assert.False(Service().Sources().Local);
    }

    [Fact]
    public async Task LocalSource_Unreachable_ReportsFetchFailed()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4);
        _localHandler.Status = HttpStatusCode.BadGateway;

        var result = await Service().AnalyzeAsync(userId, repId, LocalRequest(), CancellationToken.None);

        Assert.True(result.FetchFailed);
        Assert.False(result.Complete);
    }

    private const string AfterE4C5 = "rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq";
    private const string AfterD4D5 = "rnbqkbnr/ppp1pppp/8/3p4/3P4/8/PPP1PPPP/RNBQKBNR w KQkq";
    private const string BothFirstMoves = """{"white":50,"draws":0,"black":50,"moves":[{"uci":"e2e4","san":"e4","white":25,"draws":0,"black":25},{"uci":"d2d4","san":"d4","white":25,"draws":0,"black":25}]}""";

    [Fact]
    public async Task LocalSource_OneOutlier_StaysPending_AndTheRunGoesOn()
    {
        // Während eines Imports antwortet der lokale Explorer vereinzelt nicht — das ist kein Ausfall.
        var (userId, repId) = await SeedAsync(BlackLines);
        _localHandler.Respond(StartKey, BothFirstMoves);
        _localHandler.FailKeys.Add(AfterD4D5);

        var result = await Service().AnalyzeAsync(userId, repId, LocalRequest(), CancellationToken.None);

        Assert.False(result.FetchFailed);
        Assert.False(result.Complete);
        Assert.Equal(1, result.PositionsPending);
        Assert.Equal(2, result.PositionsAnalyzed);

        // Nächste Runde: wieder da → fertig, die schon geholten kommen aus dem Arbeitsspeicher.
        _localHandler.FailKeys.Clear();
        var again = await Service().AnalyzeAsync(userId, repId, LocalRequest(), CancellationToken.None);
        Assert.True(again.Complete);
        Assert.Equal(4, _localHandler.Urls.Count);
    }

    [Fact]
    public async Task LocalSource_SlowPosition_IsCutAtTheLayerLimit_AndLeftPending()
    {
        var (userId, repId) = await SeedAsync(BlackLines);
        _localHandler.Respond(StartKey, BothFirstMoves);
        _localHandler.SlowKeys[AfterE4C5] = TimeSpan.FromSeconds(20);
        var service = Service();
        service.Budget = TimeSpan.FromMilliseconds(100);
        service.LocalLayerFloor = TimeSpan.FromMilliseconds(300);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.AnalyzeAsync(userId, repId, LocalRequest(), CancellationToken.None);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Runde dauerte {clock.Elapsed}");
        Assert.False(result.FetchFailed);
        Assert.Equal(1, result.PositionsPending);
    }

    [Fact]
    public void Sources_NameTheLocalLimits()
    {
        var sources = Service().Sources();
        Assert.True(sources.Online);
        Assert.True(sources.Local);
        Assert.Equal(new[] { 1600, 1800, 2000, 2200, 2500 }, sources.LocalRatings);
        Assert.DoesNotContain("bullet", sources.LocalSpeeds);
    }

    [Fact]
    public async Task UnknownSource_IsRejected()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4);
        var req = Request();
        req.Source = "chessbase";
        await Assert.ThrowsAsync<ArgumentException>(() => Service().AnalyzeAsync(userId, repId, req, CancellationToken.None));
    }

    // ---- Einzelne Stellung (Explorer auf dem Analysebrett) ----

    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private static readonly ExplorerQuery Blitz = ExplorerQuery.Create("lichess", new[] { 1800, 2000 }, new[] { "blitz", "rapid" });

    private async Task<int> UserAsync(string? token = "lip_user")
    {
        var user = new AppUser { Username = "p", Email = "p@x.y", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        if (token is not null)
        {
            _db.LichessEngineCredentials.Add(new LichessEngineCredential { UserId = user.Id, EncryptedToken = _encryption.Encrypt(token) });
            await _db.SaveChangesAsync();
        }
        return user.Id;
    }

    [Fact]
    public async Task Position_Online_ReturnsResultsPerMove_AndCachesThem()
    {
        var userId = await UserAsync();
        _handler.Respond(StartKey, StartJson);

        var r = await Service().PositionAsync(userId, StartFen, null, Blitz, CancellationToken.None);

        Assert.Equal("ok", r.Status);
        Assert.Equal(1000, r.Total);
        Assert.Equal((400, 200, 400), (r.White, r.Draws, r.Black));
        var e4 = r.Moves[0];
        Assert.Equal(("e4", 600L, 240L, 120L, 240L, 1900), (e4.San, e4.Games, e4.White, e4.Draws, e4.Black, e4.AverageRating!.Value));
        Assert.Equal("King's Pawn", e4.Opening);

        // Zweiter Aufruf: aus dem Speicher, kein neuer Abruf — und der Lochfinder teilt ihn.
        await Service().PositionAsync(userId, StartFen, null, Blitz, CancellationToken.None);
        Assert.Single(_handler.Urls);
        Assert.Single(_db.LichessExplorerCacheEntries);
    }

    [Fact]
    public async Task Position_CacheEntryWithoutResults_IsFetchedAgain()
    {
        // Vor 0.504.0 geschrieben: nur Summen, keine Aufteilung Weiß/Remis/Schwarz.
        var userId = await UserAsync();
        _db.LichessExplorerCacheEntries.Add(new LichessExplorerCacheEntry
        {
            CacheKey = Blitz.CachePrefix + StartKey,
            Json = """{"t":100,"m":[{"u":"e2e4","s":"e4","g":100}]}""",
            FetchedAt = _time.GetUtcNow().UtcDateTime,
        });
        await _db.SaveChangesAsync();
        _handler.Respond(StartKey, StartJson);

        var r = await Service().PositionAsync(userId, StartFen, null, Blitz, CancellationToken.None);

        Assert.Single(_handler.Urls);
        Assert.Equal(1000, r.Total);
        Assert.Contains("\"w\":400", Assert.Single(_db.LichessExplorerCacheEntries).Json);
    }

    [Fact]
    public async Task Position_Local_NeedsNoToken()
    {
        var userId = await UserAsync(token: null);
        _localHandler.Respond(StartKey, StartJson);

        var r = await Service().PositionAsync(userId, StartFen, "local", Blitz, CancellationToken.None);

        Assert.Equal("ok", r.Status);
        Assert.Equal("local", r.Source);
        Assert.Equal(3, r.Moves.Count);
        Assert.Empty(_handler.Urls);
    }

    [Fact]
    public async Task Position_Online_WithoutToken_SaysSo()
    {
        var userId = await UserAsync(token: null);
        var r = await Service().PositionAsync(userId, StartFen, "online", Blitz, CancellationToken.None);
        Assert.Equal("tokenMissing", r.Status);
        Assert.Empty(_handler.Urls);
    }

    [Fact]
    public async Task Position_RateLimited_NamesTheWait()
    {
        var userId = await UserAsync();
        _handler.Status = HttpStatusCode.TooManyRequests;

        var r = await Service().PositionAsync(userId, StartFen, null, Blitz, CancellationToken.None);

        Assert.Equal("rateLimited", r.Status);
        Assert.Equal(60, r.RetryAfterSeconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hallo")]
    [InlineData("rnbqkbnr/pppppppp/8/8 w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR x KQkq - 0 1")]
    public async Task Position_RejectsWhatIsNoFen(string fen)
    {
        var userId = await UserAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Service().PositionAsync(userId, fen, null, Blitz, CancellationToken.None));
    }

    [Fact]
    public void Parse_KeepsResultsRatingAndOpeningOfThePosition()
    {
        var stats = LichessExplorerClient.Parse(System.Text.Json.JsonDocument.Parse(StartJson))!;
        Assert.Equal((400L, 200L, 400L), (stats.White!.Value, stats.Draws!.Value, stats.Black!.Value));
        Assert.True(stats.HasResults);
        var round = ExplorerPositionStats.FromJson(stats.ToJson())!;
        Assert.Equal(1900, round.Moves[0].AverageRating);
        Assert.Equal(120, round.Moves[0].Draws);
        Assert.False(ExplorerPositionStats.FromJson("""{"t":1,"m":[]}""")!.HasResults);
    }

    // ---- Häufigkeit jeder Stellung (Repertoire-Baum) ----

    [Fact]
    public async Task PositionFrequencies_OnRequest_CoverTheWholeRepertoire()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4);
        _handler.Respond(StartKey, StartJson);
        var req = Request();
        req.IncludePositionFrequencies = true;

        var r = await Service().AnalyzeAsync(userId, repId, req, CancellationToken.None);

        Assert.Equal(1.0, r.PositionFrequencies![StartKey], 6);
        Assert.Equal(0.6, r.PositionFrequencies["rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq"], 6);
        Assert.Null((await Service().AnalyzeAsync(userId, repId, Request(), CancellationToken.None)).PositionFrequencies);
    }

    [Fact]
    public async Task CachedOnly_AsksNobody_AndSaysWhatIsMissing()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4);
        var online = Request();
        online.CachedOnly = true;
        var local = LocalRequest();
        local.CachedOnly = true;

        var r1 = await Service().AnalyzeAsync(userId, repId, online, CancellationToken.None);
        var r2 = await Service().AnalyzeAsync(userId, repId, local, CancellationToken.None);

        Assert.Empty(_handler.Urls);
        Assert.Empty(_localHandler.Urls);
        Assert.Equal(1, r1.PositionsPending);
        Assert.Equal(1, r2.PositionsPending);
        Assert.False(r2.FetchFailed);   // nicht gefragt ist nicht gescheitert
    }

    [Fact]
    public async Task CachedOnly_AfterAHoleSearch_HasEverything()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4);
        _localHandler.Respond(StartKey, StartJson);
        await Service().AnalyzeAsync(userId, repId, LocalRequest(), CancellationToken.None);
        var req = LocalRequest();
        req.CachedOnly = true;
        req.IncludeHoles = false;
        req.IncludePositionFrequencies = true;

        var r = await Service().AnalyzeAsync(userId, repId, req, CancellationToken.None);

        Assert.True(r.Complete);
        Assert.Single(_localHandler.Urls);
        Assert.NotEmpty(r.PositionFrequencies!);
    }

    [Fact]
    public async Task Targets_FetchOnlyWhatTheirFrequencyNeeds()
    {
        var (userId, repId) = await SeedAsync(BlackLines);   // 1.e4 c5 2.Nf3 d6 und 1.d4 d5 2.c4 e6
        _localHandler.Respond(StartKey, BothFirstMoves);
        _localHandler.Respond(AfterE4C5, """{"white":10,"draws":0,"black":10,"moves":[{"uci":"g1f3","san":"Nf3","white":10,"draws":0,"black":10}]}""");
        var req = LocalRequest();
        req.IncludeHoles = false;
        req.IncludePositionFrequencies = true;
        req.Targets = new() { "rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2" };   // nach 2.Nf3

        var r = await Service().AnalyzeAsync(userId, repId, req, CancellationToken.None);

        Assert.True(r.Complete);
        Assert.Equal(0.5, r.PositionFrequencies!["rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq"], 6);
        Assert.Equal(2, _localHandler.Urls.Count);   // Grundstellung + nach 1.e4 c5, NICHT nach 1.d4 d5
        Assert.DoesNotContain(_localHandler.Urls, u => u.Contains("3p4/3P4"));
    }

    [Fact]
    public async Task Targets_TooMany_AreRejected()
    {
        var (userId, repId) = await SeedAsync(BlackVsE4);
        var req = Request();
        req.Targets = Enumerable.Repeat(StartFen, 201).ToList();
        await Assert.ThrowsAsync<ArgumentException>(() => Service().AnalyzeAsync(userId, repId, req, CancellationToken.None));
    }

    // ---- Partien einer Stellung ----

    private const string GamesJson = """
        {"white":10,"draws":0,"black":0,"moves":[],
         "topGames":[
           {"uci":"b8c6","id":"top1","winner":"white","white":{"name":"Caruana, Fabiano","rating":2818},"black":{"name":"Carlsen, Magnus","rating":2882},"year":2019,"month":"2019-08"},
           {"uci":"b8c6","id":"top2","winner":null,"white":{"name":"Ding, Liren","rating":2805},"black":{"name":"Nepo","rating":2790},"year":2021}],
         "recentGames":[
           {"uci":"d7d6","id":"top1","winner":"white","white":{"name":"Caruana, Fabiano","rating":2818},"black":{"name":"Carlsen, Magnus","rating":2882},"year":2019,"month":"2019-08"},
           {"uci":"d7d6","id":"r1","winner":"black","speed":"blitz","white":{"name":"a","rating":1900},"black":{"name":"b","rating":1950},"year":2026,"month":"2026-08"},
           {"uci":"d7d6","id":"r2","winner":"black","white":{"name":"c"},"black":{"name":"d"},"year":2026,"month":"2026-08"},
           {"uci":"d7d6","id":"r3","winner":"black","white":{"name":"e"},"black":{"name":"f"},"year":2026,"month":"2026-08"},
           {"uci":"d7d6","id":"r4","winner":"black","white":{"name":"g"},"black":{"name":"h"},"year":2026,"month":"2026-08"}]}
        """;

    [Fact]
    public async Task Games_Online_TopFirst_NoDuplicates_AHandfulWithLinks()
    {
        var userId = await UserAsync();
        _handler.Respond(StartKey, GamesJson);

        var r = await Service().GamesAsync(userId, StartFen, null, Blitz, CancellationToken.None);

        Assert.Equal("ok", r.Status);
        Assert.Equal(new[] { "top1", "top2", "r1", "r2", "r3" }, r.Games.Select(g => g.Id));
        var first = r.Games[0];
        Assert.Equal(("Caruana, Fabiano", 2818, "Carlsen, Magnus", 2882, "white", "2019-08"),
            (first.White, first.WhiteRating!.Value, first.Black, first.BlackRating!.Value, first.Winner!, first.Date!));
        Assert.Null(r.Games[1].Winner);
        Assert.Equal("2021", r.Games[1].Date);
        Assert.Equal("https://lichess.org/top1", first.Url);
        var url = Assert.Single(_handler.Urls);
        Assert.Contains("topGames=3", url);
        Assert.Contains("recentGames=5", url);
        Assert.Contains("moves=0", url);

        // Zweiter Blick: aus dem Arbeitsspeicher.
        await Service().GamesAsync(userId, StartFen, null, Blitz, CancellationToken.None);
        Assert.Single(_handler.Urls);
    }

    [Fact]
    public async Task Games_LocalMasters_HaveNoLichessLink()
    {
        var userId = await UserAsync(token: null);
        _localHandler.Respond(StartKey, GamesJson);

        var r = await Service().GamesAsync(userId, StartFen, "local", ExplorerQuery.Create("masters", null, null), CancellationToken.None);

        Assert.Equal("ok", r.Status);
        Assert.All(r.Games, g => Assert.Null(g.Url));
        Assert.Contains("masters?", Assert.Single(_localHandler.Urls));
        Assert.Contains("topGames=5", _localHandler.Urls[0]);
    }

    [Fact]
    public async Task Games_LocalLichess_KeepTheLink_TheyAreRealLichessGames()
    {
        var userId = await UserAsync(token: null);
        _localHandler.Respond(StartKey, GamesJson);

        var r = await Service().GamesAsync(userId, StartFen, "local", Blitz, CancellationToken.None);

        Assert.Equal("https://lichess.org/r1", r.Games.Single(g => g.Id == "r1").Url);
    }

    [Fact]
    public async Task Games_Online_WithoutToken_SaysSo()
    {
        var userId = await UserAsync(token: null);
        var r = await Service().GamesAsync(userId, StartFen, null, Blitz, CancellationToken.None);
        Assert.Equal("tokenMissing", r.Status);
        Assert.Empty(_handler.Urls);
    }

    [Fact]
    public async Task Games_RejectsWhatIsNoFen()
    {
        var userId = await UserAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Service().GamesAsync(userId, "e4", null, Blitz, CancellationToken.None));
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

        /// <summary>Stellungen, die mit 502 antworten.</summary>
        public readonly HashSet<string> FailKeys = new();
        /// <summary>Stellungen, die so lange brauchen (bricht mit dem Token ab).</summary>
        public readonly Dictionary<string, TimeSpan> SlowKeys = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            lock (Urls) Urls.Add(url);   // der lokale Weg fragt parallel
            LastAuth = request.Headers.Authorization?.ToString();
            if (Status != HttpStatusCode.OK) return new HttpResponseMessage(Status);

            var key = RepertoireReach.Key(System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["fen"] ?? "");
            if (FailKeys.Contains(key)) return new HttpResponseMessage(HttpStatusCode.BadGateway);
            if (SlowKeys.TryGetValue(key, out var delay)) await Task.Delay(delay, ct);
            var json = _byKey.TryGetValue(key, out var j) ? j : """{"white":0,"draws":0,"black":0,"moves":[]}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }
}
