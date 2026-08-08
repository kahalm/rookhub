using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// „Öffentlicher Kalkulations-Kurs": ein Kurz-Link (<c>/{slug}</c> bzw. <c>/{slug}/{kapitel}</c>) bringt
/// jemanden OHNE Konto direkt in den Kalkulations-Modus. Serverseitig gibt es dafür GENAU EINE Öffnung —
/// LESEND, und nur für ausdrücklich öffentlich freigegebene Bücher. Getestet wird:
/// <list type="bullet">
/// <item>die Slug-Auflösung liefert <c>isCalculation</c> (ohne das läuft der Link bei einem
/// Kalkulationsbuch in den Solver, der sofort „abgeschlossen" meldet — dessen Stellungen sind
/// Info-Linien und aus allen Quiz-Pools ausgeschlossen);</item>
/// <item>die Kapitel-Auflösung über den NAMEN (getrimmt/ohne Groß-Kleinschreibung), inkl. 404 und
/// dem Solver-Index-Kontrakt (<see cref="ChapterOrder"/>);</item>
/// <item>der anonyme Lesezugriff: privates Buch → 404, öffentliches → Stellungen, aber KEINE
/// Lösungszüge (<see cref="BookPuzzle.Moves"/>) und keine fremden Trainings-Werte.</item>
/// </list>
/// </summary>
public class PublicCalculationAccessTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseService _course;
    private readonly CalculationService _calc;
    private readonly CalculationController _calcController;

    public PublicCalculationAccessTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        var notifications = new NotificationService(_db);
        _course = new CourseService(_db, NullLogger<CourseService>.Instance, new PgnImportService(_db),
            new BookAdminService(_db),
            new RepertoireService(_db, new RepertoireAnalyzeService(_db,
                new Microsoft.Extensions.Caching.Memory.MemoryCache(
                    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()))),
            new FriendService(_db, notifications), notifications);
        _calc = new CalculationService(_db);
        _calcController = new CalculationController(_calc);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Book> SeedBookAsync(bool isPublic = true, bool isCalculation = false, string? slug = null,
        bool forDaily = false, bool forRandom = false, bool forBlind = false)
    {
        var book = new Book
        {
            FileName = $"book-{Guid.NewGuid():N}.pgn",
            DisplayName = "Kalkulation",
            IsPublic = isPublic,
            IsCalculation = isCalculation,
            PublicSlug = slug,
            ForDaily = forDaily,
            ForRandom = forRandom,
            ForBlind = forBlind,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    private async Task<BookPuzzle> SeedLineAsync(Book book, string round, string? chapter, bool infoOnly,
        string moves = "", int startPly = -1, string? comment = null)
    {
        var p = new BookPuzzle
        {
            LineId = $"{book.FileName}:{round}:{Guid.NewGuid():N}",
            BookFileName = book.FileName,
            BookId = book.Id,
            Round = round,
            Chapter = chapter,
            Fen = "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4",
            Moves = moves,
            StartPly = startPly,
            Comment = comment,
            IsInfoOnly = infoOnly,
            Title = $"Line {round}",
        };
        _db.BookPuzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    // ---------------------------------------------------------------- Slug → { bookId, isCalculation }

    [Fact]
    public async Task ResolvePublicSlug_ReportsIsCalculation_ForCalculationBook()
    {
        var book = await SeedBookAsync(isCalculation: true, slug: "noel");

        var target = await _course.ResolvePublicSlugAsync("NOEL");

        Assert.NotNull(target);
        Assert.Equal(book.Id, target!.BookId);
        Assert.True(target.IsCalculation);
    }

    [Fact]
    public async Task ResolvePublicSlug_ReportsNotCalculation_ForOrdinaryCourse()
    {
        var book = await SeedBookAsync(isCalculation: false, slug: "mate1");

        var target = await _course.ResolvePublicSlugAsync("mate1");

        Assert.NotNull(target);
        Assert.Equal(book.Id, target!.BookId);
        Assert.False(target.IsCalculation);
    }

    // ------------------------------------------- Slug + Kapitel → { …, chapter, chapterIndex }

    [Fact]
    public async Task ResolveChapter_ReturnsSolverIndex_ForQuizChapter()
    {
        var book = await SeedBookAsync(slug: "kurs");
        // Reines Info-Kapitel VORNE: es belegt im Solver keinen Index (ChapterOrder), sonst wäre
        // jedes folgende Kapitel um eins verschoben.
        await SeedLineAsync(book, "1", "Intro", infoOnly: true);
        await SeedLineAsync(book, "2", "KW45", infoOnly: false);
        await SeedLineAsync(book, "3", "KW46", infoOnly: false);

        var kw45 = await _course.ResolvePublicSlugChapterAsync("kurs", "KW45");
        var kw46 = await _course.ResolvePublicSlugChapterAsync("kurs", "KW46");

        Assert.Equal(0, kw45!.ChapterIndex);
        Assert.Equal("KW45", kw45.Chapter);
        Assert.Equal(1, kw46!.ChapterIndex);
        Assert.Equal(book.Id, kw46.BookId);
        Assert.False(kw46.IsCalculation);
    }

    [Fact]
    public async Task ResolveChapter_IgnoresCaseAndSurroundingWhitespace()
    {
        var book = await SeedBookAsync(slug: "kurs");
        await SeedLineAsync(book, "1", "KW46", infoOnly: false);

        foreach (var typed in new[] { "kw46", "KW46", " Kw46 " })
        {
            var res = await _course.ResolvePublicSlugChapterAsync("kurs", typed);
            Assert.NotNull(res);
            // Zurück kommt die Schreibweise aus dem BUCH, nicht die aus der URL.
            Assert.Equal("KW46", res!.Chapter);
            Assert.Equal(0, res.ChapterIndex);
        }
    }

    [Fact]
    public async Task ResolveChapter_UnknownChapter_ReturnsNull()
    {
        var book = await SeedBookAsync(slug: "kurs");
        await SeedLineAsync(book, "1", "KW46", infoOnly: false);

        Assert.Null(await _course.ResolvePublicSlugChapterAsync("kurs", "KW99"));
    }

    [Fact]
    public async Task ResolveChapter_UnknownSlug_ReturnsNull()
    {
        var book = await SeedBookAsync(isPublic: false, slug: "hidden");
        await SeedLineAsync(book, "1", "KW46", infoOnly: false);

        Assert.Null(await _course.ResolvePublicSlugChapterAsync("hidden", "KW46"));   // nicht öffentlich
        Assert.Null(await _course.ResolvePublicSlugChapterAsync("nope", "KW46"));     // unbekannter Alias
    }

    [Fact]
    public async Task ResolveChapter_CalculationBook_HasNoSolverIndex()
    {
        // Kalkulationsbuch: ALLE Stellungen sind Info-Linien — der Kapitel-Filter läuft dort über den NAMEN.
        var book = await SeedBookAsync(isCalculation: true, slug: "noel");
        await SeedLineAsync(book, "1", "KW46", infoOnly: true);

        var res = await _course.ResolvePublicSlugChapterAsync("noel", "kw46");

        Assert.NotNull(res);
        Assert.Equal(book.Id, res!.BookId);
        Assert.True(res.IsCalculation);
        Assert.Equal("KW46", res.Chapter);
        Assert.Null(res.ChapterIndex);
    }

    [Fact]
    public async Task ResolveChapter_InfoOnlyChapter_ResolvesWithoutSolverIndex()
    {
        var book = await SeedBookAsync(slug: "kurs");
        await SeedLineAsync(book, "1", "Intro", infoOnly: true);
        await SeedLineAsync(book, "2", "KW46", infoOnly: false);

        var res = await _course.ResolvePublicSlugChapterAsync("kurs", "intro");

        Assert.NotNull(res);
        Assert.Equal("Intro", res!.Chapter);
        Assert.Null(res.ChapterIndex);   // im Solver nicht startbar
    }

    // ---------------------------------------------------- anonymer Lesezugriff auf die Stellungen

    [Fact]
    public async Task GetPublicBook_Throws_ForPrivateBook()
    {
        var book = await SeedBookAsync(isPublic: false, isCalculation: true);
        await SeedLineAsync(book, "1", "KW46", infoOnly: true);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _calc.GetPublicBookAsync(book.Id));
    }

    [Fact]
    public async Task GetPublicBook_Controller_Returns404_ForPrivateBook()
    {
        var book = await SeedBookAsync(isPublic: false, isCalculation: true);
        await SeedLineAsync(book, "1", "KW46", infoOnly: true);

        var res = await _calcController.GetPublicBook(book.Id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(res.Result);
    }

    [Fact]
    public async Task GetPublicBook_Throws_ForMissingBook()
        => await Assert.ThrowsAsync<KeyNotFoundException>(() => _calc.GetPublicBookAsync(9999));

    [Fact]
    public async Task GetPublicBook_ReturnsPositions_ForPublicCalculationBook()
    {
        var book = await SeedBookAsync(isCalculation: true, slug: "noel");
        await SeedLineAsync(book, "2", "KW46", infoOnly: true, comment: "Weiß am Zug");
        await SeedLineAsync(book, "1", "KW45", infoOnly: true);

        var dto = await _calc.GetPublicBookAsync(book.Id);

        Assert.Equal(book.Id, dto.BookId);
        Assert.True(dto.IsCalculation);
        Assert.Equal(2, dto.Positions.Count);
        Assert.Equal(new[] { "1", "2" }, dto.Positions.Select(p => p.Round));   // Lesereihenfolge Round→Id
        Assert.Equal("KW46", dto.Positions[1].Chapter);
        Assert.Equal("Weiß am Zug", dto.Positions[1].Comment);
        Assert.All(dto.Positions, p => Assert.False(string.IsNullOrWhiteSpace(p.Fen)));
    }

    [Fact]
    public async Task GetPublicBook_NeverShipsSolutionMoves_OnlySetupUpToStartPly()
    {
        var book = await SeedBookAsync(isCalculation: true);
        // Reine Stellung (Kalkulationsbuch-Muster): Züge gespeichert, aber kein Trainingsstart.
        await SeedLineAsync(book, "1", "KW46", infoOnly: true, moves: "e2e4 e7e5 g1f3", startPly: -1);
        // Puzzle-Linie mit Trainingsstart mitten in der Partie: Vorlauf ja, Lösung nein.
        await SeedLineAsync(book, "2", "KW46", infoOnly: true, moves: "d2d4 d7d5 c2c4 e7e6", startPly: 1);

        var dto = await _calc.GetPublicBookAsync(book.Id);

        Assert.Equal(string.Empty, dto.Positions[0].SetupMoves);          // StartPly < 0 ⇒ gar keine Züge
        Assert.Equal("d2d4 d7d5", dto.Positions[1].SetupMoves);           // 0..StartPly, nicht weiter

        // Und in der SERIALISIERTEN Antwort taucht kein Lösungszug auf.
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.DoesNotContain("e2e4", json);
        Assert.DoesNotContain("g1f3", json);
        Assert.DoesNotContain("c2c4", json);
        Assert.DoesNotContain("e7e6", json);
    }

    [Fact]
    public void CalcPublicPositionDto_HasNoMovesProperty()
    {
        // Der Architektur-Vertrag des Kalkulations-Modus: BookPuzzle.Moves hat im anonymen DTO
        // keine Entsprechung — es gibt nichts, worüber die Lösung versehentlich durchrutschen könnte.
        var names = typeof(CalcPublicPositionDto).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("Moves", names);
        Assert.DoesNotContain("StartPly", names);
        Assert.Contains("SetupMoves", names);
    }

    [Fact]
    public async Task GetPublicBook_ShipsNoForeignTrainingValues()
    {
        var book = await SeedBookAsync(isCalculation: true);
        var line = await SeedLineAsync(book, "1", "KW46", infoOnly: true);
        var user = new AppUser { Username = "other", PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        _db.CalculationTrees.Add(new CalculationTree
        {
            UserId = user.Id,
            BookId = book.Id,
            BookPuzzleId = line.Id,
            TreeJson = "{\"secretIdea\":\"Turmopfer\"}",
            ChosenSan = "Rxh7",
            ChosenUci = "h1h7",
            SecondsSpent = 4711,
            Grade = 4,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var dto = await _calc.GetPublicBookAsync(book.Id);
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.DoesNotContain("Turmopfer", json);
        Assert.DoesNotContain("Rxh7", json);
        Assert.DoesNotContain("4711", json);
        foreach (var leak in new[] { "treeJson", "chosenSan", "chosenUci", "secondsSpent", "grade", "points", "hasTree" })
            Assert.DoesNotContain(leak, json);
    }

    // ------------------------------------------------ das Tor: NUR ausdrücklich öffentlich freigegeben

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task GetPublicBook_Throws_ForPoolFlaggedButNotPublicBook(bool forDaily, bool forRandom, bool forBlind)
    {
        // Persönlicher Import, den ein Admin in einen offenen Pool gelegt hat: die Pool-Flags öffnen
        // EINZELNE Puzzles/Zufallsziehungen (siehe BookAccess), NICHT den strukturierten Kurs. Der
        // Einstieg /{slug} verlangt IsPublic — also verlangt der Inhalt es auch.
        var book = await SeedBookAsync(isPublic: false, isCalculation: true,
            forDaily: forDaily, forRandom: forRandom, forBlind: forBlind);
        await SeedLineAsync(book, "1", "KW46", infoOnly: true, comment: "geheime Hausaufgabe");

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _calc.GetPublicBookAsync(book.Id));
        Assert.IsType<NotFoundObjectResult>(
            (await _calcController.GetPublicBook(book.Id, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task GetPublicBook_Works_ForPublicBook_EvenWithoutPoolFlags()
    {
        // Gegenprobe zum Theory oben: das Tor ist IsPublic, nicht die Pool-Mitgliedschaft.
        var book = await SeedBookAsync(isPublic: true, isCalculation: true, slug: "noel");
        await SeedLineAsync(book, "1", "KW46", infoOnly: true);

        var dto = await _calc.GetPublicBookAsync(book.Id);

        Assert.Equal(book.Id, dto.BookId);
        Assert.Single(dto.Positions);
    }

    // ---------------------------- Nachbar-Endpoint: der SOLVER-Pfad liefert Kalkulationsbücher nicht

    [Fact]
    public async Task GetPublicCoursePuzzles_Throws_ForPublicCalculationBook()
    {
        // Genau die Konstellation, die ein öffentlicher Kalkulations-Kurs BRAUCHT: ohne IsPublic
        // löst /{slug} nicht auf. Dasselbe Flag ist das einzige Tor des anonymen Solver-Endpoints —
        // der reicht über BookPuzzleService.MapToDto die volle Zugfolge durch.
        var book = await SeedBookAsync(isCalculation: true, slug: "noel");
        await SeedLineAsync(book, "1", "KW46", infoOnly: true, moves: "e1g1 g8f6 d2d4 e5d4");

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _course.GetPublicCoursePuzzlesAsync(book.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _course.GetPublicCoursePuzzlesAsync(book.Id, skip: 0, take: 1));
    }

    [Fact]
    public async Task GetPublicCoursePuzzles_StillShipsMoves_ForOrdinaryPublicCourse()
    {
        // Keine Regression: ein normaler öffentlicher Kurs ist ein Solver-Kurs und liefert weiter Züge.
        var book = await SeedBookAsync(isCalculation: false, slug: "mate1");
        await SeedLineAsync(book, "1", "KW46", infoOnly: false, moves: "e1g1 g8f6 d2d4 e5d4");

        var puzzles = await _course.GetPublicCoursePuzzlesAsync(book.Id);

        Assert.Single(puzzles);
        Assert.Equal("e1g1 g8f6 d2d4 e5d4", puzzles[0].Moves);
    }

    [Fact]
    public async Task GetAllPuzzles_Throws_ForCalculationBook()
    {
        // Offline-Export: derselbe MapToDto-Pfad. „Öffentlich" heißt Kurs-Zugriff für JEDEN
        // angemeldeten Nutzer (CourseAccess) — der Voll-Export wäre die Lösung.
        var book = await SeedBookAsync(isCalculation: true, slug: "noel");
        await SeedLineAsync(book, "1", "KW46", infoOnly: true, moves: "e1g1 g8f6 d2d4 e5d4");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _course.GetAllPuzzlesAsync(userId: 42, book.Id, isAdmin: false));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _course.GetAllPuzzlesAsync(userId: 1, book.Id, isAdmin: true));
    }

    [Fact]
    public async Task GetAllPuzzles_StillShipsMoves_ForOrdinaryCourse()
    {
        var book = await SeedBookAsync(isCalculation: false, slug: "mate1");
        await SeedLineAsync(book, "1", "KW46", infoOnly: false, moves: "e1g1 g8f6");

        var puzzles = await _course.GetAllPuzzlesAsync(userId: 42, book.Id, isAdmin: false);

        Assert.Single(puzzles);
        Assert.Equal("e1g1 g8f6", puzzles[0].Moves);
    }

    [Fact]
    public async Task NextAndRandomInBook_Throw_ForCalculationBook()
    {
        // Das anonyme Buch-Durchlaufen hängt am selben BookAccess-Tor wie der öffentliche
        // Kurs-Endpoint: ohne Sperre bliebe dieselbe Lücke offen, nur eine Linie nach der anderen.
        var book = await SeedBookAsync(isCalculation: true, slug: "noel");
        var first = await SeedLineAsync(book, "1", "KW46", infoOnly: true, moves: "e1g1 g8f6 d2d4 e5d4");
        await SeedLineAsync(book, "2", "KW46", infoOnly: true, moves: "d2d4 d7d5");
        var puzzles = new BookPuzzleService(_db, NullLogger<BookPuzzleService>.Instance, new NoOpTaskQueue());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => puzzles.GetNextInBookAsync(first.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => puzzles.GetRandomInBookAsync(first.Id));
    }

    [Fact]
    public async Task NextInBook_StillWorks_ForOrdinaryPublicBook()
    {
        var book = await SeedBookAsync(isCalculation: false);
        var first = await SeedLineAsync(book, "1", "KW46", infoOnly: false, moves: "e1g1 g8f6");
        var second = await SeedLineAsync(book, "2", "KW46", infoOnly: false, moves: "d2d4 d7d5");
        var puzzles = new BookPuzzleService(_db, NullLogger<BookPuzzleService>.Instance, new NoOpTaskQueue());

        var next = await puzzles.GetNextInBookAsync(first.Id);

        Assert.Equal(second.Id, next.Id);
        Assert.Equal("d2d4 d7d5", next.Moves);
    }
}
