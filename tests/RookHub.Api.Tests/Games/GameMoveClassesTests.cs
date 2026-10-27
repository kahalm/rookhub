using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>
/// Zug-Klassen am Server (0.854.0) — der Spiegel von <c>reviewGame</c> im Client. Vereinspartie 228 mit den Markierungen von
/// chess.com als LITERALE Erwartung (dieselben Züge prüft <c>game-review.util.spec.ts</c> an kleinen Stellungen). Die
/// Parität wurde beim Port an 300 Prod-Analysen Zeichen für Zeichen gegen den Client geprüft (111 „!!", 917 „!", 76 Miss).
/// </summary>
public class GameMoveClassesTests
{
    private static (List<GameAnalysisPosition> Positions, int PlyCount) Game228()
    {
        var rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "GameReview", "club-game-228.tsv"))
            .Select(l => l.Split('\t'))
            .Select(p => new GameAnalysisPosition
            {
                Ply = int.Parse(p[0]), Fen = p[1], GameMoveUci = p[2], Depth = int.Parse(p[3]), CandidatesJson = p[4] == "" ? null : p[4],
            }).ToList();
        return (rows, 71);
    }

    [Fact]
    public void Game228_Brilliant_And_Great_Like_ChessCom()
    {
        var (positions, plies) = Game228();
        var classes = GameMoveClasses.FromPositions(positions, plies);

        Assert.Equal(71, classes.Length);
        // chess.com: „!!" 22.Sxe6, 24.O-O — „!" 14.Sexd4, 19…Kd7, 23.g3, 30.De7+, 31.Te6+ (Halbzug = 2·(Zug−1) + Schwarz).
        Assert.Equal([42, 46], GameMoveClasses.PliesOf(classes, 'B'));
        Assert.Equal([26, 37, 44, 58, 60], GameMoveClasses.PliesOf(classes, 'G'));
        Assert.Equal('O', classes[48]);   // 25.Df3: gut, aber der hängende Springer war schon geopfert — kein „!!"
        Assert.Equal('X', classes[25]);   // 13…fxe6: verpasste Strafe nach 13.fxe6
        Assert.Equal('P', classes[24]);   // 13.fxe6 ??
    }

    [Fact]
    public void Store_SetsClassesAndVersion()
    {
        var (positions, plies) = Game228();
        var analysis = new GameAnalysis { PlyCount = plies, Positions = positions };
        GameMoveClasses.Store(analysis);
        Assert.Equal(GameMoveClasses.Version, analysis.MoveClassesVersion);
        Assert.Equal(GameMoveClasses.FromPositions(positions, plies), analysis.MoveClasses);
    }

    [Fact]
    public void Compute_WithoutEvaluations_IsAllNone()
    {
        Assert.Equal("--", GameMoveClasses.Compute([], null,
            ["rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1"],
            ["e2e4", "e7e5"], 2));
    }

    [Fact]
    public async Task Harvest_RecomputesMissingAndOldVersions_OnlyDone_NewestFirst()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        GameAnalysis Make(int id, GameAnalysisStatus status, int? version) => new()
        {
            Id = id, UserId = 1, Pgn = "", StartFen = "", Status = status, PlyCount = 71, MoveClassesVersion = version,
            MoveClasses = version is null ? null : "alt",
            Positions = Game228().Positions,
        };
        db.GameAnalyses.AddRange(
            Make(990101, GameAnalysisStatus.Done, null),
            Make(990102, GameAnalysisStatus.Done, GameMoveClasses.Version - 1),
            Make(990103, GameAnalysisStatus.Done, GameMoveClasses.Version),
            Make(990104, GameAnalysisStatus.Running, null));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(1, await MoveClassHarvestWorker.HarvestAsync(db, 1));   // die jüngste zuerst
        Assert.Equal(GameMoveClasses.Version, (await db.GameAnalyses.FindAsync(990102))!.MoveClassesVersion);
        Assert.Equal(1, await MoveClassHarvestWorker.HarvestAsync(db, 10));
        Assert.Equal(0, await MoveClassHarvestWorker.HarvestAsync(db, 10));

        var done = await db.GameAnalyses.AsNoTracking().ToDictionaryAsync(a => a.Id);
        Assert.Equal([42, 46], GameMoveClasses.PliesOf(done[990101].MoveClasses, 'B'));
        Assert.Equal("alt", done[990103].MoveClasses);   // aktueller Stand bleibt
        Assert.Null(done[990104].MoveClasses);           // unfertig: nicht ernten
    }
}
