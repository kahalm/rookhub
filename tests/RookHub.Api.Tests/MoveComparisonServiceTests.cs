using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>„Züge vergleichen" (0.602.0): Kandidaten rechnen, den besten bestimmen, die besten Antworten auf die
/// schwächeren gegen den besten testen, erklären lassen.</summary>
public class MoveComparisonServiceTests : IDisposable
{
    private const string START = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    /// <summary>1.e4 d5 — Weiß am Zug: exd5 gegen Nc3; die Antwort dxe4 gibt es nach exd5 nicht mehr.</summary>
    private const string SCANDI = "rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2";

    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly AnalysisJobService _jobs;
    private readonly FakeLlm _llm = new();
    private readonly MoveComparisonService _svc;

    private sealed class FakeControl : IAnalysisJobControl
    {
        public void Interrupt(int jobId) { }
        public void PreemptBackground(string engineId) { }
    }

    private sealed class FakeLlm : IClaudeJsonClient
    {
        public bool IsConfigured { get; set; } = true;
        public bool Local { get; set; }
        public bool IsLocal => Local;
        public string TranslationModel => "fake-local";
        public Queue<string?> Answers { get; } = new();
        public List<string> Prompts { get; } = new();
        public Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> CompleteJsonAsync(string purpose, string system, string userPrompt, JsonNode schema, int maxTokens, CancellationToken ct = default)
        {
            lock (Prompts) { Prompts.Add(userPrompt); return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : null); }
        }
    }

    public MoveComparisonServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
        }).Build();
        _encryption = new EncryptionService(config);
        _jobs = new AnalysisJobService(_db, new FakeControl());
        _svc = new MoveComparisonService(_db, _jobs, _llm, new MoveComparisonExplainJobs(),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MoveComparisonService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> UserWithEngineAsync(int id = 5)
    {
        _db.AppUsers.Add(new AppUser { Id = id, Username = $"u{id}", PasswordHash = "x" });
        _db.LichessEngineCredentials.Add(new LichessEngineCredential
        {
            UserId = id, EncryptedToken = _encryption.Encrypt("lip_tok"), BackgroundEngineIds = "eei_bg",
        });
        await _db.SaveChangesAsync();
        return id;
    }

    /// <summary>Den Auftrag einer Zeile fertig rechnen lassen (so, wie der Worker ihn hinterließe).</summary>
    private async Task FinishJobAsync(string fen, string resultJson, int depth = 22)
    {
        var job = await _db.AnalysisJobs.SingleAsync(j => j.Fen == fen);
        job.Status = AnalysisJobStatus.Done;
        job.ReachedDepth = depth;
        job.ResultJson = resultJson;
        await _db.SaveChangesAsync();
    }

    private static string Pvs(params (int cp, string moves)[] pvs)
        => "{\"depth\":22,\"time\":1000,\"nodes\":5000000,\"pvs\":["
           + string.Join(',', pvs.Select(p => $"{{\"depth\":22,\"cp\":{p.cp},\"moves\":[{string.Join(',', p.moves.Split(' ').Select(m => $"\"{m}\""))}]}}"))
           + "]}";

    private Task<MoveComparisonDto> CreateAsync(int user, string fen, params string[] moves)
        => _svc.CreateAsync(user, new CreateMoveComparisonRequest { Fen = fen, Moves = moves.ToList(), Depth = 22, Lang = "de" });

    // ── Anlegen ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_rechnetJeKandidatDieStellungDanach_mitDreiLinien_undVersteckt()
    {
        var u = await UserWithEngineAsync();
        var dto = await CreateAsync(u, START, "e2e4", "d2d4");

        Assert.Equal("candidates", dto.Status);
        Assert.Equal(new[] { "e4", "d4" }, dto.Candidates.Select(c => c.San));
        var jobs = await _db.AnalysisJobs.OrderBy(j => j.Id).ToListAsync();
        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j => Assert.Equal(MoveComparisonService.CandidateLines, j.MultiPv));
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", jobs[0].Fen);
        // Teilrechnungen gehören zum Vergleich: nicht in die Auftragsliste, nicht in „Gemerkte Stellungen"
        Assert.Empty(await _jobs.ListAsync(u));
        Assert.Empty(_db.RememberedPositions);
    }

    [Theory]
    [InlineData(new[] { "e2e4" }, "too-few-moves")]
    [InlineData(new[] { "e2e4", "d2d4", "c2c4", "g1f3", "b1c3" }, "too-many-moves")]
    [InlineData(new[] { "e2e4", "e2e5" }, "invalid-move")]
    public async Task Create_weistUngueltigeAuswahlAb(string[] moves, string reason)
    {
        var u = await UserWithEngineAsync();
        var ex = await Assert.ThrowsAsync<MoveComparisonException>(() => CreateAsync(u, START, moves));
        Assert.Equal(reason, ex.Reason);
        Assert.Empty(_db.MoveComparisons);
    }

    [Fact]
    public async Task Create_ohneEngineUndOhneHausEngine_istEineAbsage()
    {
        _db.AppUsers.Add(new AppUser { Id = 9, Username = "ohne", PasswordHash = "x" });
        await _db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<MoveComparisonException>(() => CreateAsync(9, START, "e2e4", "d2d4"));
        Assert.Equal("no-engine", ex.Reason);
    }

    [Fact]
    public async Task Create_doppelterZug_zaehltEinmal_undRochadeAlsKoenigSchlaegtTurm()
    {
        var u = await UserWithEngineAsync();
        const string castle = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4";
        var dto = await CreateAsync(u, castle, "e1h1", "e1g1", "d2d3");
        Assert.Equal(new[] { "O-O", "d3" }, dto.Candidates.Select(c => c.San));
    }

    /// <summary>D1-002: Die Auftraege eines Vergleichs auf der Haus-Engine fehlen zwar in der Auftragsliste, sind per Id
    /// aber mit PUT /api/analysis-jobs/{id} erreichbar. Der Tiefendeckel (<see cref="MoveComparisonService.HouseMaxDepth"/>)
    /// darf dort nicht wieder aufgehen — Tiefe, Linien und Engine aendert nur ein Admin (Riegel aus A4-001).</summary>
    [Fact]
    public async Task HausEngine_AuftraegeDesVergleichs_TiefeLinienEngine_ohneAdminNichtAenderbar()
    {
        _db.AppUsers.Add(new AppUser { Id = 1, Username = "haus", PasswordHash = "x", IsAdmin = true });
        _db.LichessEngineCredentials.Add(new LichessEngineCredential
        {
            UserId = 1, EncryptedToken = _encryption.Encrypt("lip_haus"), BackgroundEngineIds = "eei_haus", ShareAsHouseEngine = true,
        });
        _db.AppUsers.Add(new AppUser { Id = 9, Username = "ohne", PasswordHash = "x" });
        await _db.SaveChangesAsync();

        await _svc.CreateAsync(9, new CreateMoveComparisonRequest { Fen = START, Moves = ["e2e4", "d2d4"], Depth = 60, Lang = "de" });
        var jobs = await _db.AnalysisJobs.AsNoTracking().OrderBy(j => j.Id).ToListAsync();
        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j =>
        {
            Assert.Equal(9, j.UserId);
            Assert.Equal(1, j.EngineOwnerUserId);
            Assert.Equal(MoveComparisonService.HouseMaxDepth, j.TargetDepth);
        });

        var id = jobs[0].Id;
        await Assert.ThrowsAsync<ArgumentException>(() => _jobs.UpdateAsync(9, id, new UpdateAnalysisJobRequest { TargetDepth = 60 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _jobs.UpdateAsync(9, id, new UpdateAnalysisJobRequest { MultiPv = 5 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _jobs.UpdateAsync(9, id, new UpdateAnalysisJobRequest { EngineId = "eei_other" }));
        var job = await _db.AnalysisJobs.AsNoTracking().SingleAsync(j => j.Id == id);
        Assert.Equal(MoveComparisonService.HouseMaxDepth, job.TargetDepth);
        Assert.Equal(MoveComparisonService.CandidateLines, job.MultiPv);
        Assert.Equal("eei_haus", job.EngineId);
    }

    [Fact]
    public async Task EigeneEngine_AuftraegeDesVergleichs_TiefeBleibtAenderbar()
    {
        var u = await UserWithEngineAsync();
        await CreateAsync(u, START, "e2e4", "d2d4");
        var job = await _db.AnalysisJobs.AsNoTracking().OrderBy(j => j.Id).FirstAsync();
        Assert.Null(job.EngineOwnerUserId);
        var dto = await _jobs.UpdateAsync(u, job.Id, new UpdateAnalysisJobRequest { TargetDepth = 30 });
        Assert.Equal(30, dto!.TargetDepth);
    }

    // ── Ablauf ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pumpe_bestimmtDenBesten_undTestetDieAntwortenDesSchwaecherenGegenIhn()
    {
        var u = await UserWithEngineAsync();
        var dto = await CreateAsync(u, SCANDI, "e4d5", "b1c3");
        var takes = dto.Candidates.Single(c => c.Uci == "e4d5");
        var nc3 = dto.Candidates.Single(c => c.Uci == "b1c3");
        var takesFen = MoveComparisonService.PlayUci(SCANDI, "e4d5")!;
        var nc3Fen = MoveComparisonService.PlayUci(SCANDI, "b1c3")!;

        await FinishJobAsync(takesFen, Pvs((45, "d8d5 b1c3 d5a5"), (60, "g8f6 c2c4")));
        await FinishJobAsync(nc3Fen, Pvs((20, "d5e4 c3e4"), (35, "g8f6 e4e5")));
        await _svc.PumpOneAsync(dto.Id);

        var c = await _db.MoveComparisons.Include(x => x.Lines).SingleAsync();
        Assert.Equal(MoveComparisonStatus.Replies, c.Status);
        Assert.Equal("e4d5", c.BestUci);
        // Kandidaten-Aufträge sind eingesammelt und weg; es bleibt EIN Antwort-Auftrag (dxe4 geht nach exd5 nicht)
        var replies = c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Reply).OrderBy(l => l.Ordinal).ToList();
        Assert.Equal(new[] { "d5e4", "g8f6" }, replies.Select(l => l.ReplyUci));
        Assert.Equal(MoveComparisonLineState.Illegal, replies[0].State);
        Assert.Equal(MoveComparisonLineState.Pending, replies[1].State);
        var job = await _db.AnalysisJobs.SingleAsync();
        Assert.Equal(1, job.MultiPv);
        Assert.Equal(MoveComparisonService.PlayUci(takesFen, "g8f6"), job.Fen);

        await FinishJobAsync(job.Fen, Pvs((70, "c2c4 c7c6")));
        await _svc.PumpOneAsync(dto.Id);

        var done = (await _svc.GetAsync(u, dto.Id))!;
        Assert.Equal("done", done.Status);   // kein Modell auf eigener Hardware → ohne Begründung fertig
        Assert.Empty(_db.AnalysisJobs);
        Assert.Equal(new[] { "exd5", "Nc3" }, done.Candidates.Select(x => x.San));
        var best = done.Candidates[0];
        Assert.True(best.IsBest);
        Assert.Equal("+0.45", best.EvalText);
        Assert.Equal("Qxd5", best.Replies[0].San);
        var weak = done.Candidates[1];
        Assert.Equal("+0.20", weak.EvalText);
        Assert.Equal(new[] { "illegal", "done" }, weak.Tests.Select(t => t.State));
        Assert.Equal("dxe4", weak.Tests[0].ReplySan);
        Assert.Equal("+0.70", weak.Tests[1].EvalText);
        Assert.Equal(new[] { "c4", "c6" }, weak.Tests[1].Line);
        Assert.Null(weak.Explanation);
        _ = takes; _ = nc3;
    }

    [Fact]
    public async Task Begruendung_nutztDieFakten_undVerwirftFremdeZuege()
    {
        var u = await UserWithEngineAsync();
        _llm.Local = true;
        var dto = await CreateAsync(u, SCANDI, "e4d5", "b1c3");
        var takesFen = MoveComparisonService.PlayUci(SCANDI, "e4d5")!;
        await FinishJobAsync(takesFen, Pvs((45, "d8d5 b1c3 d5a5")));
        await FinishJobAsync(MoveComparisonService.PlayUci(SCANDI, "b1c3")!, Pvs((20, "d5e4 c3e4"), (35, "g8f6 e4e5")));
        await _svc.PumpOneAsync(dto.Id);
        var job = await _db.AnalysisJobs.SingleAsync();
        await FinishJobAsync(job.Fen, Pvs((70, "c2c4 c7c6")));

        _llm.Answers.Enqueue("""{"explanation":"Nach Nc3 spielt dein Gegner dxe4, danach Bb5 …"}""");   // Bb5 steht nirgends
        _llm.Answers.Enqueue("""{"explanation":"Nach Nc3 nimmt dein Gegner mit dxe4 den Bauern; nach exd5 gibt es das nicht mehr, und auf Nf6 hast du c4."}""");
        await _svc.PumpOneAsync(dto.Id);
        Assert.Equal(MoveComparisonStatus.Explaining, (await _db.MoveComparisons.SingleAsync()).Status);
        await _svc.ExplainAsync(dto.Id, CancellationToken.None);

        var done = (await _svc.GetAsync(u, dto.Id))!;
        Assert.Equal("done", done.Status);
        var weak = done.Candidates.Single(c => !c.IsBest);
        // Gespeichert in englischer SAN, ausgeliefert mit den Figurenbuchstaben der Sprache (de: Sf6)
        Assert.Equal("Nach Nc3 nimmt dein Gegner mit dxe4 den Bauern; nach exd5 gibt es das nicht mehr, und auf Nf6 hast du c4."
            .Replace("Nc3", "Sc3").Replace("Nf6", "Sf6"), weak.Explanation);
        Assert.Null(done.Candidates.Single(c => c.IsBest).Explanation);

        var prompt = _llm.Prompts[0];
        Assert.Contains("Better move: 2.exd5", prompt);
        Assert.Contains("Weaker move: 2.Nc3", prompt);
        Assert.Contains("2...dxe4: not possible after 2.exd5", prompt);
        Assert.Contains("The reader's best reply, with the engine line: 3.c4 c6", prompt);
        Assert.Contains("Reader: plays White", prompt);
    }

    [Fact]
    public async Task EinMattzug_brauchtKeinenAuftrag_undIstDerBeste()
    {
        var u = await UserWithEngineAsync();
        // 1.f3 e5 2.g4 — Schwarz am Zug, Qh4 setzt matt
        const string fool = "rnbqkbnr/pppp1ppp/8/4p3/6P1/5P2/PPPPP2P/RNBQKBNR b KQkq g3 0 2";
        var dto = await CreateAsync(u, fool, "d8h4", "g8f6");
        Assert.Single(_db.AnalysisJobs);   // nur Nf6
        Assert.Equal("done", dto.Candidates.Single(c => c.Uci == "d8h4").State);

        await FinishJobAsync(MoveComparisonService.PlayUci(fool, "g8f6")!, Pvs((-80, "b1c3 d7d5")));
        await _svc.PumpOneAsync(dto.Id);

        var c = await _db.MoveComparisons.Include(x => x.Lines).SingleAsync();
        Assert.Equal("d8h4", c.BestUci);
        // Nach dem Matt gibt es keine Antworten — alle Tests „geht nicht"
        Assert.All(c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Reply), l => Assert.Equal(MoveComparisonLineState.Illegal, l.State));
        Assert.Equal(MoveComparisonStatus.Done, c.Status);
        var best = (await _svc.GetAsync(u, dto.Id))!.Candidates[0];
        Assert.Equal("Qh4#", best.San);
        Assert.True(best.IsBest);
    }

    [Fact]
    public async Task Loeschen_nimmtOffeneAuftraegeMit_undNurEigene()
    {
        var u = await UserWithEngineAsync();
        var dto = await CreateAsync(u, START, "e2e4", "d2d4");
        Assert.False(await _svc.DeleteAsync(999, dto.Id));
        Assert.True(await _svc.DeleteAsync(u, dto.Id));
        Assert.Empty(_db.MoveComparisons);
        Assert.Empty(_db.AnalysisJobs);
    }

    [Fact]
    public async Task Deckel_offenerVergleiche()
    {
        var u = await UserWithEngineAsync();
        for (var i = 0; i < MoveComparisonService.MaxOpenPerUser; i++) await CreateAsync(u, START, "e2e4", "d2d4");
        var ex = await Assert.ThrowsAsync<MoveComparisonException>(() => CreateAsync(u, START, "e2e4", "d2d4"));
        Assert.Equal("too-many-open", ex.Reason);
        var status = await _svc.StatusAsync(u);
        Assert.True(status.EngineAvailable);
        Assert.Equal(MoveComparisonService.MaxOpenPerUser, status.OpenComparisons);
    }
}
