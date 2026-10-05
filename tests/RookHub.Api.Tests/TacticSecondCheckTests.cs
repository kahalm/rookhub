using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Tactics;
using Cand = RookHub.Api.Services.Tactics.TacticHarvest.Cand;

namespace RookHub.Api.Tests;

/// <summary>Taktik-Ernte, Phase 2: Zweitprüfung durch eine zweite Engine (lc0) — Regeln, Stufen, Pumpe, Veröffentlichung.</summary>
public class TacticSecondCheckTests : IDisposable
{
    private const int Owner = 990101;
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private static Cand C(string uci, int? cp = null, int? mate = null) => new(uci, cp, mate, new[] { uci });

    private const string MatePrev = "6k1/5ppp/8/8/8/8/7P/R5K1 b - - 0 1";
    private const string MateFen = "7k/5ppp/8/8/8/8/7P/R5K1 w - - 1 2";
    private const string LadderPrev = "6k1/8/8/8/8/8/1R6/R5K1 b - - 0 1";
    private const string LadderFen = "7k/8/8/8/8/8/1R6/R5K1 w - - 0 1";

    private static TacticCandidate Cand1(string kind = "mate", string moves = "a1a8", string prev = MatePrev, string fen = MateFen) => new()
    {
        Id = 1, GameAnalysisId = 1, Origin = GameAnalysisOrigin.Club, PrevFen = prev, BlunderUci = "g8h8", Fen = fen,
        Kind = kind, Moves = moves, Status = TacticCandidateStatus.Done,
    };

    // ── Regel ──

    [Fact]
    public void Agree_SameBestMoveAndUnique_ElseNot()
    {
        Assert.True(TacticHarvest.Agree(LadderFen, new[] { C("b2b7", cp: 700), C("a1a2", cp: 40) }, "b2b7", false));
        Assert.False(TacticHarvest.Agree(LadderFen, new[] { C("a1a2", cp: 700), C("b2b7", cp: 40) }, "b2b7", false));   // anderer bester Zug
        Assert.False(TacticHarvest.Agree(LadderFen, new[] { C("b2b7", cp: 700), C("a1a2", cp: 650) }, "b2b7", false));  // nicht eindeutig
        Assert.False(TacticHarvest.Agree(LadderFen, Array.Empty<Cand>(), "b2b7", false));
        Assert.True(TacticHarvest.Agree(MateFen, new[] { C("a1a8", mate: 1), C("g1f2", cp: 50) }, "a1a8", true));
        Assert.False(TacticHarvest.Agree(MateFen, new[] { C("a1a8", mate: 1), C("a1a7", mate: 3) }, "a1a8", true));      // zweites Matt
    }

    [Fact]
    public void SameMove_CastlingAsKingTakesRook_IsTheSameMove()
    {
        const string fen = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";
        Assert.True(TacticHarvest.SameMove(fen, "e1h1", "e1g1"));
        Assert.False(TacticHarvest.SameMove(fen, "e1f1", "e1g1"));
    }

    [Fact]
    public void Dump_RoundTrips_WithoutTheVariations()
    {
        var back = TacticHarvest.LoadDump(TacticHarvest.Dump(new[] { new Cand("a1a8", null, 1, new[] { "a1a8", "h8g8" }), C("g1f2", cp: 50) }));
        Assert.Equal(("a1a8", (int?)null, (int?)1), (back[0].Uci, back[0].Cp, back[0].Mate));
        Assert.Equal(50, back[1].Cp);
        Assert.Empty(TacticHarvest.LoadDump("kaputt"));
    }

    // ── Stufen ──

    [Fact]
    public void SecondStep_AllStagesAgree_ConfirmsTheCandidate()
    {
        var c = Cand1();
        TacticHarvestService.SecondStep(c, new[] { C("a1a8", mate: 1), C("g1f2", cp: 50) });     // Stufe 0: Aufgabenstellung
        Assert.Equal((1, (bool?)null, "a1a8", "#1"), (c.SecondStage, c.SecondAgrees, c.SecondBest, c.SecondEval));
        TacticHarvestService.SecondStep(c, new[] { C("g8f8", cp: 20) });                         // Stufe 1: vor dem Fehler war es ruhig
        Assert.Equal((true, TacticCandidateStatus.Done), (c.SecondAgrees, c.Status));
    }

    [Fact]
    public void SecondStep_DifferentBestMove_Disputes()
    {
        var c = Cand1();
        TacticHarvestService.SecondStep(c, new[] { C("g1f2", cp: 300), C("a1a8", cp: 20) });
        Assert.Equal((TacticCandidateStatus.Disputed, (bool?)false, "lc0Move"), (c.Status, c.SecondAgrees, c.RejectReason));
    }

    [Fact]
    public void SecondStep_OpponentMoveWasNoMistakeForTheSecondEngine_Disputes()
    {
        var c = Cand1();
        TacticHarvestService.SecondStep(c, new[] { C("a1a8", mate: 1), C("g1f2", cp: 50) });
        TacticHarvestService.SecondStep(c, new[] { C("g8f8", cp: -900) });                       // stand schon verloren: kein Fehler
        Assert.Equal((TacticCandidateStatus.Disputed, "lc0NoBlunder"), (c.Status, c.RejectReason));
    }

    [Fact]
    public void SecondFen_FollowsTheSolutionWithReplies_NullWhenNotPlayable()
    {
        var c = Cand1("material", "b2b7 h8g8 a1a8", LadderPrev, LadderFen);
        Assert.Equal(LadderFen, TacticHarvestService.SecondFen(c, 0));
        Assert.Equal(LadderPrev, TacticHarvestService.SecondFen(c, 1));
        Assert.Equal("6k1/1R6/8/8/8/8/8/R5K1 w - - 2 2", TacticHarvestService.SecondFen(c, 2));
        c.Moves = "b2b7 h8h1";   // Antwort nicht spielbar
        Assert.Null(TacticHarvestService.SecondFen(c, 3));
    }

    [Fact]
    public void SecondStep_LaterSolverMoveDisagrees_CutsMaterialSolution_DisputesMateSolution()
    {
        var material = Cand1("material", "b2b7 h8g8 a1a8", LadderPrev, LadderFen);
        material.SecondStage = 2;
        TacticHarvestService.SecondStep(material, new[] { C("a1a2", cp: 700), C("a1a8", cp: 20) });
        Assert.Equal(("b2b7", (bool?)true, TacticCandidateStatus.Done), (material.Moves, material.SecondAgrees, material.Status));

        var mate = Cand1("mate", "b2b7 h8g8 a1a8", LadderPrev, LadderFen);
        mate.SecondStage = 2;
        TacticHarvestService.SecondStep(mate, new[] { C("a1a2", cp: 700), C("a1a8", cp: 20) });
        Assert.Equal((TacticCandidateStatus.Disputed, "lc0Line", "b2b7 h8g8 a1a8"), (mate.Status, mate.RejectReason, mate.Moves));
    }

    [Fact]
    public void SecondStep_LaterSolverMoveAgrees_ConfirmsAtTheLastStage()
    {
        var c = Cand1("material", "b2b7 h8g8 a1a8", LadderPrev, LadderFen);
        c.SecondStage = 2;
        TacticHarvestService.SecondStep(c, new[] { C("a1a8", cp: 700), C("a1a2", cp: 20) });
        Assert.Equal(("b2b7 h8g8 a1a8", (bool?)true), (c.Moves, c.SecondAgrees));
    }

    // ── Pumpe und Kurs ──

    private TacticHarvestService Svc(string? secondEngine, string[]? engines = null)
    {
        var cfg = new Dictionary<string, string?> { ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!" };
        if (secondEngine is not null) cfg["TacticHarvest:SecondEngineId"] = secondEngine;
        var config = new ConfigurationBuilder().AddInMemoryCollection(cfg).Build();
        if (engines is not null && !_db.AppUsers.Any())
        {
            _db.AppUsers.Add(new AppUser { Id = Owner, Username = "kahalm", PasswordHash = "x", IsAdmin = true });
            var cred = new LichessEngineCredential { UserId = Owner, EncryptedToken = "enc" };
            cred.SetBackgroundEngines(engines);
            _db.LichessEngineCredentials.Add(cred);
            _db.SaveChanges();
        }
        return new TacticHarvestService(_db, new AnalysisJobService(_db, config: config), new QuietHours("", "UTC"), config,
            NullLogger<TacticHarvestService>.Instance);
    }

    private async Task SeedCandidateAsync()
    {
        _db.GameAnalyses.Add(new GameAnalysis { Id = 1, UserId = 1, Origin = GameAnalysisOrigin.Club, Pgn = "", StartFen = "x", Status = GameAnalysisStatus.Done });
        var c = Cand1();
        c.Id = 0;
        _db.TacticCandidates.Add(c);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Pump_SecondOff_CreatesNoJob_AndPublishesAsBefore()
    {
        await SeedCandidateAsync();
        await Svc(null, new[] { "stockfish1", "rhe_lc0" }).PumpAsync(Owner, default);
        Assert.Empty(_db.AnalysisJobs);
        Assert.Equal(1, await Svc(null).PublishAsync(default));
    }

    [Fact]
    public async Task Pump_SecondOn_CreatesNodeJobOnTheNamedEngine_AndPublishesOnlyWhenConfirmed()
    {
        await SeedCandidateAsync();
        var svc = Svc("rhe_lc0", new[] { "stockfish1", "rhe_lc0" });
        Assert.Equal(0, await svc.PublishAsync(default));             // unentschieden → wartet
        await svc.PumpAsync(Owner, default);
        var job = await _db.AnalysisJobs.SingleAsync();
        Assert.Equal(("rhe_lc0", 50_000L, 3, MateFen), (job.EngineId, job.TargetNodes, job.MultiPv, job.Fen));
        Assert.Equal(job.Id, (await _db.TacticCandidates.SingleAsync()).SecondJobId);

        var c = await _db.TacticCandidates.SingleAsync();
        c.SecondAgrees = true;
        await _db.SaveChangesAsync();
        Assert.Equal(1, await svc.PublishAsync(default));
    }

    [Fact]
    public async Task Pump_DisputedCandidate_IsNeverPublished()
    {
        await SeedCandidateAsync();
        var c = await _db.TacticCandidates.SingleAsync();
        c.Status = TacticCandidateStatus.Disputed;
        await _db.SaveChangesAsync();
        Assert.Equal(0, await Svc("rhe_lc0", new[] { "rhe_lc0" }).PublishAsync(default));
        Assert.Equal(0, await Svc(null).PublishAsync(default));
    }

    [Fact]
    public async Task Pump_SecondEngineNotInOwnersList_WaitsWithoutThrowing()
    {
        await SeedCandidateAsync();
        await Svc("rhe_lc0", new[] { "stockfish1" }).PumpAsync(Owner, default);
        Assert.Empty(_db.AnalysisJobs);
        Assert.Null((await _db.TacticCandidates.SingleAsync()).SecondJobId);
    }

    [Fact]
    public async Task Pump_OwnerHasOnlyTheSecondEngine_FirstCheckDoesNotUseItAndWaits()
    {
        _db.GameAnalyses.Add(new GameAnalysis { Id = 1, UserId = 1, Origin = GameAnalysisOrigin.Club, Pgn = "", StartFen = "x", Status = GameAnalysisStatus.Done });
        var c = Cand1("material", "b2b7", LadderPrev, LadderFen);
        c.Id = 0; c.Status = TacticCandidateStatus.Verifying; c.NextFen = "6k1/1R6/8/8/8/8/8/R5K1 w - - 2 2"; c.PendingReplyUci = "h8g8";
        _db.TacticCandidates.Add(c);
        await _db.SaveChangesAsync();
        // Nur lc0 hinterlegt und als „nur auf Anforderung" gesperrt: die automatische Wahl wirft, die Ernte wartet.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!", [$"{ExplicitOnlyEngines.ConfigKey}:0"] = "rhe_lc0",
            ["TacticHarvest:SecondEngineId"] = "rhe_lc0",
        }).Build();
        _db.AppUsers.Add(new AppUser { Id = Owner, Username = "kahalm", PasswordHash = "x", IsAdmin = true });
        var cred = new LichessEngineCredential { UserId = Owner, EncryptedToken = "enc" };
        cred.SetBackgroundEngines(new[] { "rhe_lc0" });
        _db.LichessEngineCredentials.Add(cred);
        await _db.SaveChangesAsync();
        var svc = new TacticHarvestService(_db, new AnalysisJobService(_db, config: config), new QuietHours("", "UTC"), config,
            NullLogger<TacticHarvestService>.Instance);

        await svc.PumpAsync(Owner, default);   // wirft nicht

        Assert.DoesNotContain(_db.AnalysisJobs, j => j.EngineId == "rhe_lc0" && j.TargetNodes == null);
        Assert.Null((await _db.TacticCandidates.SingleAsync()).AnalysisJobId);
    }
}
