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
using RookHub.Api.Tests.Games;

namespace RookHub.Api.Tests.Engines;

/// <summary>Zugvergleiche der Fehler aus der vertieften Partie-Analyse statt neu gerechnet (2026-11-09, Partie 145): Kandidaten aus
/// der Stellung des Fehlers bzw. der Folgestellung, Antwort-Tests aus der Hauptvariante des besten Zuges, Rest über die Engine.
/// Partie wie <see cref="GameMistakeSelectionTests"/>: Fehler bei Halbzug 2 (2.Qh5, Weiß) und 5 (3…Nf6, Schwarz).</summary>
public class MoveComparisonFromAnalysisTests : IDisposable
{
    private const int House = 7;
    private const int ClubGame = 990211;
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

    public MoveComparisonFromAnalysisTests()
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
        _db.LeagueClubs.Add(new LeagueClub { Id = 990311, Name = "SK A", TeamPrefix = "A", AnonName = "A" });
        _db.LeagueClubGames.Add(new LeagueClubGame { Id = ClubGame, ClubId = 990311, Year = 2026, White = "W", Black = "S", Result = "1-0",
            Plies = 6, Pgn = "1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 1-0", MovesHash = "x" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private ClubMistakeComparisons Club() => new(_db, _svc, NullLogger<ClubMistakeComparisons>.Instance,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ClubMistakeComparisons:MaxActive"] = "1" }).Build());

    /// <summary>Kandidaten wie nach der Vertiefung: Sicht der Seite am Zug, mit Hauptvariante (roh, UCI). Bewertungen der jeweils
    /// ersten Kandidaten wie in <see cref="GameMistakeSelectionTests.Plies"/>, damit dieselben Fehler gewählt werden.</summary>
    private static Dictionary<int, string> Candidates() => new()
    {
        [0] = """[{"uci":"e2e4","cp":30,"pv":["e2e4","e7e5","g1f3","b8c6"]}]""",
        [1] = """[{"uci":"d7d5","cp":-30,"pv":["d7d5","e4d5","d8d5","b1c3"]}]""",
        [2] = """[{"uci":"g1f3","cp":30,"pv":["g1f3","b8c6","f1b5","a7a6"]}]""",
        // Nach 2.Qh5 (Schwarz am Zug): Schwarz steht +0,60 = Weiß −0,60.
        [3] = """[{"uci":"b8c6","cp":60,"pv":["b8c6","f1c4","g7g6","h5f3"]},{"uci":"g7g6","cp":40,"pv":["g7g6","h5f3","g8f6","b1c3"]}]""",
        [4] = """[{"uci":"e1e3","cp":-60}]""",
        // Vor 3…Nf6 (Schwarz am Zug): +3,00 für Schwarz = Weiß −3,00.
        [5] = """[{"uci":"g7g6","cp":300,"pv":["g7g6","h5f3","g8f6","b1c3"]}]""",
        [6] = """[{"uci":"h5f7","mate":1,"pv":["h5f7"]}]""",
    };

    private async Task<GameAnalysis> AnalysisAsync(int id = 990121, bool refined = true, int depth = 30,
        Action<Dictionary<int, string>>? edit = null, Action<List<GameAnalysisPosition>>? positionsEdit = null)
    {
        var cands = Candidates();
        edit?.Invoke(cands);
        var ucis = GameMistakeSelectionTests.Ucis;
        var positions = Enumerable.Range(0, 7).Select(ply => new GameAnalysisPosition
        {
            Ply = ply, Fen = GameMistakeSelectionTests.Fens[ply], GameMoveUci = ply < ucis.Length ? ucis[ply] : "", Depth = depth,
            Refined = refined, CandidatesJson = cands[ply],
        }).ToList();
        positionsEdit?.Invoke(positions);
        var a = new GameAnalysis
        {
            Id = id, UserId = House, Pgn = "", StartFen = "", Origin = GameAnalysisOrigin.Club, LeagueClubGameId = ClubGame,
            Status = GameAnalysisStatus.Done, PlyCount = 6, White = "W", Black = "S", Positions = positions, FinishedAt = DateTime.UtcNow,
            RefineDepth = 30, RefineMultiPv = 5, RefinedAt = DateTime.UtcNow,
        };
        _db.GameAnalyses.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    private Task<MoveComparison> RowAsync(int ply) => _db.MoveComparisons.Include(c => c.Lines).SingleAsync(c => c.Ply == ply);

    [Fact]
    public async Task Enqueue_RefinedAnalysis_GoesStraightToExplaining_NoEngineJob()
    {
        var a = await AnalysisAsync();
        Assert.Equal(2, await ClubMistakeComparisons.EnqueueAsync(_db, a));

        var c = await RowAsync(5);   // 3…Nf6 statt 3…g6: beides aus der Analyse, die einzige Antwort (Qxf7#) geht nach g6 nicht
        Assert.Equal(MoveComparisonStatus.Explaining, c.Status);
        Assert.Equal(MoveComparisonSource.Analysis, c.Source);
        Assert.Equal("g7g6", c.BestUci);
        Assert.Equal(30, c.Depth);
        Assert.All(c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Candidate), l =>
        {
            Assert.Equal(MoveComparisonLineState.Done, l.State);
            Assert.Equal(30, l.ReachedDepth);
            Assert.Null(l.AnalysisJobId);
        });
        var test = Assert.Single(c.Lines, l => l.Kind == MoveComparisonLineKind.Reply);
        Assert.Equal("h5f7", test.ReplyUci);
        Assert.Equal(MoveComparisonLineState.Illegal, test.State);
        Assert.Empty(_db.AnalysisJobs);

        await _svc.PumpOneAsync(c.Id);   // ohne Modell: fertig ohne Text
        Assert.Equal(MoveComparisonStatus.Done, (await RowAsync(5)).Status);
        Assert.Empty(_db.AnalysisJobs);
    }

    [Fact]
    public async Task Sign_BlackToMove_ShownFromBlacksView_StoredAsWhite()
    {
        var a = await AnalysisAsync();
        await ClubMistakeComparisons.EnqueueAsync(_db, a);
        var c = await RowAsync(5);
        // Zeile = Broker-Form, Weiß-Sicht: Schwarz +3,00 → cp −300; Qxf7# nach Nf6 → Weiß setzt matt in 1.
        Assert.Contains("\"cp\":-300", c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == "g7g6").ResultJson);
        Assert.Contains("\"mate\":1", c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == "g8f6").ResultJson);

        var dto = (await _svc.GetForAnalysisAsync(a.Id, c.Id))!;
        Assert.False(dto.WhiteToMove);
        Assert.Equal("analysis", dto.Source);
        var best = dto.Candidates.Single(x => x.Uci == "g7g6");
        var weak = dto.Candidates.Single(x => x.Uci == "g8f6");
        Assert.True(best.IsBest);
        Assert.Equal("+3.00", best.EvalText);   // Sicht der Seite am Zug (Schwarz)
        Assert.Equal("#-1", weak.EvalText);     // Schwarz wird matt
        Assert.Equal(["Qf3", "Nf6", "Nc3"], best.Replies.Single().Line);
        Assert.Equal(["Qxf7#"], weak.Replies.Single().Line);
    }

    [Fact]
    public async Task Sign_WhiteToMove_PlayedMoveFromNextPosition_BestReplyDerived_RestByEngine()
    {
        var a = await AnalysisAsync();
        await ClubMistakeComparisons.EnqueueAsync(_db, a);
        var c = await RowAsync(2);   // 2.Qh5 statt 2.Nf3

        // Partiezug nicht unter den Kandidaten von Halbzug 2 → aus der Folgestellung (Halbzug 3): zwei beste Antworten.
        var weakLine = c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == "d1h5");
        Assert.Equal(MoveComparisonLineState.Done, weakLine.State);
        Assert.Contains("\"cp\":-60", weakLine.ResultJson);   // Schwarz +0,60 → Weiß −0,60
        Assert.Contains("\"cp\":-40", weakLine.ResultJson);
        Assert.Equal("g1f3", c.BestUci);

        // 2…Nc6 ist auch die Hauptvariante nach 2.Nf3 → aus der Analyse; 2…g6 nach 2.Nf3 kennt sie nicht → wartet auf die Engine.
        var tests = c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Reply).OrderBy(l => l.Ordinal).ToList();
        Assert.Equal(["b8c6", "g7g6"], tests.Select(t => t.ReplyUci));
        Assert.Equal(MoveComparisonLineState.Done, tests[0].State);
        Assert.Equal(MoveComparisonLineState.Pending, tests[1].State);
        Assert.Equal(MoveComparisonStatus.Waiting, c.Status);
        Assert.Equal(MoveComparisonSource.Mixed, c.Source);

        var dto = (await _svc.GetForAnalysisAsync(a.Id, c.Id))!;
        Assert.True(dto.WhiteToMove);
        Assert.Equal("mixed", dto.Source);
        var best = dto.Candidates.Single(x => x.Uci == "g1f3");
        var weak = dto.Candidates.Single(x => x.Uci == "d1h5");
        Assert.Equal("+0.30", best.EvalText);
        Assert.Equal("-0.60", weak.EvalText);
        Assert.Equal(["Nc6", "g6"], weak.Replies.Select(r => r.San));
        var derived = weak.Tests.Single(t => t.ReplyUci == "b8c6");
        Assert.Equal("+0.30", derived.EvalText);
        Assert.Equal(["Bb5", "a6"], derived.Line);

        // Die Pumpe rechnet nur den offenen Test: ein Auftrag mit einer Linie, kein Kandidaten-Auftrag.
        await Club().TickAsync();
        c = await RowAsync(2);
        Assert.Equal(MoveComparisonStatus.Replies, c.Status);
        var job = Assert.Single(await _db.AnalysisJobs.ToListAsync());
        Assert.Equal(1, job.MultiPv);
        Assert.Equal(30, job.TargetDepth);
        Assert.Equal(tests[1].Id, c.Lines.Single(l => l.AnalysisJobId == job.Id).Id);

        job.Status = AnalysisJobStatus.Done;
        job.ResultJson = """{"depth":30,"pvs":[{"moves":["f1c4","f8g7"],"cp":45}]}""";
        job.ReachedDepth = 30;
        await _db.SaveChangesAsync();
        await _svc.PumpOneAsync(c.Id);
        Assert.Equal(MoveComparisonStatus.Done, (await RowAsync(2)).Status);   // ohne Modell: fertig ohne Text
        Assert.Empty(_db.AnalysisJobs);
    }

    [Fact]
    public async Task NotRefined_OrTooShallow_FallsBackToEngine()
    {
        var plain = await AnalysisAsync(990122, refined: false);
        await ClubMistakeComparisons.EnqueueAsync(_db, plain);
        var shallow = await AnalysisAsync(990123, depth: 24);   // Vertiefung aufgegeben: Refined, aber Tiefe des ersten Durchgangs
        await ClubMistakeComparisons.EnqueueAsync(_db, shallow);

        var rows = await _db.MoveComparisons.Include(c => c.Lines).ToListAsync();
        Assert.Equal(4, rows.Count);
        Assert.All(rows, c =>
        {
            Assert.Equal(MoveComparisonStatus.Waiting, c.Status);
            Assert.Equal(MoveComparisonSource.Engine, c.Source);
            Assert.All(c.Lines, l => Assert.Equal(MoveComparisonLineKind.Candidate, l.Kind));
            Assert.All(c.Lines, l => Assert.Equal(MoveComparisonLineState.Pending, l.State));
        });

        await Club().TickAsync();   // wie bisher: zwei Kandidaten-Aufträge mit drei Linien
        var jobs = await _db.AnalysisJobs.ToListAsync();
        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j => Assert.Equal(MoveComparisonService.CandidateLines, j.MultiPv));
        Assert.Single(await _db.MoveComparisons.Where(c => c.Status == MoveComparisonStatus.Candidates).ToListAsync());
    }

    [Fact]
    public async Task PlayedMoveAmongCandidates_NoUsableNextPosition_TakesItsMainLine()
    {
        var a = await AnalysisAsync(edit: c => c[2] =
                """[{"uci":"g1f3","cp":30,"pv":["g1f3","b8c6","f1b5","a7a6"]},{"uci":"d1h5","cp":-60,"pv":["d1h5","b8c6","f1c4","g7g6"]}]""",
            positionsEdit: p => p[3].Refined = false);
        await ClubMistakeComparisons.EnqueueAsync(_db, a);
        var c = await RowAsync(2);
        var weakLine = c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == "d1h5");
        Assert.Equal(MoveComparisonLineState.Done, weakLine.State);
        var dto = (await _svc.GetForAnalysisAsync(a.Id, c.Id))!;
        var weak = dto.Candidates.Single(x => x.Uci == "d1h5");
        Assert.Equal("-0.60", weak.EvalText);
        Assert.Equal(["Nc6", "Bc4", "g6"], weak.Replies.Single().Line);
        // Die einzige Antwort (2…Nc6) ist die Hauptvariante nach 2.Nf3 → ganz aus der Analyse.
        Assert.Equal(MoveComparisonSource.Analysis, c.Source);
        Assert.Equal(MoveComparisonStatus.Explaining, c.Status);
    }

    [Fact]
    public async Task TooShortMainLine_OnlyThatCandidateIsCalculated()
    {
        var a = await AnalysisAsync(edit: c => c[5] = """[{"uci":"g7g6","cp":300,"pv":["g7g6"]}]""");   // keine Antwort in der Linie
        await ClubMistakeComparisons.EnqueueAsync(_db, a);
        var c = await RowAsync(5);
        Assert.Equal(MoveComparisonSource.Mixed, c.Source);
        Assert.Equal(MoveComparisonLineState.Pending, c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == "g7g6").State);
        Assert.Equal(MoveComparisonLineState.Done, c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == "g8f6").State);

        // Halbzug 2 läuft schon als Mixed (der offene Antwort-Test) — Deckel frei machen, dann startet Halbzug 5 mit EINEM Auftrag.
        var other = await RowAsync(2);
        other.Status = MoveComparisonStatus.Done;
        await _db.SaveChangesAsync();
        await Club().TickAsync();
        var job = Assert.Single(await _db.AnalysisJobs.ToListAsync());
        Assert.Equal(MoveComparisonService.CandidateLines, job.MultiPv);
        Assert.Equal(c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == "g7g6").Id, (await RowAsync(5)).Lines.Single(l => l.AnalysisJobId == job.Id).Id);
    }

    [Fact]
    public async Task Legacy_WaitingAndRunningComparisons_AreFilledByThePump()
    {
        // Altbestand: angelegt und gestartet, als die Analyse noch nicht vertieft war (Herkunft noch ungeprüft).
        var a = await AnalysisAsync(refined: false);
        await ClubMistakeComparisons.EnqueueAsync(_db, a);
        await Club().TickAsync();   // Halbzug 2 rechnet (zwei Aufträge), Halbzug 5 wartet
        Assert.Equal(2, await _db.AnalysisJobs.CountAsync());
        foreach (var row in await _db.MoveComparisons.ToListAsync()) row.Source = null;
        foreach (var p in await _db.GameAnalysisPositions.ToListAsync()) p.Refined = true;   // jetzt vertieft
        await _db.SaveChangesAsync();

        await Club().TickAsync();

        var waiting = await RowAsync(5);   // war wartend → direkt erklären, kein Auftrag
        Assert.Equal(MoveComparisonSource.Analysis, waiting.Source);
        Assert.Equal(MoveComparisonStatus.Explaining, waiting.Status);
        var running = await RowAsync(2);   // rechnete schon → Kandidaten übernommen, ihre Aufträge weg
        Assert.Equal(MoveComparisonSource.Mixed, running.Source);
        Assert.All(running.Lines.Where(l => l.Kind == MoveComparisonLineKind.Candidate), l =>
        {
            Assert.Equal(MoveComparisonLineState.Done, l.State);
            Assert.Null(l.AnalysisJobId);
        });
        Assert.Empty(_db.AnalysisJobs);

        await _svc.PumpOneAsync(running.Id);   // weiter wie gewohnt: nur der unbekannte Antwort-Test geht an die Engine
        running = await RowAsync(2);
        Assert.Equal(MoveComparisonStatus.Replies, running.Status);
        var job = Assert.Single(await _db.AnalysisJobs.ToListAsync());
        Assert.Equal(1, job.MultiPv);
        Assert.Equal("g7g6", running.Lines.Single(l => l.AnalysisJobId == job.Id).ReplyUci);
    }

    [Fact]
    public async Task UserComparison_IsMarkedEngine()
    {
        var dto = await _svc.CreateAsync(House, new RookHub.Api.DTOs.Engines.CreateMoveComparisonRequest
        {
            Fen = GameMistakeSelectionTests.Fens[2], Moves = ["g1f3", "d1h5"],
        });
        Assert.Equal("engine", dto.Source);
        Assert.Equal(MoveComparisonSource.Engine, (await _db.MoveComparisons.SingleAsync()).Source);
    }
}
