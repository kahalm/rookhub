using System.Net;
using System.Text;
using Chess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Library;

namespace RookHub.Api.Tests.League;

/// <summary>
/// Vorausanalyse laufender Ligapartien der eigenen Mannschaft (1.034.0, Wunsch 2026-11-26: „Tiefe 30 + 5 Linien schon während der
/// Bundesliga, nur für Schwaz“): Stellungen laufender Partien werden auf der untersten Stufe eingereiht, die Analyse der später
/// eingespielten Partie übernimmt fertige (Cache) und offene (Übernahme) statt neu zu rechnen. Ids im 99xxxx-Bereich.
/// </summary>
public class ClubBroadcastPreAnalysisTests : IDisposable
{
    private const int ClubId = 990201;
    private const string Engine = "eei_prep";
    private const string Moves = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6";
    private static readonly string Today = DateTime.UtcNow.ToString("yyyy.MM.dd");

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private int _owner;

    public void Dispose() => _db.Dispose();

    private static string Game(string result = "*", string whiteTeam = "Schachklub Schwaz", string blackTeam = "Pinggau-Friedberg",
        string moves = Moves, string? date = null, string url = "https://lichess.org/broadcast/bl/round-2/L3MX2o99/pRe99999") =>
        $"[Event \"1. Bundesliga AUT 2026/2027\"]\n[Date \"{date ?? Today}\"]\n[Round \"2.1\"]\n[White \"Schmider, Niklas\"]\n"
        + $"[Black \"Vaibhav, Suri\"]\n[Result \"{result}\"]\n[WhiteTeam \"{whiteTeam}\"]\n[WhiteFideId \"990301\"]\n"
        + $"[BlackTeam \"{blackTeam}\"]\n[BlackFideId \"990302\"]\n[Variant \"Standard\"]\n[GameURL \"{url}\"]\n\n"
        + $"{moves} {{ [%clk 1:30:54] }} {result}\n\n";

    private async Task SeedAsync()
    {
        _db.LeagueClubs.Add(new LeagueClub { Id = ClubId, Name = "SK Schwaz", TeamPrefix = "Schwaz", AnonName = "Schwaz", Region = LeagueRegions.Tirol });
        var user = new AppUser { Id = 990210, Username = "haus", Email = "h@t.com", PasswordHash = "h", IsAdmin = true };
        _db.AppUsers.Add(user);
        _db.LichessEngineCredentials.Add(new LichessEngineCredential { UserId = user.Id, EncryptedToken = "enc", BackgroundEngineIds = Engine });
        await _db.SaveChangesAsync();
        _owner = user.Id;
    }

    private IConfiguration Config(bool enabled = true) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["MasterAnalysis:OwnerUserId"] = _owner.ToString(),
        ["LiveBoards:PreAnalysis:Enabled"] = enabled ? "true" : "false",
    }).Build();

    private ClubBroadcastPreAnalysis Prep(bool enabled = true) =>
        new(_db, new AnalysisJobService(_db, null), NullLogger<ClubBroadcastPreAnalysis>.Instance, Config(enabled));

    private Task<List<AnalysisJob>> PrepJobsAsync() => _db.AnalysisJobs.Where(j => j.Prep).OrderBy(j => j.Id).ToListAsync();

    /// <summary>Ein fertiges Ergebnis mit fünf (oder so vielen wie legal) Linien für <paramref name="fen"/>.</summary>
    private static string ResultFor(string fen, int depth)
    {
        var moves = ChessBoard.LoadFromFen(fen).Moves().Take(5).Select(GamePlies.ToUci).ToList();
        var pvs = string.Join(',', moves.Select((m, i) => $"{{\"depth\":{depth},\"cp\":{30 - i},\"moves\":[\"{m}\"]}}"));
        return $"{{\"depth\":{depth},\"pvs\":[{pvs}]}}";
    }

    private static void Finish(AnalysisJob j)
    {
        j.Status = AnalysisJobStatus.Done;
        j.ReachedDepth = j.TargetDepth;
        j.ResultJson = ResultFor(j.Fen, j.TargetDepth);
        j.FinishedAt = DateTime.UtcNow;
    }

    [Fact]
    public void OngoingGames_TakesOnlyGamesWithoutResult()
    {
        var games = ClubBroadcastPreAnalysis.OngoingGames(Game() + Game(result: "1-0", url: "https://x/2") + Game(moves: ""));
        var g = Assert.Single(games);
        Assert.Equal(10, g.Sans.Count);
        Assert.Equal("Schachklub Schwaz", g.H("WhiteTeam"));
    }

    [Fact]
    public async Task Enqueue_OwnClubGame_EveryPositionAtLowestPriority_Depth30_FiveLines()
    {
        await SeedAsync();
        var n = await Prep().EnqueueAsync(ClubBroadcastPreAnalysis.OngoingGames(Game()), default);

        var jobs = await PrepJobsAsync();
        Assert.Equal(11, n);                                         // 10 Halbzüge + die aktuelle Stellung
        Assert.Equal(11, jobs.Count);
        Assert.All(jobs, j =>
        {
            Assert.Equal(JobPriority.LivePrep, j.Priority);
            Assert.Equal(GameAnalysisDefaults.ClubRefineDepth, j.TargetDepth);
            Assert.Equal(30, j.TargetDepth);
            Assert.Equal(GameAnalysisDefaults.MultiPv, j.MultiPv);
            Assert.Equal(5, j.MultiPv);
            Assert.True(j.Background && j.Batch);
            Assert.Equal(Engine, j.EngineId);
            Assert.Equal(_owner, j.UserId);
        });
        // Nicht in der Auftragsliste des Haus-Kontos
        Assert.Empty(await new AnalysisJobService(_db, null).ListAsync(_owner));

        // Der nächste Takt: nichts doppelt, nur die neuen Stellungen der fortgesetzten Partie
        Assert.Equal(0, await Prep().EnqueueAsync(ClubBroadcastPreAnalysis.OngoingGames(Game()), default));
        Assert.Equal(2, await Prep().EnqueueAsync(ClubBroadcastPreAnalysis.OngoingGames(Game(moves: Moves + " 6. Be3 e5")), default));
    }

    [Fact]
    public async Task Enqueue_OtherClubs_OldGames_OrSwitchOff_QueueNothing()
    {
        await SeedAsync();
        Assert.Equal(0, await Prep().EnqueueAsync(ClubBroadcastPreAnalysis.OngoingGames(Game(whiteTeam: "Jenbach", blackTeam: "Pinggau-Friedberg")), default));
        Assert.Equal(0, await Prep().EnqueueAsync(ClubBroadcastPreAnalysis.OngoingGames(Game(date: "2026.07.05")), default));
        Assert.Equal(0, await Prep(enabled: false).EnqueueAsync(ClubBroadcastPreAnalysis.OngoingGames(Game()), default));
        Assert.Empty(_db.AnalysisJobs);
    }

    [Fact]
    public async Task Enqueue_PositionAlreadyCachedDeepEnough_IsSkipped()
    {
        await SeedAsync();
        var fens = ClubBroadcastPreAnalysis.PositionsOf(ClubBroadcastPreAnalysis.OngoingGames(Game())[0].Sans);
        var a = new GameAnalysis { UserId = _owner, Pgn = "", StartFen = "", Status = GameAnalysisStatus.Done, PlyCount = 1, TargetDepth = 30,
            MultiPv = 5, White = "W", Black = "S" };
        _db.GameAnalyses.Add(a);
        await _db.SaveChangesAsync();
        var cand = BrokerCandidates.ToJson(BrokerCandidates.Parse(ResultFor(fens[0], 30), fens[0])!);
        _db.GameAnalysisPositions.Add(new GameAnalysisPosition { GameAnalysisId = a.Id, Ply = 0, Fen = fens[0], GameMoveUci = "e2e4",
            GameMoveSan = "e4", Depth = 31, CandidatesJson = cand });
        await _db.SaveChangesAsync();

        Assert.Equal(10, await Prep().EnqueueAsync(ClubBroadcastPreAnalysis.OngoingGames(Game()), default));
        Assert.DoesNotContain(await PrepJobsAsync(), j => j.Fen == fens[0]);
    }

    [Fact]
    public async Task ImportedClubGame_ReusesFinishedPreAnalysis_NoNewJobs_AndIsRefinedRightAway()
    {
        await SeedAsync();
        await Prep().EnqueueAsync(ClubBroadcastPreAnalysis.OngoingGames(Game()), default);
        foreach (var j in await PrepJobsAsync()) Finish(j);
        await _db.SaveChangesAsync();

        // Der Cache findet die Vorausanalyse für beide Durchgänge (Tiefe 20 und 30, fünf Linien)
        var fen = (await PrepJobsAsync())[3].Fen;
        Assert.Equal(30, (await GameAnalysisPositionCache.FindAsync(_db, 0, fen, 30, 5, default))!.Depth);
        Assert.NotNull(await GameAnalysisPositionCache.FindAsync(_db, 0, fen, 20, 5, default));
        Assert.Null(await GameAnalysisPositionCache.FindAsync(_db, 0, fen, 31, 5, default));

        var svc = Analyses();
        var dto = await svc.CreateClubBatchAsync(_owner, 990250, $"[Result \"1-0\"]\n\n{Moves} 1-0");
        await PumpUntilRefinedAsync(svc, dto.Id);

        var analysis = await _db.GameAnalyses.Include(g => g.Positions).SingleAsync(g => g.Id == dto.Id);
        Assert.Equal(GameAnalysisStatus.Done, analysis.Status);
        Assert.NotNull(analysis.RefinedAt);
        Assert.All(analysis.Positions, p => { Assert.Equal(30, p.Depth); Assert.True(p.Refined); });
        Assert.Empty(_db.AnalysisJobs.Where(j => !j.Prep));          // kein einziger neuer Auftrag
    }

    [Fact]
    public async Task ImportedClubGame_AdoptsOpenPreAnalysis_InsteadOfQueueingTwice()
    {
        await SeedAsync();
        await Prep().EnqueueAsync(ClubBroadcastPreAnalysis.OngoingGames(Game()), default);
        var prepIds = (await PrepJobsAsync()).Select(j => j.Id).ToHashSet();

        var svc = Analyses();
        var dto = await svc.CreateClubBatchAsync(_owner, 990251, $"[Result \"1-0\"]\n\n{Moves} 1-0");
        await svc.PumpOneAsync(dto.Id);

        var analysis = await _db.GameAnalyses.Include(g => g.Positions).SingleAsync(g => g.Id == dto.Id);
        Assert.Equal(prepIds.Count, _db.AnalysisJobs.Count());        // übernommen, nicht doppelt angelegt
        Assert.All(analysis.Positions, p => Assert.Contains(p.AnalysisJobId!.Value, prepIds));
        Assert.All(await PrepJobsAsync(), j => Assert.Equal(JobPriority.ClubNew, j.Priority));   // Stufe der Vereinspartie

        // Fertig gerechnet → eingesammelt, gleich vertieft, keine Vertiefungsaufträge
        foreach (var j in await PrepJobsAsync()) Finish(j);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        await PumpUntilRefinedAsync(svc, dto.Id);
        analysis = await _db.GameAnalyses.Include(g => g.Positions).SingleAsync(g => g.Id == dto.Id);
        Assert.NotNull(analysis.RefinedAt);
        Assert.All(analysis.Positions, p => Assert.True(p.Refined));
        Assert.Empty(_db.AnalysisJobs);
    }

    [Fact]
    public async Task Worker_PicksEveryOtherKindBeforePreAnalysis()
    {
        await SeedAsync();
        await Prep().EnqueueAsync(ClubBroadcastPreAnalysis.OngoingGames(Game()), default);
        var jobs = new AnalysisJobService(_db, null);
        var league = await jobs.CreateAsync(_owner, new DTOs.Engines.CreateAnalysisJobRequest
        {
            Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", TargetDepth = 20, MultiPv = 5,
        }, remember: false, background: true, batch: true, priority: JobPriority.Master);

        Assert.Equal(league.Id, (await jobs.PickNextForEngineAsync(Engine, DateTime.UtcNow.AddMinutes(1)))!.Id);
        Assert.True(JobPriority.LivePrep > Enum.GetValues<JobPriority>().Where(p => p != JobPriority.LivePrep).Max());
        Assert.Equal("livePrep", EngineOverviewService.KindOf(JobPriority.LivePrep));
        _db.AnalysisJobs.Remove(await _db.AnalysisJobs.FindAsync(league.Id) ?? throw new InvalidOperationException());
        await _db.SaveChangesAsync();
        Assert.True((await jobs.PickNextForEngineAsync(Engine, DateTime.UtcNow.AddMinutes(1)))!.Prep);
    }

    [Fact]
    public async Task Cleanup_RemovesOldPreAnalysis_KeepsFreshAndAdopted()
    {
        await SeedAsync();
        await Prep().EnqueueAsync(ClubBroadcastPreAnalysis.OngoingGames(Game()), default);
        var all = await PrepJobsAsync();
        foreach (var j in all.Take(5)) j.CreatedAt = DateTime.UtcNow.AddDays(-8);
        var a = new GameAnalysis { UserId = _owner, Pgn = "", StartFen = "", PlyCount = 1, White = "W", Black = "S" };
        _db.GameAnalyses.Add(a);
        await _db.SaveChangesAsync();
        _db.GameAnalysisPositions.Add(new GameAnalysisPosition { GameAnalysisId = a.Id, Ply = 0, Fen = all[0].Fen, GameMoveUci = "e2e4",
            GameMoveSan = "e4", AnalysisJobId = all[0].Id });
        await _db.SaveChangesAsync();

        Assert.Equal(4, await Prep().CleanupAsync(default));
        Assert.Equal(all.Count - 4, (await PrepJobsAsync()).Count);
    }

    [Fact]
    public async Task BroadcastImport_FeedsOngoingOwnGames_FromTheSamePgn()
    {
        await SeedAsync();
        _db.LeagueBroadcasts.Add(new LeagueBroadcast { TourId = "IjDgZV99", Name = "1. Bundesliga", FoundAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        var detail = "{\"tour\":{\"id\":\"IjDgZV99\",\"name\":\"1. Bundesliga Austria 2026/2027\"},\"rounds\":[{\"id\":\"r2\",\"name\":\"Round 2\",\"ongoing\":true}]}";
        var http = new FakeHttp(u => u.EndsWith(".pgn") ? Ok(Game()) : Ok(detail));
        var import = new LeagueBroadcastImport(_db, new HttpClient(http), NullLogger<LeagueBroadcastImport>.Instance, Config(),
            prep: Prep()) { Pause = TimeSpan.Zero };

        await import.ImportAsync(await _db.LeagueBroadcasts.SingleAsync(), default);

        Assert.Equal(11, (await PrepJobsAsync()).Count);
        Assert.Equal(2, http.Urls.Count);                              // kein zusätzlicher Lichess-Abruf
    }

    private GameAnalysisService Analyses()
    {
        var jobs = new AnalysisJobService(_db, null);
        return new GameAnalysisService(_db, jobs, new CommentSetService(_db, NullLogger<CommentSetService>.Instance),
            NullLogger<GameAnalysisService>.Instance, null);
    }

    private async Task PumpUntilRefinedAsync(GameAnalysisService svc, int id)
    {
        for (var i = 0; i < 20; i++)
        {
            await svc.PumpOneAsync(id);
            _db.ChangeTracker.Clear();
            var a = await _db.GameAnalyses.AsNoTracking().SingleAsync(g => g.Id == id);
            if (a.Status == GameAnalysisStatus.Done && a.RefinedAt != null) return;
        }
    }

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
}
