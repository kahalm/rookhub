using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers.Courses;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Ebooks;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Puzzles;
using Xunit;

namespace RookHub.Api.Tests.Courses;

/// <summary>Kurs „Persönliche Taktiken“ (<see cref="PersonalTacticsCourse"/>, <see cref="PersonalTacticsController"/>):
/// die Empfohlenen Übungen einer Partie als Kapitel — ein Kurs je Konto, ein Kapitel je Partie, gelöst im normalen Kurs-Löser.</summary>
public class PersonalTacticsCourseTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private PersonalTacticsCourse Service() => new(_db, new GameExercises(_db, new EbookSuggestions(_db)));
    private static readonly EbookViewer Viewer1 = new(1, false);

    private int _puzzleId, _positionId;
    private void AddPuzzle(string fen, string moves, int rating, string themes)
    {
        _db.Puzzles.Add(new Puzzle { Id = ++_puzzleId, LichessId = $"L{_puzzleId}", Fen = fen, Moves = moves, Rating = rating, Popularity = 95, NbPlays = 1000, Themes = themes });
    }
    private void AddPosition(int analysisId, int ply, string fen, string move, string cands) =>
        _db.GameAnalysisPositions.Add(new GameAnalysisPosition { Id = ++_positionId, GameAnalysisId = analysisId, Ply = ply, Fen = fen, GameMoveUci = move, GameMoveSan = "x", CandidatesJson = cands });

    /// <summary>Weiß verpasst Dg7# (Zug 6) und setzt später doch matt; dazu drei Aufgaben mit demselben Mattbild.</summary>
    private void SeedMatePositions(int analysisId)
    {
        AddPosition(analysisId, 10, "6k1/5p1p/5P1Q/8/8/8/8/K7 w - - 0 1", "a1b1", """[{"uci":"h6g7","mate":1,"pv":["h6g7"]},{"uci":"h6h5","cp":800}]""");
        AddPosition(analysisId, 11, "6k1/5p1p/5P1Q/8/8/8/8/1K6 b - - 1 1", "g8h8", """[{"uci":"g8h8","cp":-900}]""");
        AddPosition(analysisId, 12, "7k/5p1p/5P1Q/8/8/8/8/1K6 w - - 2 2", "h6g7", """[{"uci":"h6g7","mate":1,"pv":["h6g7"]}]""");
    }

    /// <summary>Weiß verpasst die Springergabel Sc7+ (Zug 1); dazu drei Gabel-Aufgaben.</summary>
    private void SeedForkPositions(int analysisId)
    {
        AddPosition(analysisId, 0, "r3k3/8/8/3N4/8/8/8/4K3 w - - 0 1", "e1e2", """[{"uci":"d5c7","cp":600,"pv":["d5c7","e8d7","c7a8"]},{"uci":"e1e2","cp":0}]""");
        AddPosition(analysisId, 1, "r3k3/8/8/3N4/8/8/4K3/8 b - - 1 1", "a8a7", """[{"uci":"a8a7","cp":0,"pv":["a8a7"]}]""");
        AddPosition(analysisId, 2, "4k3/r7/8/3N4/8/8/4K3/8 w - - 2 2", "", """[{"uci":"d5c7","cp":0}]""");
    }

    /// <summary>Eigene Partien 7 (Matt) und 11 (Gabel) von Nutzer 1, Partie 20 von Nutzer 2 (mit Teilen-Link), Vereinspartie 247.</summary>
    private async Task SeedAsync()
    {
        _db.SavedGames.Add(new SavedGame { Id = 7, UserId = 1, Pgn = "", White = "Anna", Black = "Bert", PlayedAt = new DateTime(2026, 3, 1), WhiteElo = 1550, BlackElo = 1500, GameAnalysisId = 1, ShareToken = "tok7" });
        SeedMatePositions(1);
        _db.SavedGames.Add(new SavedGame { Id = 11, UserId = 1, Pgn = "", White = "Anna", Black = "Cleo", PlayedAt = new DateTime(2025, 5, 1), WhiteElo = 1500, GameAnalysisId = 3, ShareToken = "tok11" });
        SeedForkPositions(3);
        _db.SavedGames.Add(new SavedGame { Id = 20, UserId = 2, Pgn = "", White = "Dora", Black = "Emil", WhiteElo = 1500, GameAnalysisId = 4, ShareToken = "tok20" });
        SeedForkPositions(4);

        _db.LeagueClubs.Add(new LeagueClub { Id = 1, Name = "Schwaz", AnonName = "Schwaz", CreatedAt = DateTime.UtcNow });
        _db.LeagueClubGames.Add(new LeagueClubGame { Id = 247, ClubId = 1, White = "Delmarko, Dietmar", Black = "Schwaz", Year = 2026, Anonymized = true, BlackRealName = "Geheim, Gustav", WhiteElo = 1500, ShareToken = "cg247" });
        _db.GameAnalyses.Add(new GameAnalysis { Id = 5, UserId = 1, Pgn = "", StartFen = "", LeagueClubGameId = 247, Origin = GameAnalysisOrigin.Club });
        SeedForkPositions(5);

        foreach (var r in new[] { 1500, 1550, 1600 }) AddPuzzle("6k1/p4p1p/5P1Q/8/8/8/8/K7 b - - 0 1", "a7a6 h6g7", r, "mate mateIn1");
        foreach (var r in new[] { 1450, 1500, 1550 }) AddPuzzle("r3k3/p7/8/3N4/8/8/8/4K3 b - - 0 1", "a7a6 d5c7 e8d7 c7a8", r, "fork short");
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        while (await PuzzleSignatureWorker.RunBatchAsync(_db, 10) > 0) { }
        while (await PuzzleSignatureWorker.RunTacticBatchAsync(_db, 10) > 0) { }
    }

    private Task<PersonalTacticsChapterDto> OpenOwnAsync(int gameId, string? lang = "de") =>
        Service().OpenAsync(1, new PersonalTacticsCourse.Game(PersonalTacticsCourse.GameKind.Own, gameId, true), Viewer1, lang);

    [Fact]
    public async Task Open_FirstClick_CreatesCourseAndChapterWithTheExercisesInSectionOrder()
    {
        await SeedAsync();
        var shown = (await new GameExercises(_db, new EbookSuggestions(_db)).ForOwnGameAsync(1, 7, default, Viewer1))!
            .Groups.SelectMany(g => g.Exercises).ToList();
        Assert.NotEmpty(shown);

        var dto = await OpenOwnAsync(7);

        Assert.True(dto.Created);
        Assert.Equal(0, dto.ChapterIndex);
        Assert.Equal("Anna – Bert (2026)", dto.Chapter);
        Assert.Equal(shown.Count, dto.Lines);

        var book = await _db.Books.AsNoTracking().SingleAsync();
        Assert.Equal(dto.BookId, book.Id);
        Assert.Equal("personal-tactics-u1.pgn", book.FileName);
        Assert.Equal(1, book.OwnerUserId);
        Assert.Equal("Persönliche Taktiken", book.DisplayName);
        Assert.Equal(ImportPipeline.CurrentVersion, book.ImportVersion);   // nie „veraltet“ im Aktualisieren-Banner
        Assert.False(book.ForDaily || book.ForRandom || book.ForBlind || book.IsPublic);

        var lines = await _db.BookPuzzles.AsNoTracking().Where(p => p.BookId == book.Id).InReadingOrder().ToListAsync();
        // Lichess-Form unverändert: Stellung vor dem Gegnerzug, Gegnerzug + Lösung, klassischer Start.
        Assert.Equal(shown.Select(e => (e.Fen, string.Join(' ', e.Moves))), lines.Select(l => (l.Fen, l.Moves)));
        Assert.All(lines, l =>
        {
            Assert.Equal(0, l.StartPly);
            Assert.False(l.IsInfoOnly);
            Assert.Equal("Anna – Bert (2026)", l.Chapter);
            Assert.Equal(PersonalTacticsCourse.LineSource, l.Source);
            Assert.StartsWith("personal-tactics-u1.pgn:own7:", l.LineId);
            Assert.Contains("mateIn1", l.Tags);
            Assert.Contains("Lichess L", l.Title);
        });
        Assert.Equal("own:7:10", lines[0].SourceGame);
        Assert.StartsWith("6. · Lichess L", lines[0].Title);
        Assert.Equal(shown[0].Rating, lines[0].BookRating);
        Assert.Equal(lines.Count, lines.Select(l => l.LineId).Distinct().Count());
    }

    [Fact]
    public async Task Open_SameGameAgain_OpensTheExistingChapter_EvenAfterRenaming_ProgressStays()
    {
        await SeedAsync();
        var first = await OpenOwnAsync(7);
        var courses = TestServices.Course(_db);
        var puzzle = (await courses.GetNextAsync(1, first.BookId, "sequential", null, null, false, first.ChapterIndex)).Puzzle!;
        await courses.RecordResultAsync(1, first.BookId, new RecordCourseResultDto { BookPuzzleId = puzzle.Id, Solved = true, Mode = "sequential" }, false);

        // Kurs und Kapitel umbenennen — erkannt wird am Dateinamen bzw. an den Linien der Partie, nicht am Namen.
        var book = await _db.Books.SingleAsync();
        book.DisplayName = "Meine Taktik";
        foreach (var l in await _db.BookPuzzles.ToListAsync()) l.Chapter = "Umbenannt";
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var again = await OpenOwnAsync(7);

        Assert.False(again.Created);
        Assert.Equal((first.BookId, first.ChapterIndex, first.Lines), (again.BookId, again.ChapterIndex, again.Lines));
        Assert.Equal("Umbenannt", again.Chapter);
        Assert.Equal(1, await _db.Books.CountAsync());
        Assert.Equal(first.Lines, await _db.BookPuzzles.CountAsync());
        Assert.Equal("Meine Taktik", (await _db.Books.AsNoTracking().SingleAsync()).DisplayName);
        Assert.Equal(1, await _db.CoursePuzzleResults.CountAsync(r => r.UserId == 1 && r.BookId == first.BookId));
    }

    [Fact]
    public async Task Open_SecondGame_SecondChapterInTheSameCourse_OtherUserGetsOwnCourse()
    {
        await SeedAsync();
        var a = await OpenOwnAsync(7);
        var b = await OpenOwnAsync(11);

        Assert.Equal(a.BookId, b.BookId);
        Assert.True(b.Created);
        Assert.Equal(1, b.ChapterIndex);
        Assert.Equal("Anna – Cleo (2025)", b.Chapter);
        Assert.Equal(0, (await OpenOwnAsync(7)).ChapterIndex);   // das erste Kapitel bleibt vorn
        var chapters = await TestServices.Course(_db).GetChaptersAsync(1, a.BookId, false);
        Assert.Equal(["Anna – Bert (2026)", "Anna – Cleo (2025)"], chapters.Select(c => c.Name));
        Assert.Equal([a.Lines, b.Lines], chapters.Select(c => c.PuzzleCount));

        var other = await Service().OpenAsync(2, new PersonalTacticsCourse.Game(PersonalTacticsCourse.GameKind.Own, 20, true), new EbookViewer(2, false), "en");
        Assert.NotEqual(a.BookId, other.BookId);
        var otherBook = await _db.Books.AsNoTracking().SingleAsync(x => x.Id == other.BookId);
        Assert.Equal(("personal-tactics-u2.pgn", 2, "Personal tactics"), (otherBook.FileName, otherBook.OwnerUserId!.Value, otherBook.DisplayName));
        // Nur für den Besitzer sichtbar.
        Assert.False(await CourseAccess.CanAccessAsync(_db, 2, a.BookId, false));
        Assert.True(await CourseAccess.CanAccessAsync(_db, 1, a.BookId, false));
    }

    [Fact]
    public async Task Open_SameNameTwice_ChapterNameStaysUnique()
    {
        await SeedAsync();
        var g = await _db.SavedGames.SingleAsync(x => x.Id == 11);
        (g.Black, g.PlayedAt) = ("Bert", new DateTime(2026, 9, 9));
        await _db.SaveChangesAsync();

        Assert.Equal("Anna – Bert (2026)", (await OpenOwnAsync(7)).Chapter);
        var second = await OpenOwnAsync(11);
        Assert.Equal("Anna – Bert (2026) · 2", second.Chapter);
        Assert.Equal(1, second.ChapterIndex);
    }

    [Fact]
    public async Task Solver_PlaysTheChapterSequentially_ThenCompleted()
    {
        await SeedAsync();
        await OpenOwnAsync(7);
        var dto = await OpenOwnAsync(11);
        var expected = await _db.BookPuzzles.AsNoTracking().Where(p => p.BookId == dto.BookId && p.Chapter == dto.Chapter)
            .InReadingOrder().Select(p => p.Id).ToListAsync();
        Assert.Equal(3, expected.Count);
        var courses = TestServices.Course(_db);

        var played = new List<int>();
        for (var i = 0; i < expected.Count; i++)
        {
            var next = await courses.GetNextAsync(1, dto.BookId, "sequential", null, null, false, dto.ChapterIndex);
            Assert.False(next.Completed);
            Assert.Equal(expected.Count, next.Total);
            Assert.Equal("a7a6 d5c7 e8d7 c7a8", next.Puzzle!.Moves);
            Assert.Equal(0, next.Puzzle.StartPly);
            Assert.Equal("own", next.Puzzle.SourceGame!.Kind);
            played.Add(next.Puzzle.Id);
            await courses.RecordResultAsync(1, dto.BookId, new RecordCourseResultDto { BookPuzzleId = next.Puzzle.Id, Solved = true, Mode = "sequential", ChapterIndex = dto.ChapterIndex }, false);
        }
        Assert.Equal(expected, played);
        Assert.True((await courses.GetNextAsync(1, dto.BookId, "sequential", null, null, false, dto.ChapterIndex)).Completed);
        // Das andere Kapitel ist davon unberührt.
        Assert.False((await courses.GetNextAsync(1, dto.BookId, "sequential", null, null, false, 0)).Completed);
    }

    [Fact]
    public async Task Open_GameWithoutExercises_Throws_AndCreatesNothing()
    {
        _db.SavedGames.Add(new SavedGame { Id = 9, UserId = 1, Pgn = "" });
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<DomainValidationException>(() => OpenOwnAsync(9));
        Assert.Empty(await _db.Books.ToListAsync());
    }

    [Fact]
    public async Task Open_StaleImportVersion_IsLiftedOnTheNextClick()
    {
        await SeedAsync();
        var dto = await OpenOwnAsync(7);
        var book = await _db.Books.SingleAsync();
        book.ImportVersion = ImportPipeline.CurrentVersion - 1;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await OpenOwnAsync(7);
        Assert.Equal(ImportPipeline.CurrentVersion, (await _db.Books.AsNoTracking().SingleAsync(b => b.Id == dto.BookId)).ImportVersion);
    }

    [Fact]
    public void Helpers_MoveLabel_Tags_GameLabel()
    {
        Assert.Equal("1.", PersonalTacticsCourse.MoveLabel(0));
        Assert.Equal("19.", PersonalTacticsCourse.MoveLabel(36));
        Assert.Equal("19…", PersonalTacticsCourse.MoveLabel(37));
        Assert.Equal("fork,short", PersonalTacticsCourse.Tags("fork", ["fork", "short", " "]));
        Assert.Null(PersonalTacticsCourse.Tags(null, []));
        Assert.True(PersonalTacticsCourse.Tags("fork", Enumerable.Range(0, 100).Select(i => $"theme{i}"))!.Length <= 200);
        Assert.Equal("? – Bert", PersonalTacticsCourse.GameLabel(null, " Bert ", null));
        Assert.True(PersonalTacticsCourse.GameLabel(new string('a', 300), "b", 2026).Length <= 200);
        Assert.Equal("personal-tactics-u42.pgn", PersonalTacticsCourse.FileOf(42));
    }

    // ── Controller: Zugriff auf die Partie je Weg ──

    private PersonalTacticsController Controller(int userId, bool admin = false, params string[] permissions)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        claims.AddRange(permissions.Select(p => new Claim(RookHub.Api.Authorization.PermissionAuthorizationHandler.PermissionClaimType, p)));
        return new PersonalTacticsController(Service(), new LeagueClubResolver(_db))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) } },
        };
    }

    private static Task<IActionResult> PostAsync(PersonalTacticsController c, string? kind, int? id = null, string? token = null) =>
        c.FromGame(new PersonalTacticsFromGameDto { Kind = kind, Id = id, Token = token, Lang = "en" }, null, null, default);

    [Fact]
    public async Task FromGame_OwnGame_Ok_ForeignOrUnknown_404_AndNothingCreated()
    {
        await SeedAsync();
        var ok = Assert.IsType<OkObjectResult>(await PostAsync(Controller(1), "own", 7));
        var dto = Assert.IsType<PersonalTacticsChapterDto>(ok.Value);
        Assert.True(dto.Created);
        Assert.Equal(0, dto.ChapterIndex);

        Assert.IsType<NotFoundResult>(await PostAsync(Controller(2), "own", 7));     // fremde Partie
        Assert.IsType<NotFoundResult>(await PostAsync(Controller(1), "own", 999));
        Assert.IsType<NotFoundResult>(await PostAsync(Controller(2), "own-share", token: "nope"));
        Assert.IsType<NotFoundResult>(await PostAsync(Controller(2), "club-link", token: "nope"));
        Assert.IsType<NotFoundResult>(await PostAsync(Controller(2), "club-share", 247, "nope"));   // ohne Liga-Dienste kein Weg
        Assert.IsType<BadRequestObjectResult>(await PostAsync(Controller(1), "weird", 7));
        Assert.IsType<BadRequestObjectResult>(await PostAsync(Controller(1), "own"));
        Assert.IsType<BadRequestObjectResult>(await Controller(1).FromGame(null, null, null, default));
        Assert.Equal(1, await _db.Books.CountAsync());
    }

    [Fact]
    public async Task FromGame_SharedLinks_CourseBelongsToTheCaller_NoBackLinkToAGameHeCannotOpen()
    {
        await SeedAsync();
        // Nutzer 1 sieht die Partie 20 von Nutzer 2 über deren Teilen-Link.
        var own = Assert.IsType<PersonalTacticsChapterDto>(Assert.IsType<OkObjectResult>(await PostAsync(Controller(1), "own-share", token: "tok20")).Value);
        var book = await _db.Books.AsNoTracking().SingleAsync(b => b.Id == own.BookId);
        Assert.Equal(1, book.OwnerUserId);
        Assert.Equal("Dora – Emil", own.Chapter);
        Assert.All(await _db.BookPuzzles.AsNoTracking().Where(p => p.Chapter == own.Chapter).ToListAsync(), l => Assert.Null(l.SourceGame));

        // Vereinspartie über ihren eigenen Link: gespeicherte (anonymisierte) Namen, nie der Klarname.
        var club = Assert.IsType<PersonalTacticsChapterDto>(Assert.IsType<OkObjectResult>(await PostAsync(Controller(1), "club-link", token: "cg247")).Value);
        Assert.Equal(own.BookId, club.BookId);
        Assert.Equal(1, club.ChapterIndex);
        Assert.Equal("Delmarko, Dietmar – Schwaz (2026)", club.Chapter);
        var lines = await _db.BookPuzzles.AsNoTracking().Where(p => p.Chapter == club.Chapter).ToListAsync();
        Assert.All(lines, l => { Assert.Null(l.SourceGame); Assert.StartsWith("personal-tactics-u1.pgn:club247:", l.LineId); });
        Assert.DoesNotContain(await _db.BookPuzzles.AsNoTracking().ToListAsync(), l => (l.Title + l.Chapter + l.Comment).Contains("Geheim"));

        // Der eigene Teilen-Link des Besitzers: die Linie führt zurück zur Partie.
        var mine = Assert.IsType<PersonalTacticsChapterDto>(Assert.IsType<OkObjectResult>(await PostAsync(Controller(1), "own-share", token: "tok11")).Value);
        Assert.All(await _db.BookPuzzles.AsNoTracking().Where(p => p.Chapter == mine.Chapter).ToListAsync(), l => Assert.Equal("own:11:0", l.SourceGame));
    }

    [Fact]
    public async Task FromGame_ClubGame_NeedsLeagueViewAndTheClub()
    {
        await SeedAsync();
        // Ohne league.view: 403, nichts angelegt.
        Assert.IsType<ForbidResult>(await PostAsync(Controller(3), "club", 247));
        // Mit dem Recht, aber ohne Verein: Absage des Vereins-Resolvers (403 noClub).
        var noClub = Assert.IsType<ObjectResult>(await PostAsync(Controller(3, false, Permissions.LeagueView), "club", 247));
        Assert.Equal(403, noClub.StatusCode);
        Assert.Empty(await _db.Books.ToListAsync());

        // Mitglied des Vereins (über seine Gruppe).
        _db.Groups.Add(new Group { Id = 50, Name = "Schwaz", CreatedAt = DateTime.UtcNow });
        _db.UserGroups.Add(new UserGroup { UserId = 3, GroupId = 50 });
        _db.LeagueClubMembers.Add(new LeagueClubMember { ClubId = 1, GroupId = 50 });
        await _db.SaveChangesAsync();
        var member = Controller(3, false, Permissions.LeagueView);
        var dto = Assert.IsType<PersonalTacticsChapterDto>(Assert.IsType<OkObjectResult>(await PostAsync(member, "club", 247)).Value);
        Assert.Equal("Delmarko, Dietmar – Schwaz (2026)", dto.Chapter);
        Assert.Equal("personal-tactics-u3.pgn", (await _db.Books.AsNoTracking().SingleAsync()).FileName);
        Assert.All(await _db.BookPuzzles.AsNoTracking().ToListAsync(), l => Assert.Equal("club:247:0", l.SourceGame));
        Assert.IsType<NotFoundResult>(await PostAsync(member, "club", 999));

        // Zweiter Klick: dasselbe Kapitel.
        var again = Assert.IsType<PersonalTacticsChapterDto>(Assert.IsType<OkObjectResult>(await PostAsync(member, "club", 247)).Value);
        Assert.False(again.Created);
        Assert.Equal((dto.BookId, dto.ChapterIndex), (again.BookId, again.ChapterIndex));
    }
}
