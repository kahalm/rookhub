using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers.Games;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>
/// Reparatur der Partieanalysen nach dem Provider-Versatz (<see cref="GameAnalysisRepair"/>): Fund-Regeln samt Ausschlüssen,
/// Trockenlauf ändert nichts, Wiederöffnen wie „Neu starten“, Seiten über den Cursor, nur echte Admins, und der
/// Positions-Cache kopiert die falschen Zahlen nicht zurück.
/// </summary>
public class GameAnalysisRepairTests : IDisposable
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";
    private const string MateFen = "rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3";
    private const string StalemateFen = "7k/5Q2/6K1/8/8/8/8/8 b - - 0 1";
    /// <summary>Genau ein legaler Zug: Ka1-b1 (a2 und b2 grenzen an den schwarzen König a3).</summary>
    private const string OneMoveFen = "8/8/8/8/8/k7/8/K7 w - - 0 1";

    private readonly AppDbContext _db;
    private readonly GameAnalysisRepair _repair;
    private int _userId;

    public GameAnalysisRepairTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _repair = new GameAnalysisRepair(_db, new AnalysisJobService(_db, null), NullLogger<GameAnalysisRepair>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static string Lines(int n) => "[" + string.Join(",", Enumerable.Range(0, n).Select(i =>
        $"{{\"uci\":\"{new[] { "e2e4", "d2d4", "g1f3", "c2c4", "b1c3" }[i]}\",\"cp\":{20 - i}}}")) + "]";

    // ── Fund-Regeln ─────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    //          json-Linien, fen,         multiPv, refineMpv, refined, countRule, erwartet
    [InlineData(0, Start,        5, null, false, true,  RepairFinding.Empty)]
    [InlineData(0, Start,        1, null, false, false, RepairFinding.Empty)]          // auch Lc0: [] ist nie ein Ergebnis
    [InlineData(0, MateFen,      5, null, false, true,  RepairFinding.None)]           // Matt: kein legaler Zug
    [InlineData(0, StalemateFen, 5, null, false, true,  RepairFinding.None)]           // Patt
    [InlineData(2, Start,        5, null, false, true,  RepairFinding.MissingLines)]   // fremde Linien weggefiltert
    [InlineData(5, Start,        5, null, false, true,  RepairFinding.None)]
    [InlineData(1, Start,        1, null, false, true,  RepairFinding.None)]           // schnelle Analyse mit einer Linie
    [InlineData(1, OneMoveFen,   5, null, false, true,  RepairFinding.None)]           // nur ein legaler Zug
    [InlineData(2, Start,        5, null, false, false, RepairFinding.None)]           // Lc0/Knoten: Zähl-Regel gilt nicht
    [InlineData(1, Start,        5, 1,    true,  true,  RepairFinding.None)]           // Vertiefung mit einer Linie
    [InlineData(1, Start,        1, 5,    true,  true,  RepairFinding.None)]           // Vertiefung blieb liegen: erster Durchgang
    [InlineData(1, Start,        5, 5,    true,  true,  RepairFinding.MissingLines)]
    public void Classify(int lines, string fen, int multiPv, int? refineMultiPv, bool refined, bool countRule, RepairFinding expected)
        => Assert.Equal(expected, GameAnalysisRepair.Classify(Lines(lines), fen, multiPv, refineMultiPv, refined, countRule).Finding);

    [Fact]
    public void Classify_unlesbar_oderKaputteFen_keinFund()
    {
        Assert.Equal(RepairFinding.None, GameAnalysisRepair.Classify("kein json", Start, 5, null, false, true).Finding);
        Assert.Equal(RepairFinding.None, GameAnalysisRepair.Classify("[]", "kaputt", 5, null, false, true).Finding);
        Assert.Equal(RepairFinding.None, GameAnalysisRepair.Classify(null, Start, 5, null, false, true).Finding);
    }

    // ── Bestand aufbauen ────────────────────────────────────────────────────────────────────────────────────────

    private async Task<GameAnalysis> AnalysisAsync(int multiPv = 5, GameAnalysisStatus status = GameAnalysisStatus.Done,
        string? engineId = null, int? refineDepth = null, params (string Fen, string? Json, int? JobId)[] positions)
    {
        if (_userId == 0)
        {
            var u = new AppUser { Username = "liga", PasswordHash = "x" };
            _db.AppUsers.Add(u);
            await _db.SaveChangesAsync();
            _userId = u.Id;
        }
        var a = new GameAnalysis
        {
            UserId = _userId, Pgn = "", StartFen = Start, Status = status, PlyCount = positions.Length, TargetDepth = 20,
            MultiPv = multiPv, EngineId = engineId, Origin = GameAnalysisOrigin.League, AccuracyWhite = 91, AccuracyBlack = 88,
            MoveClasses = "x", MoveClassesVersion = 3, FinishedAt = DateTime.UtcNow, RefineDepth = refineDepth,
            RefineMultiPv = refineDepth is null ? null : 5, RefinedAt = refineDepth is null ? null : DateTime.UtcNow,
        };
        var ply = 0;
        foreach (var (fen, json, jobId) in positions)
            a.Positions.Add(new GameAnalysisPosition
            {
                Ply = ply++, Fen = fen, GameMoveUci = "e2e4", GameMoveSan = "e4", CandidatesJson = json, Depth = json is null ? 0 : 20,
                EvalText = json is null ? null : "+0.20", AnalyzedAt = DateTime.UtcNow, AnalysisJobId = jobId, FailedAttempts = 2,
                Refined = refineDepth is not null, NodeStepsJson = "[]",
            });
        _db.GameAnalyses.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    // ── Trockenlauf / Anwenden ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Trockenlauf_zaehlt_aendertNichts()
    {
        var a = await AnalysisAsync(5, positions: [(Start, "[]", null), (AfterE4, Lines(2), null), (Start, Lines(5), null), (MateFen, "[]", null)]);
        var before = await _db.GameAnalysisPositions.AsNoTracking().Select(p => p.CandidatesJson).ToListAsync();

        var r = await _repair.RunAsync(new GameAnalysisRepairRequest(), adminUserId: 1);

        Assert.False(r.Applied);
        Assert.Equal((4, 1, 1, 1), (r.Scanned, r.FoundEmpty, r.FoundMissingLines, r.SkippedTerminal));
        Assert.Equal(0, r.PositionsReopened);
        Assert.Null(r.NextCursor);
        Assert.Equal(2, r.Samples.Count);
        Assert.Equal(before, await _db.GameAnalysisPositions.AsNoTracking().Select(p => p.CandidatesJson).ToListAsync());
        Assert.Equal(GameAnalysisStatus.Done, (await _db.GameAnalyses.AsNoTracking().SingleAsync(x => x.Id == a.Id)).Status);
    }

    [Fact]
    public async Task Anwenden_oeffnetNurDieFunde_wieNeuStarten_undRaeumtAbgeleitetesAb()
    {
        var a = await AnalysisAsync(5, refineDepth: 30,
            positions: [(Start, Lines(5), null), (AfterE4, "[]", null), (Start, Lines(5), null), (AfterE4, Lines(3), null)]);
        _db.GameMoveExplanations.AddRange(
            new GameMoveExplanation { GameAnalysisId = a.Id, Ply = 0, Language = "de", Class = "mistake", Text = "a" },   // Zug vor dem Fund
            new GameMoveExplanation { GameAnalysisId = a.Id, Ply = 1, Language = "de", Class = "mistake", Text = "b" },   // Fund
            new GameMoveExplanation { GameAnalysisId = a.Id, Ply = 2, Language = "de", Class = "mistake", Text = "c" },   // Zug vor Fund 3
            new GameMoveExplanation { GameAnalysisId = a.Id, Ply = 3, Language = "de", Class = "mistake", Text = "d" });
        await _db.SaveChangesAsync();

        var r = await _repair.RunAsync(new GameAnalysisRepairRequest(Apply: true), adminUserId: 1);

        Assert.Equal((2, 1), (r.PositionsReopened, r.AnalysesReopened));
        Assert.Equal(4, r.ExplanationsDeleted);
        _db.ChangeTracker.Clear();
        var ps = await _db.GameAnalysisPositions.Where(p => p.GameAnalysisId == a.Id).OrderBy(p => p.Ply).ToListAsync();
        foreach (var p in new[] { ps[1], ps[3] })
        {
            Assert.Null(p.CandidatesJson);
            Assert.Null(p.EvalText);
            Assert.Null(p.AnalyzedAt);
            Assert.Null(p.NodeStepsJson);
            Assert.Equal((0, 0, false), (p.Depth, p.FailedAttempts, p.Refined));
        }
        Assert.Equal(Lines(5), ps[0].CandidatesJson);                   // gute Stellungen bleiben
        Assert.True(ps[0].Refined);
        var reloaded = await _db.GameAnalyses.SingleAsync(x => x.Id == a.Id);
        Assert.Equal(GameAnalysisStatus.Pending, reloaded.Status);
        Assert.Null(reloaded.FinishedAt);
        Assert.Null(reloaded.AccuracyWhite);
        Assert.Null(reloaded.MoveClasses);
        Assert.Null(reloaded.RefinedAt);                                 // die geöffneten Stellungen werden wieder vertieft
    }

    [Fact]
    public async Task Ausschluesse_offenerAuftrag_gescheiterteAnalyse_Lc0Zaehlregel()
    {
        await AnalysisAsync(5, positions: [(Start, "[]", 4711)]);                                    // Auftrag läuft
        await AnalysisAsync(5, status: GameAnalysisStatus.Failed, positions: [(Start, "[]", null)]);
        await AnalysisAsync(5, engineId: "rhe_lc0", positions: [(Start, Lines(2), null)]);

        var r = await _repair.RunAsync(new GameAnalysisRepairRequest(Apply: true), adminUserId: 1);

        Assert.Equal((0, 0, 0), (r.FoundEmpty, r.FoundMissingLines, r.PositionsReopened));
    }

    // ── Seiten ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Seiten_maxAnalysen_setztCursorVorDieNaechsteAnalyse_undDerNaechsteAufrufMachtWeiter()
    {
        var first = await AnalysisAsync(5, positions: [(Start, "[]", null), (AfterE4, "[]", null)]);
        var second = await AnalysisAsync(5, positions: [(Start, Lines(5), null), (AfterE4, "[]", null)]);
        var secondFind = await _db.GameAnalysisPositions.Where(p => p.GameAnalysisId == second.Id && p.Ply == 1).Select(p => p.Id).SingleAsync();

        var r1 = await _repair.RunAsync(new GameAnalysisRepairRequest(Apply: true, MaxAnalyses: 1), adminUserId: 1);
        Assert.Equal((2, 1), (r1.PositionsReopened, r1.AnalysesReopened));
        Assert.Equal(secondFind - 1, r1.NextCursor);

        var r2 = await _repair.RunAsync(new GameAnalysisRepairRequest(Apply: true, AfterPositionId: r1.NextCursor!.Value, MaxAnalyses: 1),
            adminUserId: 1);
        Assert.Equal((1, 1), (r2.PositionsReopened, r2.AnalysesReopened));
        Assert.Null(r2.NextCursor);
        Assert.All(r1.Samples, s => Assert.Equal(first.Id, s.GameAnalysisId));
    }

    [Fact]
    public async Task Seiten_maxScan_liestNieMehrAlsVerlangt()
    {
        await AnalysisAsync(5, positions: [(Start, Lines(5), null), (Start, Lines(5), null), (Start, "[]", null)]);

        var r = await _repair.RunAsync(new GameAnalysisRepairRequest(MaxScan: 2), adminUserId: 1);
        Assert.Equal(2, r.Scanned);
        Assert.Equal(0, r.FoundEmpty);
        Assert.NotNull(r.NextCursor);

        var rest = await _repair.RunAsync(new GameAnalysisRepairRequest(AfterPositionId: r.NextCursor!.Value, MaxScan: 2), adminUserId: 1);
        Assert.Equal((1, 1), (rest.Scanned, rest.FoundEmpty));
        Assert.Null(rest.NextCursor);
    }

    // ── Admin-Zwang ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Endpunkt_nurAdmins_nieAusEinerImpersonation()
    {
        var t = typeof(AdminGameAnalysisRepairController);
        Assert.Contains(Permissions.AdminOnly, t.GetCustomAttribute<HasPermissionAttribute>()!.Policy!);
        foreach (var action in new[] { nameof(AdminGameAnalysisRepairController.DryRun), nameof(AdminGameAnalysisRepairController.Apply) })
            Assert.True(t.GetMethod(action)!.IsDefined(typeof(DenyWhileImpersonatingAttribute), true));
    }

    // ── Positions-Cache ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PositionsCache_kopiertKeineQuelleMitZuWenigenLinien()
    {
        var polluted = await AnalysisAsync(5, positions: [(AfterE4, Lines(2), null)]);   // 2 von 5 Linien: fremd gefiltert
        var target = await AnalysisAsync(1, status: GameAnalysisStatus.Pending, positions: [(Start, null, null)]);
        Assert.Null(await GameAnalysisPositionCache.FindAsync(_db, target, AfterE4, 20, 1, default));

        var good = await AnalysisAsync(5, positions: [(AfterE4, Lines(5), null)]);
        Assert.NotNull(await GameAnalysisPositionCache.FindAsync(_db, target, AfterE4, 20, 1, default));
        Assert.NotEqual(polluted.Id, good.Id);
    }
}
