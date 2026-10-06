using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Zwischenstände einer Knotenanalyse (10k, 20k, … Knoten): Stufenbildung (<see cref="NodeStepRecorder"/>), Auswertung
/// (<see cref="Convergence"/>) und der Weg vom Auftrag zur Stellung und zum Endpunkt. Die Zahlen sind LITERAL gerechnet.
/// </summary>
public class NodeStepsTests : IDisposable
{
    private const string Game = """
[Event "Testpartie"]
[White "Anderssen"]
[Black "Kieseritzky"]
[Result "1-0"]

1. e4 e5 2. f4 exf4 1-0
""";

    private const string Lc0 = "eei_lc0";
    private const int User = 5;
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private static string Thresholds(IEnumerable<NodeStep> steps) => string.Join(",", steps.Select(s => s.Threshold));

    // ── Stufenbildung ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recorder_keepsTheLastLineBelowEachThreshold()
    {
        var r = new NodeStepRecorder(10_000);
        r.Observe(4_000, "e7e5", 30, null);
        r.Observe(12_000, "c7c5", 20, null);     // 10k: letzte Zeile ≤ 10k ist die bei 4k
        r.Observe(19_000, "c7c5", 18, null);
        r.Observe(26_000, "c7c5", 10, null);     // 20k: letzte Zeile ≤ 20k ist die bei 19k
        var steps = r.Snapshot();

        Assert.Equal("10000,20000", Thresholds(steps));
        Assert.Equal(("e7e5", 4_000L, 30), (steps[0].Uci, steps[0].Nodes, steps[0].Cp));
        Assert.Equal(("c7c5", 19_000L, 18), (steps[1].Uci, steps[1].Nodes, steps[1].Cp));
    }

    [Fact]
    public void Recorder_doesNotInventAStepWhenTheEngineWasSilentForMoreThanOneStep()
    {
        var r = new NodeStepRecorder(10_000);
        r.Observe(12_000, "e7e5", 30, null);
        r.Observe(80_000, "e7e5", 25, null);     // Schweigen von 12k bis 80k: 30k..70k hätten nur eine uralte Zeile
        var steps = r.Snapshot();

        Assert.Equal("20000", Thresholds(steps));     // nur 20k liegt innerhalb einer Schrittweite hinter 12k
        Assert.DoesNotContain(steps, s => s.Threshold is 30_000 or 40_000 or 70_000);
    }

    [Fact]
    public void Recorder_finishStoresTheGoalWithTheNodesActuallyReached()
    {
        var r = new NodeStepRecorder(10_000);
        r.Observe(48_000, "e7e5", 12, null);
        r.Observe(99_400, "e7e5", 14, null);     // Lc0 hört knapp vor dem Ziel auf
        r.Finish(100_000);
        var steps = r.Snapshot();

        Assert.Equal(("e7e5", 99_400L), (steps[^1].Uci, steps[^1].Nodes));
        Assert.Equal(100_000, steps[^1].Threshold);
        Assert.Equal(14, steps[^1].Cp);
    }

    [Fact]
    public void Recorder_finishWithoutReachingTheGoalAddsNothing()
    {
        var r = new NodeStepRecorder(10_000);
        r.Observe(40_000, "e7e5", 12, null);
        r.Finish(100_000);
        Assert.Empty(r.Snapshot());
    }

    [Fact]
    public void Recorder_stepZeroIsOff()
    {
        var r = new NodeStepRecorder(0);
        Assert.False(r.Enabled);
        r.Observe(5_000, "e7e5", 1, null);
        r.Observe(50_000, "e7e5", 2, null);
        r.Finish(50_000);
        Assert.Empty(r.Snapshot());
        Assert.False(r.Dirty);
    }

    [Fact]
    public void Recorder_resumingDoesNotCountTwice_andOnlyReplacesWithMoreNodes()
    {
        var first = new NodeStepRecorder(10_000);
        first.Observe(4_000, "e7e5", 30, null);
        first.Observe(25_000, "c7c5", 10, null);
        var saved = first.Snapshot();                       // nur 10k (n=4k); 20k hätte eine Zeile von vor 21k Knoten

        // Fortsetzung: die Suche beginnt bei 0 Knoten, liefert für 10k weniger Knoten als der gespeicherte Stand → bleibt
        var resumed = new NodeStepRecorder(10_000, saved);
        resumed.Observe(1_000, "a7a6", 99, null);
        resumed.Observe(12_000, "a7a6", 98, null);
        var after = resumed.Snapshot();
        Assert.Equal(saved.Select(s => (s.Threshold, s.Nodes, s.Uci)), after.Select(s => (s.Threshold, s.Nodes, s.Uci)).Take(saved.Count));

        // … und mit MEHR Knoten an derselben Schwelle wird ersetzt
        var better = new NodeStepRecorder(10_000, saved);
        better.Observe(9_500, "d7d5", 7, null);
        better.Observe(10_500, "d7d5", 8, null);
        var replaced = better.Snapshot().Single(s => s.Threshold == 10_000);
        Assert.Equal(("d7d5", 9_500L), (replaced.Uci, replaced.Nodes));
        Assert.Equal(1, better.Snapshot().Count(s => s.Threshold == 10_000));      // nie doppelt
    }

    [Fact]
    public void Recorder_aNewSearchWithFewerNodesDoesNotBackfill()
    {
        var r = new NodeStepRecorder(10_000);
        r.Observe(30_000, "e7e5", 1, null);
        r.Observe(2_000, "c7c5", 2, null);        // neue Suche
        r.Observe(15_000, "c7c5", 3, null);
        var steps = r.Snapshot();
        Assert.Equal("10000", Thresholds(steps));
        Assert.Equal("c7c5", steps[0].Uci);
    }

    [Fact]
    public void NodeSteps_roundTripKeepsMateAndOrdersByThreshold()
    {
        var json = NodeSteps.ToJson([new NodeStep(20_000, 19_500, "d1h5", null, 3), new NodeStep(10_000, 9_000, "e2e4", 31, null)]);
        var back = NodeSteps.Parse(json);
        Assert.Equal(new[] { 10_000L, 20_000L }, back.Select(s => s.Threshold));
        Assert.Equal(3, back[1].Mate);
        Assert.Null(back[1].Cp);
        Assert.Empty(NodeSteps.Parse("kein json"));
        Assert.Empty(NodeSteps.Parse(null));
    }

    [Fact]
    public void ObserveStep_readsNodesAndTheBestMove_fromTheSideToMovesView()
    {
        // Schwarz am Zug, der Broker liefert aus Weiß-Sicht -25 → für Schwarz +25
        const string fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";
        const string line = "{\"depth\":7,\"nodes\":12000,\"time\":9,\"pvs\":[{\"moves\":[\"e7e5\",\"g1f3\"],\"cp\":-25}]}";
        var r = new NodeStepRecorder(10_000);
        AnalysisJobWorker.ObserveStep(r, line, fen);
        AnalysisJobWorker.ObserveStep(r, line.Replace("12000", "21000"), fen);
        var step = Assert.Single(r.Snapshot(), s => s.Threshold == 20_000);
        Assert.Equal(("e7e5", 12_000L, 25), (step.Uci, step.Nodes, step.Cp));
    }

    // ── Auswertung ─────────────────────────────────────────────────────────────────────────────────────

    private static List<NodeStep> Pos(params (long t, string uci, int cp)[] rows) =>
        rows.Select(r => new NodeStep(r.t, r.t, r.uci, r.cp, null)).ToList();

    [Fact]
    public void Evaluate_countsSameMove_moveChanges_andMedianDifference()
    {
        var report = Convergence.Evaluate(new[]
        {
            Pos((10_000, "e2e4", 40), (20_000, "d2d4", 10), (30_000, "d2d4", 0)),
            Pos((10_000, "e2e4", 50), (20_000, "e2e4", 50), (30_000, "e2e4", 50)),
            Pos((10_000, "g1f3", 20), (20_000, "d2d4", 20), (30_000, "d2d4", 20)),
        });

        Assert.Equal(3, report.Positions);
        Assert.Equal(new[] { 10_000L, 20_000L, 30_000L }, report.Rows.Select(r => r.Threshold));

        var r10 = report.Rows[0];
        Assert.Equal(3, r10.Positions);
        Assert.Equal(1.0 / 3, r10.SameMoveShare, 6);            // nur Stellung 2 hat schon den Zielzug
        Assert.Equal(0, r10.MoveChanges);                       // keine Vorstufe
        Assert.Equal(40, r10.P90Cp);                            // Differenzen 40, 0, 0 → p90 (nächster Rang) = 40
        Assert.Equal(0, r10.MedianCp);                          // … und der Median 0

        var r20 = report.Rows[1];
        Assert.Equal(2, r20.MoveChanges);                       // Stellung 1 (e4→d4) und Stellung 3 (Nf3→d4)
        Assert.Equal(1.0, r20.SameMoveShare);                   // bei 20k stehen schon alle auf dem Zielzug

        var r30 = report.Rows[2];
        Assert.Equal(1.0, r30.SameMoveShare);                   // am Ziel ist alles gleich
        Assert.Equal(0, r30.MedianCp);
        Assert.Equal(0, r30.MoveChanges);
    }

    [Fact]
    public void Evaluate_capsMateAtPlusMinusThousand_andCountsMateMismatchSeparately()
    {
        var withMate = new List<NodeStep>
        {
            new(10_000, 9_000, "d1h5", 800, null),
            new(20_000, 19_000, "d1h5", null, 3),
        };
        var report = Convergence.Evaluate(new[] { withMate });
        var row = report.Rows[0];
        Assert.Equal(200, row.MedianCp);                        // Matt = 1000, gegen 800 → 200 (kein Riesenabstand)
        Assert.Equal(1, row.MateMismatch);                      // Zielstufe Matt, diese Stufe nicht
        Assert.Equal(0, report.Rows[1].MateMismatch);
    }

    [Fact]
    public void Evaluate_positionsWithOnlyOneStepAreLeftOut_andNothingGivesAnEmptyReport()
    {
        var report = Convergence.Evaluate(new[] { Pos((10_000, "e2e4", 5)), Pos((10_000, "e2e4", 5), (20_000, "e2e4", 6)) });
        Assert.Equal(1, report.Positions);
        Assert.Equal(2, report.Rows.Count);
        Assert.Empty(Convergence.Evaluate(Array.Empty<IReadOnlyList<NodeStep>>()).Rows);
    }

    [Fact]
    public void Evaluate_winPercentDifferenceIsInPercentagePoints()
    {
        // 0 cp ↔ 50 %, 300 cp ↔ rund 75,1 %: Lichess-Kurve
        Assert.Equal(50.0, Convergence.WinPercent(0), 6);
        Assert.InRange(Convergence.WinPercent(300), 75.0, 75.3);
        var report = Convergence.Evaluate(new[] { Pos((10_000, "e2e4", 0), (20_000, "e2e4", 300)) });
        Assert.InRange(report.Rows[0].MedianWinPct, 24.9, 25.3);
    }

    // ── Auftrag → Stellung → Endpunkt ──────────────────────────────────────────────────────────────────

    private async Task SeedAsync(int id = User)
    {
        _db.AppUsers.Add(new AppUser { Id = id, Username = $"u{id}", PasswordHash = "x", IsAdmin = true });
        _db.LichessEngineCredentials.Add(new LichessEngineCredential { UserId = id, EncryptedToken = "enc", BackgroundEngineIds = Lc0 });
        await _db.SaveChangesAsync();
    }

    private GameAnalysisService Service()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!" }).Build();
        var jobs = new AnalysisJobService(_db, null, null, cfg);
        return new GameAnalysisService(_db, jobs, new CommentSetService(_db, NullLogger<CommentSetService>.Instance),
            NullLogger<GameAnalysisService>.Instance);
    }

    [Fact]
    public async Task FinishedJob_hands_itsSteps_toThePosition_andTheEndpointEvaluatesThem()
    {
        await SeedAsync();
        var svc = Service();
        var dto = await svc.CreateAsync(User, new CreateGameAnalysisRequest { Pgn = Game, EngineId = Lc0, TargetNodes = 30_000 });
        var positions = await _db.GameAnalysisPositions.Where(p => p.GameAnalysisId == dto.Id).OrderBy(p => p.Ply).ToListAsync();
        Assert.NotEmpty(positions);

        // Jeden Auftrag fertig melden: Ergebnis = e2e4 (nur die erste Stellung ist damit legal-sicher, die anderen bekommen ihren eigenen Zug)
        foreach (var pos in positions)
        {
            var job = await _db.AnalysisJobs.SingleAsync(j => j.Id == pos.AnalysisJobId);
            var uci = TestMoves.MoveOf(pos);     // der Partiezug (Endstellung: irgendein legaler)
            job.Status = AnalysisJobStatus.Done;
            job.ResultJson = JsonSerializer.Serialize(new { depth = 9, nodes = 30_000, pvs = new[] { new { moves = new[] { uci }, cp = 20 } } });
            job.ReachedDepth = 9;
            job.NodeStepsJson = NodeSteps.ToJson([
                new NodeStep(10_000, 9_500, uci, 5, null), new NodeStep(20_000, 19_000, uci, 15, null), new NodeStep(30_000, 30_000, uci, 20, null)]);
        }
        await _db.SaveChangesAsync();

        await svc.PumpOneAsync(dto.Id);

        _db.ChangeTracker.Clear();
        var done = await _db.GameAnalysisPositions.Where(p => p.GameAnalysisId == dto.Id && p.CandidatesJson != null).ToListAsync();
        Assert.NotEmpty(done);
        Assert.All(done, p => Assert.Equal(3, NodeSteps.Parse(p.NodeStepsJson).Count));      // Stufen sind mitgenommen, bevor der Auftrag geht

        var conv = await svc.ConvergenceAsync(User, dto.Id);
        Assert.NotNull(conv);
        Assert.Equal(30_000, conv!.TargetNodes);
        Assert.Equal(done.Count, conv.Positions);
        Assert.Equal(new[] { 10_000L, 20_000L, 30_000L }, conv.Rows.Select(r => r.Threshold));
        Assert.All(conv.Rows, r => Assert.Equal(100.0, r.SameMovePercent));
        Assert.Equal(15, conv.Rows[0].MedianCp);                                             // |5 - 20|
        Assert.Equal(5, conv.Rows[1].MedianCp);
        Assert.Equal(0, conv.Rows[2].MedianCp);
    }

    [Fact]
    public async Task Convergence_belongsToTheOwnerOnly_andIsEmptyWithoutSteps()
    {
        await SeedAsync();
        await SeedAsync(id: 6);
        var svc = Service();
        var dto = await svc.CreateAsync(User, new CreateGameAnalysisRequest { Pgn = Game, EngineId = Lc0, TargetNodes = 30_000 });

        Assert.Null(await svc.ConvergenceAsync(6, dto.Id));          // fremde Analyse → wie nicht vorhanden
        Assert.Null(await svc.ConvergenceAsync(User, 99_999));
        var empty = await svc.ConvergenceAsync(User, dto.Id);
        Assert.NotNull(empty);
        Assert.Equal(0, empty!.Positions);
        Assert.Empty(empty.Rows);
    }
}
