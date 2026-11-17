using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Die Reparatur der Partieanalysen (<see cref="GameAnalysisRepair"/>, 0.990.0) gegen ECHTES MariaDB: die Seitenabfrage mit
/// Join auf die Analyse und Konstruktor-Projektion, das Wiederöffnen samt Löschen der Erklärungen und der gehärtete
/// Positions-Cache. InMemory übersetzt nichts davon in SQL.
/// </summary>
public class GameAnalysisRepairSqlTests(GameAnalysisRepairSqlFixture fixture)
    : IAsyncLifetime, IClassFixture<GameAnalysisRepairSqlFixture>
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";
    private const string Five = "[{\"uci\":\"e7e5\",\"cp\":-20},{\"uci\":\"c7c5\",\"cp\":-25},{\"uci\":\"e7e6\",\"cp\":-30},"
        + "{\"uci\":\"c7c6\",\"cp\":-32},{\"uci\":\"d7d5\",\"cp\":-40}]";

    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(int Analysis, int Good, int Empty, int Few)> SeedAsync()
    {
        await using var db = fixture.Schema.NewContext();
        var user = new AppUser { Username = "liga", Email = "liga@t.local", PasswordHash = "x" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var a = new GameAnalysis
        {
            UserId = user.Id, Pgn = "1. e4 e5 *", StartFen = Start, PlyCount = 3, MultiPv = 5, TargetDepth = 20,
            Status = GameAnalysisStatus.Done, Origin = GameAnalysisOrigin.League, AccuracyWhite = 90, AccuracyBlack = 80,
            MoveClasses = "x", MoveClassesVersion = 3, FinishedAt = DateTime.UtcNow,
        };
        a.Positions.Add(new GameAnalysisPosition { Ply = 0, Fen = Start, GameMoveUci = "e2e4", GameMoveSan = "e4", CandidatesJson = "[]", Depth = 20 });
        a.Positions.Add(new GameAnalysisPosition { Ply = 1, Fen = AfterE4, GameMoveUci = "e7e5", GameMoveSan = "e5", CandidatesJson = Five, Depth = 20 });
        a.Positions.Add(new GameAnalysisPosition { Ply = 2, Fen = AfterE4, GameMoveUci = "e7e5", GameMoveSan = "e5",
            CandidatesJson = "[{\"uci\":\"e7e5\",\"cp\":-20},{\"uci\":\"c7c5\",\"cp\":-25}]", Depth = 20 });
        db.GameAnalyses.Add(a);
        await db.SaveChangesAsync();
        db.GameMoveExplanations.Add(new GameMoveExplanation { GameAnalysisId = a.Id, Ply = 0, Language = "de", Class = "mistake", Text = "t" });
        await db.SaveChangesAsync();
        var ps = a.Positions.OrderBy(p => p.Ply).ToList();
        return (a.Id, ps[1].Id, ps[0].Id, ps[2].Id);
    }

    private GameAnalysisRepair Repair(Data.AppDbContext db)
        => new(db, new AnalysisJobService(db), NullLogger<GameAnalysisRepair>.Instance);

    [MySqlFact]
    public async Task DryRun_thenApply_onRealMariaDb()
    {
        var (analysisId, good, empty, few) = await SeedAsync();

        await using (var db = fixture.Schema.NewContext())
        {
            var dry = await Repair(db).RunAsync(new GameAnalysisRepairRequest(), adminUserId: 1);
            Assert.Equal((3, 1, 1, 0), (dry.Scanned, dry.FoundEmpty, dry.FoundMissingLines, dry.PositionsReopened));
            Assert.Null(dry.NextCursor);
        }
        await using (var db = fixture.Schema.NewContext())
            Assert.Equal(GameAnalysisStatus.Done, (await db.GameAnalyses.SingleAsync(a => a.Id == analysisId)).Status);

        await using (var db = fixture.Schema.NewContext())
        {
            var paged = await Repair(db).RunAsync(new GameAnalysisRepairRequest(MaxScan: 1), adminUserId: 1);
            Assert.Equal(new[] { good, empty, few }.Min(), paged.NextCursor);
            var applied = await Repair(db).RunAsync(new GameAnalysisRepairRequest(Apply: true), adminUserId: 1);
            Assert.Equal((2, 1, 1), (applied.PositionsReopened, applied.AnalysesReopened, applied.ExplanationsDeleted));
        }

        await using (var db = fixture.Schema.NewContext())
        {
            var a = await db.GameAnalyses.Include(x => x.Positions).SingleAsync(x => x.Id == analysisId);
            Assert.Equal(GameAnalysisStatus.Pending, a.Status);
            Assert.Null(a.AccuracyWhite);
            Assert.Null(a.MoveClasses);
            Assert.Null(a.Positions.Single(p => p.Id == empty).CandidatesJson);
            Assert.Null(a.Positions.Single(p => p.Id == few).CandidatesJson);
            Assert.Equal(Five, a.Positions.Single(p => p.Id == good).CandidatesJson);
            Assert.False(await db.GameMoveExplanations.AnyAsync(x => x.GameAnalysisId == analysisId));
        }
    }

    [MySqlFact]
    public async Task PositionCache_skipsSourcesWithTooFewLines_onRealMariaDb()
    {
        await SeedAsync();
        await using var db = fixture.Schema.NewContext();
        var target = new GameAnalysis { UserId = (await db.AppUsers.FirstAsync()).Id, Pgn = "", StartFen = Start, MultiPv = 1, TargetDepth = 20 };
        db.GameAnalyses.Add(target);
        await db.SaveChangesAsync();

        // Quellen für AfterE4: eine gute (5 Linien) und eine mit 2 von 5 — gefunden werden darf nur die gute.
        var hit = await GameAnalysisPositionCache.FindAsync(db, target, AfterE4, 20, 1, default);
        Assert.Equal(Five, hit!.CandidatesJson);
        db.GameAnalysisPositions.Remove(await db.GameAnalysisPositions.SingleAsync(p => p.CandidatesJson == Five));
        await db.SaveChangesAsync();
        Assert.Null(await GameAnalysisPositionCache.FindAsync(db, target, AfterE4, 20, 1, default));
    }
}

public sealed class GameAnalysisRepairSqlFixture() : MariaDbClassFixture("garepair", withApp: false);
