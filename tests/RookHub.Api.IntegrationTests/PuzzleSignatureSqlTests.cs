using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using RookHub.Api.Services.Puzzles;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>Mattbilder-Lauf und Auswahl der Übungen gegen ECHTES MariaDB (0.960.0): <c>Themes LIKE</c>, <c>IN</c>-Listen, Fortschritt in
/// <c>SystemSettings</c>, Bereichsabfragen über (Signatur, Rating) und (Thema, Rating) — InMemory prüft die Übersetzung nicht.</summary>
public class PuzzleSignatureSqlTests(PuzzleSignatureSqlFixture fixture) : IAsyncLifetime, IClassFixture<PuzzleSignatureSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [MySqlFact]
    public async Task Worker_AndExercises_RunOnMariaDb()
    {
        await using (var db = fixture.Schema.NewContext())
        {
            var user = new AppUser { Username = "ex", Email = "ex@test.invalid", PasswordHash = "x" };
            db.AppUsers.Add(user);
            var i = 0;
            foreach (var r in new[] { 1500, 1550, 1600 })
                db.Puzzles.Add(new Puzzle { LichessId = $"sig{++i}", Fen = "6k1/p4p1p/5P1Q/8/8/8/8/K7 b - - 0 1", Moves = "a7a6 h6g7", Rating = r, Popularity = 95, NbPlays = 900, Themes = "mate mateIn1" });
            db.Puzzles.Add(new Puzzle { LichessId = "fam", Fen = "6k1/p4p1p/7Q/7N/8/8/8/K7 b - - 0 1", Moves = "a7a6 h6g7", Rating = 1520, Popularity = 95, NbPlays = 900, Themes = "mate mateIn1" });
            db.Puzzles.Add(new Puzzle { LichessId = "other", Fen = "6k1/p4p1p/8/8/8/8/8/K7 b - - 0 1", Moves = "a7a6", Rating = 1500, Popularity = 95, NbPlays = 900, Themes = "endgame" });
            await db.SaveChangesAsync();
            var analysis = new GameAnalysis { UserId = user.Id, Pgn = "", StartFen = "", Status = GameAnalysisStatus.Done };
            db.GameAnalyses.Add(analysis);
            await db.SaveChangesAsync();
            db.GameAnalysisPositions.AddRange(
                new GameAnalysisPosition { GameAnalysisId = analysis.Id, Ply = 10, Fen = "6k1/5p1p/5P1Q/8/8/8/8/K7 w - - 0 1", GameMoveUci = "a1b1", GameMoveSan = "Kb1", CandidatesJson = """[{"uci":"h6g7","mate":1,"pv":["h6g7"]}]""" },
                new GameAnalysisPosition { GameAnalysisId = analysis.Id, Ply = 11, Fen = "6k1/5p1p/5P1Q/8/8/8/8/1K6 b - - 1 1", GameMoveUci = "g8h8", GameMoveSan = "Kh8", CandidatesJson = """[{"uci":"g8h8","cp":-900}]""" });
            db.SavedGames.Add(new SavedGame { UserId = user.Id, Pgn = "", Source = "pgn", WhiteElo = 1550, GameAnalysisId = analysis.Id, ShareToken = "sqltok" });
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.Schema.NewContext())
        {
            Assert.Equal(2, await PuzzleSignatureWorker.RunBatchAsync(db, 2));
            Assert.Equal(2, await PuzzleSignatureWorker.RunBatchAsync(db, 2));
            Assert.Equal(0, await PuzzleSignatureWorker.RunBatchAsync(db, 2));
            Assert.True(await PuzzleSignatureWorker.DoneAsync(db));
            Assert.Equal(4, await db.PuzzleSignatures.CountAsync());
        }
        await using (var db = fixture.Schema.NewContext())
        {
            var dto = await new GameExercises(db, new EbookSuggestions(db)).ForSharedOwnGameAsync("sqltok");
            var g = Assert.Single(dto!.Groups);
            Assert.Equal(3, g.Exercises.Count(e => e.Match == "strict"));
            Assert.Equal(1, g.Exercises.Count(e => e.Match == "family"));
        }
    }
}

public sealed class PuzzleSignatureSqlFixture() : MariaDbClassFixture("pzsig", withApp: false);
