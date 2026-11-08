using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.League;
using RookHub.Api.Tests.Games;

namespace RookHub.Api.Tests.Engines;

/// <summary>Zugvergleiche zu den Fehlern einer Vereinspartie (0.908.0): anlegen beim endgültigen Ende der Hauptanalyse,
/// gedeckelt als Stapel-Hintergrundarbeit starten, nachtragen, lesen nur über die Partie, weg mit der Analyse.</summary>
public class ClubMistakeComparisonsTests : IDisposable
{
    private const int House = 5;
    private const int ClubGame = 990201;
    private readonly AppDbContext _db;
    private readonly AnalysisJobService _jobs;
    private readonly MoveComparisonService _svc;

    private sealed class FakeControl : IAnalysisJobControl
    {
        public void Interrupt(int jobId) { }
        public void PreemptBackground(string engineId) { }
    }

    private sealed class NoLlm : IClaudeJsonClient
    {
        public bool IsConfigured => false;
        public bool IsLocal => false;
        public string TranslationModel => "none";
        public Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> CompleteJsonAsync(string purpose, string system, string userPrompt, JsonNode schema, int maxTokens, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    public ClubMistakeComparisonsTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _jobs = new AnalysisJobService(_db, new FakeControl());
        _svc = new MoveComparisonService(_db, _jobs, new NoLlm(), new MoveComparisonExplainJobs(),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MoveComparisonService>.Instance);
        var encryption = new EncryptionService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
        }).Build());
        _db.AppUsers.Add(new AppUser { Id = House, Username = "house", PasswordHash = "x", IsAdmin = true });
        _db.LichessEngineCredentials.Add(new LichessEngineCredential
        {
            UserId = House, EncryptedToken = encryption.Encrypt("lip_tok"), BackgroundEngineIds = "eei_bg",
        });
        _db.LeagueClubs.Add(new LeagueClub { Id = 990301, Name = "SK A", TeamPrefix = "A", AnonName = "A" });
        _db.LeagueClubs.Add(new LeagueClub { Id = 990302, Name = "SK B", TeamPrefix = "B", AnonName = "B" });
        _db.LeagueClubGames.Add(new LeagueClubGame { Id = ClubGame, ClubId = 990301, Year = 2026, White = "W", Black = "S", Result = "1-0",
            Plies = 6, Pgn = "1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 1-0", MovesHash = "x" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private ClubMistakeComparisons Club(int maxActive = 1) => new(_db, _svc, NullLogger<ClubMistakeComparisons>.Instance,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ClubMistakeComparisons:MaxActive"] = maxActive.ToString(),
        }).Build());

    /// <summary>Die kleine Partie aus <see cref="GameMistakeSelectionTests"/> als Analyse (Kandidaten aus Sicht der Seite am Zug):
    /// Fehler bei Halbzug 2 (2.Qh5) und 5 (3…Nf6), 3.Bc4 hat einen unspielbaren „Bestzug".</summary>
    private async Task<GameAnalysis> AnalysisAsync(int id = 990101, string? engineId = null, GameAnalysisStatus status = GameAnalysisStatus.Done,
        DateTime? finished = null)
    {
        string Cand(string uci, int whiteCp, bool white) => $"[{{\"uci\":\"{uci}\",\"cp\":{(white ? whiteCp : -whiteCp)}}}]";
        var plies = GameMistakeSelectionTests.Plies();
        var positions = plies.Select(p => new GameAnalysisPosition
        {
            Ply = p.Ply, Fen = GameMistakeSelectionTests.Fens[p.Ply], GameMoveUci = GameMistakeSelectionTests.Ucis[p.Ply], Depth = 20,
            CandidatesJson = Cand(p.BestUci!, p.Cp!.Value, p.Ply % 2 == 0),
        }).ToList();
        positions.Add(new GameAnalysisPosition { Ply = 6, Fen = GameMistakeSelectionTests.Fens[6], GameMoveUci = "", Depth = 20,
            CandidatesJson = "[{\"uci\":\"h5f7\",\"mate\":1}]" });
        var a = new GameAnalysis
        {
            Id = id, UserId = House, Pgn = "", StartFen = "", Origin = GameAnalysisOrigin.Club, LeagueClubGameId = ClubGame,
            EngineId = engineId, Status = status, PlyCount = 6, White = "W", Black = "S", Positions = positions,
            FinishedAt = finished ?? DateTime.UtcNow,
        };
        _db.GameAnalyses.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    [Fact]
    public async Task Enqueue_OneWaitingComparisonPerMistake_Depth35_German_Idempotent()
    {
        var a = await AnalysisAsync();
        Assert.Equal(2, await ClubMistakeComparisons.EnqueueAsync(_db, a));

        var rows = await _db.MoveComparisons.Include(c => c.Lines).OrderBy(c => c.Ply).ToListAsync();
        Assert.Equal([2, 5], rows.Select(r => r.Ply!.Value));
        Assert.All(rows, r =>
        {
            Assert.Equal(MoveComparisonStatus.Waiting, r.Status);
            Assert.Equal(35, r.Depth);
            Assert.Equal("de", r.Language);
            Assert.Equal(House, r.UserId);
            Assert.Equal(a.Id, r.GameAnalysisId);
        });
        Assert.Equal(["g1f3", "d1h5"], rows[0].Lines.OrderBy(l => l.Ordinal).Select(l => l.CandidateUci));   // [bester, Partiezug]
        Assert.Empty(_db.AnalysisJobs);   // noch kein Auftrag — die Pumpe startet gedeckelt
        Assert.NotNull(a.MistakeComparisonsAt);

        Assert.Equal(0, await ClubMistakeComparisons.EnqueueAsync(_db, a));
        a.MistakeComparisonsAt = null;   // auch ohne Marke: je Halbzug höchstens einer
        Assert.Equal(0, await ClubMistakeComparisons.EnqueueAsync(_db, a));
        Assert.Equal(2, await _db.MoveComparisons.CountAsync());
    }

    [Fact]
    public async Task Enqueue_NotForLc0_OrUnfinished()
    {
        var lc0 = await AnalysisAsync(990102, engineId: "eei_lc0");
        var running = await AnalysisAsync(990103, status: GameAnalysisStatus.Running);
        Assert.Equal(0, await ClubMistakeComparisons.EnqueueAsync(_db, lc0));
        Assert.Equal(0, await ClubMistakeComparisons.EnqueueAsync(_db, running));
        Assert.Empty(_db.MoveComparisons);
    }

    [Fact]
    public async Task Tick_BackfillsThenStartsAtMostMaxActive_AsBatchBackground_OutsideUserLimits()
    {
        await AnalysisAsync();   // fertig, aber nie eingereiht (Altbestand)
        await Club().TickAsync();

        var rows = await _db.MoveComparisons.OrderBy(c => c.Ply).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(MoveComparisonStatus.Candidates, rows[0].Status);
        Assert.Equal(MoveComparisonStatus.Waiting, rows[1].Status);
        var jobs = await _db.AnalysisJobs.ToListAsync();
        Assert.Equal(2, jobs.Count);   // bester Zug + Partiezug
        Assert.All(jobs, j => { Assert.True(j.Background); Assert.True(j.Batch); Assert.Equal(35, j.TargetDepth); });

        // Für das Haus-Konto unsichtbar und ohne Wirkung auf seine Deckel.
        Assert.Empty(await _svc.ListAsync(House));
        Assert.Equal(0, (await _svc.StatusAsync(House)).OpenComparisons);
        Assert.Null(await _svc.GetAsync(House, rows[0].Id));
        Assert.False(await _svc.DeleteAsync(House, rows[0].Id));

        await Club().TickAsync();   // eins läuft schon — der zweite wartet
        Assert.Equal(MoveComparisonStatus.Waiting, (await _db.MoveComparisons.FindAsync(rows[1].Id))!.Status);
    }

    [Fact]
    public async Task Start_NewestFinishedAnalysisFirst()
    {
        var old = await AnalysisAsync(990104, finished: DateTime.UtcNow.AddDays(-3));
        var fresh = await AnalysisAsync(990105, finished: DateTime.UtcNow);
        await ClubMistakeComparisons.EnqueueAsync(_db, old);
        await ClubMistakeComparisons.EnqueueAsync(_db, fresh);
        await Club().TickAsync();
        var started = await _db.MoveComparisons.SingleAsync(c => c.Status == MoveComparisonStatus.Candidates);
        Assert.Equal(fresh.Id, started.GameAnalysisId);
        Assert.Equal(2, started.Ply);
    }

    [Fact]
    public async Task DeleteForAnalysis_RemovesRowsAndJobs_ResetsMarker()
    {
        var a = await AnalysisAsync();
        await ClubMistakeComparisons.EnqueueAsync(_db, a);
        await Club().TickAsync();
        Assert.NotEmpty(_db.AnalysisJobs);

        Assert.Equal(2, await ClubMistakeComparisons.DeleteForAnalysisAsync(_db, _jobs, a));
        await _db.SaveChangesAsync();
        Assert.Empty(_db.MoveComparisons);
        Assert.Empty(_db.MoveComparisonLines);
        Assert.Empty(_db.AnalysisJobs);
        Assert.Null(a.MistakeComparisonsAt);
    }

    [Fact]
    public async Task Read_OnlyThroughTheClubOfTheGame()
    {
        var a = await AnalysisAsync();
        await ClubMistakeComparisons.EnqueueAsync(_db, a);
        var reader = new ClubGameComparisons(_db, _svc);
        var own = await _db.LeagueClubs.FindAsync(990301);
        var other = await _db.LeagueClubs.FindAsync(990302);

        var list = await reader.ForGameAsync(own!, ClubGame);
        Assert.NotNull(list);
        Assert.Equal([2, 5], list!.Select(x => x.Ply));
        Assert.Equal("d1h5", list[0].PlayedUci);
        Assert.Equal("g1f3", list[0].AnalysisBestUci);
        Assert.Equal("waiting", list[0].Comparison.Status);
        Assert.NotNull(await reader.GetAsync(own!, ClubGame, list[0].Comparison.Id));

        Assert.Null(await reader.ForGameAsync(other!, ClubGame));
        Assert.Null(await reader.GetAsync(other!, ClubGame, list[0].Comparison.Id));
        Assert.Null(await reader.ForGameAsync(own!, 424242));
    }

    // ── Antwort nach dem besten Zug: noch derselbe Zug? (Befund Partie 236) ──────────────────────────

    [Theory]
    // Nach dem schwächeren Zug schlägt Nxd7 einen Läufer, nach dem besten steht d7 leer → verworfen.
    [InlineData("4k3/3b4/8/4N3/8/8/8/4K3 w - - 0 1", "4k3/8/8/4N3/8/8/8/4K3 w - - 0 1", "e5d7", false)]
    // Ein stiller Zug bleibt still → gilt.
    [InlineData("4k3/8/8/4N3/8/8/8/4K3 w - - 0 1", "4k3/8/8/4N3/8/8/8/4K3 w - - 0 1", "e5f7", true)]
    // Still dort, schlägt hier → gilt (genau das kann der Grund sein).
    [InlineData("4k3/8/8/4N3/8/8/8/4K3 w - - 0 1", "4k3/3b4/8/4N3/8/8/8/4K3 w - - 0 1", "e5d7", true)]
    // Andere Figur auf dem Startfeld → verworfen.
    [InlineData("4k3/8/8/4N3/8/8/8/4K3 w - - 0 1", "4k3/8/8/4B3/8/8/8/4K3 w - - 0 1", "e5d6", false)]
    // En passant schlägt auch ohne Figur auf dem Zielfeld.
    [InlineData("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1", "4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1", "e5d6", true)]
    public void ReplyTransfers_Rule(string weakFen, string bestFen, string uci, bool expected)
        => Assert.Equal(expected, MoveComparisonService.ReplyTransfers(weakFen, bestFen, uci));
}
