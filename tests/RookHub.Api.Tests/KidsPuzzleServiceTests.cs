using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace RookHub.Api.Tests;

/// <summary>
/// Daten der Kinderseite (<see cref="KidsPuzzleService"/>, <see cref="KidsController"/>): Aufbau der
/// Stufen-Leiter aus dem Standard-Puzzle-Bestand und die fuer Kinder freigegebenen Kurse.
/// </summary>
public class KidsPuzzleServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly KidsPuzzleService _service;

    public KidsPuzzleServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new KidsPuzzleService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static string FenWith(int pieces)
    {
        var letters = "kK" + new string('P', Math.Max(0, pieces - 2));
        var ranks = Enumerable.Range(0, 8)
            .Select(r => letters.Length > r * 8 ? letters.Substring(r * 8, Math.Min(8, letters.Length - r * 8)) : "")
            .Select(row => row.Length == 8 ? row : row + (8 - row.Length));
        return string.Join('/', ranks) + " w - - 0 1";
    }

    /// <summary>Legt <paramref name="count"/> leichte Matt-in-1-Puzzles an, plus Ballast, der nicht in
    /// Frage kommt (zu schwer, zu voll, zu lang).</summary>
    private async Task SeedPuzzlesAsync(int count = 200)
    {
        for (var i = 0; i < count; i++)
        {
            _db.Puzzles.Add(new Puzzle
            {
                LichessId = $"M{i:D5}", Fen = FenWith(4 + i % 6), Moves = "a2a3 b2b3",
                Rating = 600 + i % 250, RatingDeviation = 80, Popularity = 95, NbPlays = 500,
                Themes = "mate mateIn1 oneMove",
            });
        }
        _db.Puzzles.Add(new Puzzle { LichessId = "HARD1", Fen = FenWith(4), Moves = "a2a3 b2b3",
            Rating = 1500, RatingDeviation = 80, Popularity = 95, NbPlays = 500, Themes = "mateIn1" });
        _db.Puzzles.Add(new Puzzle { LichessId = "FULL1", Fen = FenWith(20), Moves = "a2a3 b2b3",
            Rating = 600, RatingDeviation = 80, Popularity = 95, NbPlays = 500, Themes = "mateIn1" });
        _db.Puzzles.Add(new Puzzle { LichessId = "LONG1", Fen = FenWith(4), Moves = "a2a3 b2b3 c7c6 b3b4 d7d6 b4b5",
            Rating = 600, RatingDeviation = 80, Popularity = 95, NbPlays = 500, Themes = "mateIn3" });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Rebuild_BautStufenAusDenLeichtestenPuzzles()
    {
        await SeedPuzzlesAsync();

        var result = await _service.RebuildAsync();

        Assert.True(result.Levels > 0);
        Assert.Equal(result.Puzzles, await _db.KidsPuzzles.CountAsync());
        var excluded = await _db.Puzzles.Where(p => p.LichessId == "HARD1" || p.LichessId == "FULL1" || p.LichessId == "LONG1")
            .Select(p => p.Id).ToListAsync();
        Assert.False(await _db.KidsPuzzles.AnyAsync(k => excluded.Contains(k.PuzzleId)));
        Assert.True(await _service.IsCurrentAsync());
    }

    [Fact]
    public async Task Rebuild_ErsetztDieAlteLeiter()
    {
        await SeedPuzzlesAsync();
        var firstId = await _db.Puzzles.Select(p => p.Id).FirstAsync();
        _db.KidsPuzzles.Add(new KidsPuzzle { PuzzleId = firstId, Level = 99, Theme = "old", CurriculumVersion = 0 });
        await _db.SaveChangesAsync();
        Assert.False(await _service.IsCurrentAsync());

        await _service.RebuildAsync();

        Assert.False(await _db.KidsPuzzles.AnyAsync(k => k.Level == 99 || k.CurriculumVersion != KidsCurriculum.Version));
        Assert.True(await _service.IsCurrentAsync());
    }

    [Fact]
    public async Task IsCurrent_LeereLeiterIstNichtAktuell() =>
        Assert.False(await _service.IsCurrentAsync());

    [Fact]
    public async Task Rebuild_OhneBestandBleibtDieLeiterLeer()
    {
        var result = await _service.RebuildAsync();
        Assert.Equal(0, result.Puzzles);
        Assert.Empty(await _service.GetLevelsAsync());
    }

    [Fact]
    public async Task Levels_InReihenfolgeMitThemaUndAnzahl()
    {
        await SeedPuzzlesAsync();
        await _service.RebuildAsync();

        var levels = await _service.GetLevelsAsync();

        Assert.Equal(Enumerable.Range(1, levels.Count), levels.Select(l => l.Level));
        Assert.All(levels, l => Assert.Equal("mate1", l.Theme));
        Assert.All(levels, l => Assert.Equal(KidsCurriculum.PuzzlesPerLevel, l.PuzzleCount));
    }

    [Fact]
    public async Task Level_LiefertAufgabenLeichtesteZuerst()
    {
        await SeedPuzzlesAsync();
        await _service.RebuildAsync();

        var level = await _service.GetLevelAsync(1);

        Assert.NotNull(level);
        Assert.Equal("mate1", level!.Theme);
        Assert.Equal(KidsCurriculum.PuzzlesPerLevel, level.Puzzles.Count);
        var pieces = level.Puzzles.Select(p => KidsCurriculum.CountPieces(p.Fen)).ToList();
        Assert.True(pieces.First() <= pieces.Last());
        Assert.All(level.Puzzles, p => Assert.Equal("a2a3 b2b3", p.Moves));
    }

    [Fact]
    public async Task Level_UnbekannteStufeIst404()
    {
        var controller = new KidsController(_service);
        var result = await controller.GetLevel(42, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ---- Kinderkurse ----------------------------------------------------------------------------

    private async Task<Book> AddBookAsync(string name, bool forKids, bool isCalculation = false)
    {
        var book = new Book
        {
            FileName = name + ".pgn", DisplayName = name, ForKids = forKids, IsCalculation = isCalculation,
            Source = new BookSource(),
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    private async Task AddLineAsync(Book book, string lineId, bool infoOnly = false, string round = "001")
    {
        _db.BookPuzzles.Add(new BookPuzzle
        {
            LineId = lineId, BookFileName = book.FileName, BookId = book.Id, Round = round,
            Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", Moves = "e7e5 d2d4",
            IsInfoOnly = infoOnly,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Courses_NurFreigegebeneOhneKalkulationsbuecher()
    {
        var kids = await AddBookAsync("Kinderkurs", forKids: true);
        await AddLineAsync(kids, "k1");
        await AddLineAsync(kids, "k2");
        await AddLineAsync(kids, "k-info", infoOnly: true);
        var normal = await AddBookAsync("Erwachsene", forKids: false);
        await AddLineAsync(normal, "n1");
        var calc = await AddBookAsync("Rechnen", forKids: true, isCalculation: true);
        await AddLineAsync(calc, "c1");
        var empty = await AddBookAsync("Nur Info", forKids: true);
        await AddLineAsync(empty, "e-info", infoOnly: true);

        var courses = await _service.GetCoursesAsync();

        var only = Assert.Single(courses);
        Assert.Equal(kids.Id, only.BookId);
        Assert.Equal("Kinderkurs", only.Title);
        Assert.Equal(2, only.PuzzleCount);
    }

    [Fact]
    public async Task CoursePuzzles_OhneInfoLinienInLesereihenfolge()
    {
        var kids = await AddBookAsync("Kinderkurs", forKids: true);
        await AddLineAsync(kids, "second", round: "002");
        await AddLineAsync(kids, "info", infoOnly: true, round: "001.5");
        await AddLineAsync(kids, "first", round: "001");

        var lines = await _service.GetCoursePuzzlesAsync(kids.Id);

        Assert.Equal(new[] { "first", "second" }, lines.Select(l => l.LineId));
    }

    [Fact]
    public async Task CoursePuzzles_NichtFreigegebenIst404()
    {
        var normal = await AddBookAsync("Erwachsene", forKids: false);
        await AddLineAsync(normal, "n1");
        var calc = await AddBookAsync("Rechnen", forKids: true, isCalculation: true);
        await AddLineAsync(calc, "c1");

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetCoursePuzzlesAsync(normal.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetCoursePuzzlesAsync(calc.Id));

        var controller = new KidsController(_service);
        var result = await controller.GetCoursePuzzles(normal.Id, null, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task BookAdmin_SchaltetForKids()
    {
        var book = await AddBookAsync("Kinderkurs", forKids: false);
        var admin = new BookAdminService(_db);

        var updated = await admin.UpdateBookAsync(book.Id, new UpdateBookDto { ForKids = true });

        Assert.True(updated.ForKids);
        Assert.True((await admin.GetBooksAsync()).Single().ForKids);
        // Ein Update ohne das Feld laesst es stehen.
        var again = await admin.UpdateBookAsync(book.Id, new UpdateBookDto { DisplayName = "Neu" });
        Assert.True(again.ForKids);
    }

    // ---- Kindertitel + „erst, wenn Deutsch" (Wunsch 2026-09-27) ----

    [Fact]
    public async Task Kurse_ErscheinenErst_WennSieDeutschSind()
    {
        var english = await AddBookAsync("Learn Chess", forKids: true);
        english.CommentLanguage = "en";
        await AddLineAsync(english, "e1");
        var german = await AddBookAsync("Mattbilder", forKids: true);
        german.CommentLanguage = "de";
        await AddLineAsync(german, "g1");
        await _db.SaveChangesAsync();
        var service = new KidsPuzzleService(_db, new[] { "de" });

        Assert.Equal(new[] { german.Id }, (await service.GetCoursesAsync()).Select(c => c.BookId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetCoursePuzzlesAsync(english.Id));

        // Uebersetzung laeuft: weiter unsichtbar.
        _db.CourseTranslationJobs.Add(new CourseTranslationJob { BookId = english.Id, Language = "de", Status = CourseTranslationJobStatus.Running, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        Assert.Single(await service.GetCoursesAsync());

        // Fertig uebersetzt: sichtbar, auch ueber den direkten Weg.
        _db.CourseTranslationJobs.Add(new CourseTranslationJob { BookId = english.Id, Language = "de", Status = CourseTranslationJobStatus.Done, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        Assert.Equal(2, (await service.GetCoursesAsync()).Count);
        Assert.Single(await service.GetCoursePuzzlesAsync(english.Id));
    }

    [Fact]
    public async Task Kurse_OhneSprachbedingung_SofortSichtbar()
    {
        var english = await AddBookAsync("Learn Chess", forKids: true);
        english.CommentLanguage = "en";
        await AddLineAsync(english, "e1");
        await _db.SaveChangesAsync();

        Assert.Single(await new KidsPuzzleService(_db, Array.Empty<string>()).GetCoursesAsync());
    }

    [Theory]
    [InlineData("de", new[] { "de" })]
    [InlineData(" de , HR,de ", new[] { "de", "hr" })]
    [InlineData("", new string[0])]
    public void Sprachbedingung_AusDerKonfiguration(string value, string[] expected) =>
        Assert.Equal(expected, KidsPuzzleService.ParseLanguages(value));

    [Fact]
    public void Sprachbedingung_VorgabeIstDeutsch()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        Assert.Equal("de", KidsPuzzleService.DefaultRequiredCourseLanguages);
        Assert.Null(config["Kids:RequiredCourseLanguages"]);   // nicht gesetzt → Vorgabe
    }

    [Fact]
    public async Task Kindertitel_JeSprache_UndDieReihenfolgeFolgtIhnen()
    {
        var b1 = await AddBookAsync("Learn Chess the Right Way - Book 1", forKids: true);
        b1.KidsTitles = KidsTitles.Normalize(new Dictionary<string, string?> { ["de"] = "Matt in einem Zug", ["en"] = "Checkmate in One" });
        await AddLineAsync(b1, "a1");
        var b2 = await AddBookAsync("Knight Fork Trainer", forKids: true);
        b2.KidsTitles = KidsTitles.Normalize(new Dictionary<string, string?> { ["de"] = "Die Springergabel", ["en"] = "The Knight Fork" });
        await AddLineAsync(b2, "b1");
        await _db.SaveChangesAsync();

        Assert.Equal(new[] { "Die Springergabel", "Matt in einem Zug" }, (await _service.GetCoursesAsync("de")).Select(c => c.Title));
        Assert.Equal(new[] { "Checkmate in One", "The Knight Fork" }, (await _service.GetCoursesAsync("hu")).Select(c => c.Title));   // hu fehlt → en
        Assert.Equal("Matt in einem Zug", (await _service.GetCoursePuzzlesAsync(b1.Id, "de")).Single().BookTitle);
        b2.KidsTitles = null;                                           // ohne eigene Titel: der Buchname
        await _db.SaveChangesAsync();
        Assert.Contains("Knight Fork Trainer", (await _service.GetCoursesAsync("de")).Select(c => c.Title));
    }

    [Theory]
    [InlineData("de", "Matt")]
    [InlineData("hr", "Checkmate")]      // keine hr-Fassung → Englisch
    [InlineData(null, "Checkmate")]
    public void Kindertitel_Rueckfall(string? lang, string expected) =>
        Assert.Equal(expected, KidsTitles.Pick("{\"de\":\"Matt\",\"en\":\"Checkmate\"}", lang, "Buchname"));

    [Fact]
    public void Kindertitel_OhneEigene_DerBuchname()
    {
        Assert.Equal("Buchname", KidsTitles.Pick(null, "de", "Buchname"));
        Assert.Equal("Buchname", KidsTitles.Pick("{kaputt", "de", "Buchname"));
        Assert.Equal("Nur Deutsch", KidsTitles.Pick("{\"de\":\"Nur Deutsch\"}", "hu", "Buchname"));
    }

    [Fact]
    public void Kindertitel_Eingabe_WirdGeprueft()
    {
        Assert.Equal("{\"de\":\"Matt\",\"en\":\"Mate\"}",
            KidsTitles.Normalize(new Dictionary<string, string?> { ["EN"] = " Mate ", ["de"] = "Matt", ["hu"] = "  " }));
        Assert.Null(KidsTitles.Normalize(new Dictionary<string, string?> { ["de"] = "" }));
        Assert.Throws<ArgumentException>(() => KidsTitles.Normalize(new Dictionary<string, string?> { ["fr"] = "Mat" }));
        Assert.Throws<ArgumentException>(() => KidsTitles.Normalize(new Dictionary<string, string?> { ["de"] = new string('x', KidsTitles.MaxLength + 1) }));
    }

    [Fact]
    public async Task Buecherverwaltung_SpeichertKindertitel_OhneDieEloZuVerlieren()
    {
        var book = await AddBookAsync("Learn Chess", forKids: true);
        book.MinElo = 800; book.MaxElo = 1200;
        await _db.SaveChangesAsync();

        var dto = await new BookAdminService(_db).UpdateBookAsync(book.Id, new UpdateBookDto
        {
            KidsTitles = new Dictionary<string, string?> { ["de"] = "Matt in einem Zug" }, MinElo = 800, MaxElo = 1200,
        });

        Assert.Equal("Matt in einem Zug", dto.KidsTitles["de"]);
        Assert.Equal("{\"de\":\"Matt in einem Zug\"}", (await _db.Books.FindAsync(book.Id))!.KidsTitles);
    }
}
