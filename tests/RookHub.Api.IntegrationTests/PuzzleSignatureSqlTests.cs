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
            db.Puzzles.Add(new Puzzle { LichessId = "fork", Fen = "r3k3/p7/8/3N4/8/8/8/4K3 b - - 0 1", Moves = "a7a6 d5c7 e8d7 c7a8", Rating = 1500, Popularity = 95, NbPlays = 900, Themes = "fork short" });
            db.Puzzles.Add(new Puzzle { LichessId = "nothemes", Fen = "r3k3/p7/8/3N4/8/8/8/4K3 b - - 0 1", Moves = "a7a6 d5c7 e8d7 c7a8", Rating = 1500, Popularity = 95, NbPlays = 900, Themes = null });
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
            // Motive (0.961.0): Aufgaben ohne Matt, auch ohne Themen; die Matt-Aufgaben bleiben außen vor.
            Assert.Equal(3, await PuzzleSignatureWorker.RunTacticBatchAsync(db, 10));
            Assert.Equal(0, await PuzzleSignatureWorker.RunTacticBatchAsync(db, 10));
            Assert.Equal(2, await db.PuzzleSignatures.CountAsync(x => x.Kind == PuzzleSignatureKind.Fork));
        }
        await using (var db = fixture.Schema.NewContext())
        {
            var dto = await new GameExercises(db, new EbookSuggestions(db)).ForSharedOwnGameAsync("sqltok");
            var g = Assert.Single(dto!.Groups);
            Assert.Equal(3, g.Exercises.Count(e => e.Match == "strict"));
            Assert.Equal(1, g.Exercises.Count(e => e.Match == "family"));
        }
    }

    /// <summary>Buchbeispiele (0.962.0): Lauf über die Diagramme (Bereichs-Löschen, Fortschritt), Auswahl mit der Rechte-Unterabfrage
    /// (<c>EbookAccess.Visible</c> als <c>IN</c>, auch über Gruppen), Mischwert-Fenster, Meldungen/Korrekturen — gegen MariaDB.</summary>
    [MySqlFact]
    public async Task BookSignatures_AndRights_RunOnMariaDb()
    {
        int userId, otherId;
        await using (var db = fixture.Schema.NewContext())
        {
            var user = new AppUser { Username = "bk", Email = "bk@test.invalid", PasswordHash = "x" };
            var other = new AppUser { Username = "bk2", Email = "bk2@test.invalid", PasswordHash = "x" };
            db.AppUsers.AddRange(user, other);
            var everyone = new Group { Name = "Everyone", IsEveryone = true };
            var club = new Group { Name = "Verein" };
            db.Groups.AddRange(everyone, club);
            await db.SaveChangesAsync();
            (userId, otherId) = (user.Id, other.Id);
            db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = club.Id });
            var open = new Ebook { Title = "Offen", Author = "A", GroupAccess = [new EbookGroupAccess { GroupId = everyone.Id }] };
            var clubBook = new Ebook { Title = "Verein", Author = "B", TargetMinElo = 1400, GroupAccess = [new EbookGroupAccess { GroupId = club.Id }] };
            var foreign = new Ebook { Title = "Fremd", Author = "C", OwnerUserId = other.Id };
            db.Ebooks.AddRange(open, clubBook, foreign);
            await db.SaveChangesAsync();
            EbookDiagram D(Ebook b, string fen, EbookSolutionCheck check = EbookSolutionCheck.Confirmed, string source = "book") =>
                new() { EbookId = b.Id, Page = 5, W = 0.3, H = 0.3, Fen = fen, SolutionUci = "h6g7", SolutionSource = source, SolutionCheck = check };
            const string pawn = "6k1/5p1p/5P1Q/8/8/8/8/K7 w - - 0 1", knight = "6k1/5p1p/7Q/7N/8/8/8/K7 w - - 0 1";
            var reported = D(open, pawn);
            db.EbookDiagrams.AddRange(D(open, knight), D(clubBook, pawn), D(clubBook, pawn, EbookSolutionCheck.Engine, "engine"), D(foreign, pawn), reported);
            await db.SaveChangesAsync();
            db.EbookDiagramReports.Add(new EbookDiagramReport { EbookId = open.Id, DiagramId = reported.Id, UserId = other.Id, Page = 5, Fen = pawn });
            var analysis = new GameAnalysis { UserId = user.Id, Pgn = "", StartFen = "", Status = GameAnalysisStatus.Done };
            db.GameAnalyses.Add(analysis);
            await db.SaveChangesAsync();
            db.GameAnalysisPositions.AddRange(
                new GameAnalysisPosition { GameAnalysisId = analysis.Id, Ply = 10, Fen = "6k1/5p1p/5P1Q/8/8/8/8/K7 w - - 0 1", GameMoveUci = "a1b1", GameMoveSan = "Kb1", CandidatesJson = """[{"uci":"h6g7","mate":1,"pv":["h6g7"]}]""" },
                new GameAnalysisPosition { GameAnalysisId = analysis.Id, Ply = 11, Fen = "6k1/5p1p/5P1Q/8/8/8/8/1K6 b - - 1 1", GameMoveUci = "g8h8", GameMoveSan = "Kh8", CandidatesJson = """[{"uci":"g8h8","cp":-900}]""" });
            db.SavedGames.Add(new SavedGame { UserId = user.Id, Pgn = "", Source = "pgn", WhiteElo = 1550, GameAnalysisId = analysis.Id, ShareToken = "booktok" });
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.Schema.NewContext())
        {
            Assert.Equal(3, await BookExercises.RunBatchAsync(db, null, 3));
            Assert.Equal(2, await BookExercises.RunBatchAsync(db, null, 3));
            Assert.Equal(0, await BookExercises.RunBatchAsync(db, null, 3));
            Assert.Equal(5, await db.EbookDiagramSignatures.CountAsync());
        }
        await using (var db = fixture.Schema.NewContext())
        {
            var svc = new GameExercises(db, new EbookSuggestions(db));
            var mine = (await svc.ForSharedOwnGameAsync("booktok", default, new EbookViewer(userId, false)))!;
            var books = Assert.Single(mine.Groups).Exercises.Where(e => e.Source == "book").ToList();
            // Streng lösbar (Verein), Familie lösbar (Offen), zuletzt das Beispiel mit Engine-Lösung (Verein); das gemeldete und das fremde fehlen.
            Assert.Equal(["Verein", "Offen", "Verein"], books.Select(e => e.Book!.Title));
            Assert.Equal(2, books.Count(e => e.Book!.Solvable));
            var anon = (await svc.ForSharedOwnGameAsync("booktok", default, new EbookViewer(EbookCaller.Anonymous(null), false)))!;
            Assert.Equal(["Offen"], Assert.Single(anon.Groups).Exercises.Where(e => e.Source == "book").Select(e => e.Book!.Title));
            var foreign = (await svc.ForSharedOwnGameAsync("booktok", default, new EbookViewer(otherId, false)))!;
            Assert.Contains(foreign.Groups.SelectMany(g => g.Exercises), e => e.Book?.Title == "Fremd");
        }
    }
}

public sealed class PuzzleSignatureSqlFixture() : MariaDbClassFixture("pzsig", withApp: false);
