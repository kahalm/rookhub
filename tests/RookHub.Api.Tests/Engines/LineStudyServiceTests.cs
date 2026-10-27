using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Engines;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Tests.Engines;

/// <summary>„Ohne Engine analysieren" (0.855.0): Baum je Nutzer und Partie, jede Stellung im Hintergrund, Zahlen erst nach dem
/// Einschalten der Engine.</summary>
public class LineStudyServiceTests : IDisposable
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private readonly AppDbContext _db;
    private readonly LineStudyService _svc;

    public LineStudyServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _svc = new LineStudyService(_db, new AnalysisJobService(_db));
    }

    public void Dispose() => _db.Dispose();

    private async Task UserAsync(int id, bool engine = true)
    {
        _db.AppUsers.Add(new AppUser { Id = id, Username = "u" + id, PasswordHash = "x" });
        if (engine) _db.LichessEngineCredentials.Add(new LichessEngineCredential { UserId = id, EncryptedToken = "enc", BackgroundEngineIds = "eei_bg" });
        await _db.SaveChangesAsync();
    }

    /// <summary>1.e4 e5 (Hauptlinie) und 1…c5 als Variante.</summary>
    private static SaveLineStudyRequest Req(string key = "game:abc") => new()
    {
        GameKey = key, StartFen = Start, Title = "A – B", Current = 1,
        Tree = new AnalysisTreeDto { N = [new() { P = -1, U = "e2e4" }, new() { P = 0, U = "e7e5" }, new() { P = 0, U = "c7c5" }] },
    };

    [Fact]
    public async Task Save_CreatesOnePositionPerNode_PlusTheStart_AndReplacesOnSecondSave()
    {
        await UserAsync(997201);
        var dto = await _svc.SaveAsync(997201, Req());
        Assert.Equal((4, 0, false), (dto.Total, dto.Analyzed, dto.Revealed));
        Assert.Equal(3, dto.Tree.N!.Count);

        var again = Req();
        again.Tree!.N!.Add(new() { P = 1, U = "g1f3" });
        var second = await _svc.SaveAsync(997201, again);
        Assert.Equal(dto.Id, second.Id);                       // je Partie EIN Baum
        Assert.Equal(5, second.Total);
        Assert.Equal(dto.Id, (await _svc.GetAsync(997201, "game:abc"))!.Id);
        Assert.Null(await _svc.GetAsync(997202, "game:abc"));   // fremd
    }

    [Fact]
    public async Task Save_IllegalMoveOrKey_IsRejected()
    {
        await UserAsync(997203);
        var bad = Req();
        bad.Tree!.N![1].U = "e7e4";
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SaveAsync(997203, bad));
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SaveAsync(997203, Req("bad key!")));
    }

    [Fact]
    public async Task Save_MatePosition_IsDoneWithoutJob()
    {
        await UserAsync(997204);
        // Narrenmatt: 1.f3 e5 2.g4 Dh4#
        var dto = await _svc.SaveAsync(997204, new SaveLineStudyRequest
        {
            GameKey = "game:mate", StartFen = Start,
            Tree = new AnalysisTreeDto { N = [new() { P = -1, U = "f2f3" }, new() { P = 0, U = "e7e5" }, new() { P = 1, U = "g2g4" }, new() { P = 2, U = "d8h4" }] },
        });
        Assert.Equal((5, 1), (dto.Total, dto.Analyzed));
    }

    [Fact]
    public async Task Step_QueuesJobs_CollectsResults_AndEvalsOnlyAfterReveal()
    {
        await UserAsync(997205);
        var dto = await _svc.SaveAsync(997205, Req());

        var (collected, queued) = await _svc.StepAsync();
        Assert.Equal((0, 4), (collected, queued));
        var jobs = await _db.AnalysisJobs.ToListAsync();
        Assert.All(jobs, j => Assert.True(j.Background));
        Assert.Empty(await _db.RememberedPositions.ToListAsync());   // nicht in „Gemerkte Stellungen"

        foreach (var job in jobs)
        {
            job.Status = AnalysisJobStatus.Done;
            job.ReachedDepth = 20;
            job.ResultJson = "{\"depth\":20,\"pvs\":[{\"depth\":20,\"cp\":30,\"moves\":[\"" + FirstLegal(job.Fen) + "\"]}]}";
        }
        await _db.SaveChangesAsync();
        Assert.Equal((4, 0), await _svc.StepAsync());
        Assert.Empty(await _db.AnalysisJobs.ToListAsync());          // übernommen und weg

        var hidden = (await _svc.EvalsAsync(997205, dto.Id))!;
        Assert.Equal((false, 4, 4), (hidden.Revealed, hidden.Analyzed, hidden.Total));
        Assert.Empty(hidden.Nodes);                                 // vor dem Einschalten keine Zahlen

        Assert.True((await _svc.RevealAsync(997205, dto.Id))!.Revealed);
        var shown = (await _svc.EvalsAsync(997205, dto.Id))!;
        Assert.Equal([-1, 0, 1, 2], shown.Nodes.Select(n => n.Index));   // je Knoten des gespeicherten Baums, -1 = Start
        var start = shown.Nodes.Single(n => n.Index == -1);
        Assert.Equal((30, "a2a3"), (start.Cp, start.BestUci));
        Assert.Equal("a7a5", shown.Nodes.Single(n => n.Index == 0).BestUci);   // nach 1.e4: der Bestzug DIESER Stellung (Schwarz)
        Assert.Null(await _svc.EvalsAsync(997206, dto.Id));          // fremd
    }

    [Fact]
    public async Task Step_FailedJob_IsRetried_ThenGivenUp()
    {
        await UserAsync(997207);
        await _svc.SaveAsync(997207, new SaveLineStudyRequest { GameKey = "game:one", StartFen = Start });
        for (var round = 0; round < LineStudyService.MaxAttempts; round++)
        {
            await _svc.StepAsync();
            var job = await _db.AnalysisJobs.SingleAsync();
            job.Status = AnalysisJobStatus.Failed;
            await _db.SaveChangesAsync();
            await _svc.StepAsync();
        }
        Assert.Equal(LineStudyPositionStatus.Failed, (await _db.LineStudyPositions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Step_WithoutAnyEngine_LeavesPositionsPending()
    {
        await UserAsync(997208, engine: false);
        await _svc.SaveAsync(997208, Req());
        Assert.Equal((0, 0), await _svc.StepAsync());
        Assert.All(await _db.LineStudyPositions.ToListAsync(), p => Assert.Equal(LineStudyPositionStatus.Pending, p.Status));
    }

    private static string FirstLegal(string fen) =>
        RookHub.Api.Services.Games.TacticsBoard.TryParse(fen)!.LegalMoves()
            .Select(m => RookHub.Api.Services.Games.TacticsBoard.SquareName(m.From) + RookHub.Api.Services.Games.TacticsBoard.SquareName(m.To))
            .OrderBy(u => u).First();
}
