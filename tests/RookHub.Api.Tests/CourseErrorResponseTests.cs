using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Codereview 2026-09-29, A7-011: Kurs- und Kalkulations-Serien-Controller bildeten dieselbe Lage verschieden ab —
/// „nicht Besitzer" war bei PUT themes 403 mit eigenem Text, bei der Kalkulations-Serie 403 OHNE Rumpf
/// (<c>Forbid()</c>), und ein unbekanntes oder unlesbares Buch dort ebenfalls 403. Jetzt gilt EINE
/// Besitzer-oder-Admin-Regel (<see cref="CourseAccess.LoadManageableAsync"/>) mit EINER Antwort, und die
/// Controller fangen nichts mehr selbst (Domänen-Ausnahmen → DomainExceptionFilter).
///
/// <para>Zweiter Schritt (A7-011, Rest der Einheit): auch Repertoire-, Aufgabenblatt-, Kalkulations-, Kinder- und
/// Explorer-Controller fangen nichts mehr selbst, und keine ihrer Fehlerantworten kommt mehr ohne
/// <c>{ message }</c>.</para>
/// </summary>
public class CourseErrorResponseTests : IDisposable
{
    private const int OwnerId = 991101, ViewerId = 991102, UnknownBookId = 991199;
    private readonly AppDbContext _db;

    public CourseErrorResponseTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private static ControllerContext As(int userId, bool isAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (isAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
        };
    }

    private CourseController Courses(int userId) => new(TestServices.Course(_db), new CourseStatsService(_db),
        ReprocessTestHelper.Build(_db), new RecordingReprocessLauncher(), new CourseAuthoringService(_db),
        new FlashcardMarkService(_db), TestServices.Conversion(_db), new CoursePgnExportService(_db))
    { ControllerContext = As(userId) };

    private CalcSeriesController Series(int userId, bool isAdmin = false) =>
        new(new CalcEditionService(_db, TestServices.Friends(_db)), _db) { ControllerContext = As(userId, isAdmin) };

    private RepertoireController Repertoires(int userId)
    {
        var cache = TestServices.Cache();
        var service = TestServices.Repertoire(_db, cache);
        return new RepertoireController(service, ReprocessTestHelper.Build(_db), new RecordingReprocessLauncher(),
            new RepertoireTrainingService(_db), new SharedLineService(_db),
            new RepertoirePositionLookupService(new RepertoireLineSource(_db, cache), cache), new FlashcardMarkService(_db),
            new RepertoireSimilarityService(new RepertoireLineSource(_db, cache)), TestServices.Conversion(_db, repertoire: service))
        { ControllerContext = As(userId) };
    }

    private WorksheetController Worksheets(int userId) => new(new WorksheetService(_db)) { ControllerContext = As(userId) };

    private async Task<int> SeedBookAsync(bool isPublic)
    {
        var book = new Book
        {
            FileName = $"a7-011-{Guid.NewGuid():N}.pgn", DisplayName = "Serie", IsPublic = isPublic,
            IsCalculation = true, OwnerUserId = OwnerId, Source = new BookSource(),
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book.Id;
    }

    [Fact]
    public async Task NichtBesitzer_bekommtUeberallDasselbe403_mitRumpf()
    {
        var bookId = await SeedBookAsync(isPublic: true);   // lesbar für jeden, verwalten nur der Besitzer

        var themes = await DomainHttp.ResultAsync(async () => await Courses(ViewerId).SetThemes(bookId,
            new SetCourseThemesInputDto { Themes = new List<string> { "endgame" } }));
        var calculation = await DomainHttp.ResultAsync(async () => await Courses(ViewerId).SetCalculation(bookId,
            new SetCourseCalculationDto { IsCalculation = false }, default));
        var seriesManage = await DomainHttp.ResultAsync(async () => (await Series(ViewerId).ListManage(bookId, default)).Result);
        var seriesUpsert = await DomainHttp.ResultAsync(async () => (await Series(ViewerId).Upsert(bookId,
            new CalcEditionInputDto { Chapter = "Woche 1", PublishAt = DateTime.UtcNow }, default)).Result);

        foreach (var result in new[] { themes, calculation, seriesManage, seriesUpsert })
            DomainHttp.AssertError(result, 403, CourseAccess.ManageForbiddenMessage);
        Assert.False(await _db.CalcEditions.AnyAsync());
        Assert.True((await _db.Books.SingleAsync()).IsCalculation);
    }

    [Fact]
    public async Task UnbekanntesOderUnlesbaresBuch_ist404_auchInDerKalkulationsSerie()
    {
        var privateBookId = await SeedBookAsync(isPublic: false);

        foreach (var bookId in new[] { UnknownBookId, privateBookId })
        {
            DomainHttp.AssertError(await DomainHttp.ResultAsync(async () => await Courses(ViewerId).SetThemes(bookId,
                new SetCourseThemesInputDto { Themes = new List<string>() })), 404, "Book not found.");
            DomainHttp.AssertError(await DomainHttp.ResultAsync(async () =>
                (await Series(ViewerId).ListManage(bookId, default)).Result), 404, "Book not found.");
            DomainHttp.AssertError(await DomainHttp.ResultAsync(async () =>
                await Series(ViewerId).RemoveMember(bookId, OwnerId, default)), 404, "Book not found.");
        }
    }

    /// <summary>Admin vor einem Buch, das es nicht gibt: 404 wie für jeden — nicht 403 und nicht leer.</summary>
    [Fact]
    public async Task Admin_UnbekanntesBuch_ist404_inDerKalkulationsSerie()
    {
        DomainHttp.AssertError(await DomainHttp.ResultAsync(async () =>
            (await Series(ViewerId, isAdmin: true).ListManage(UnknownBookId, default)).Result), 404, "Book not found.");
        DomainHttp.AssertError(await DomainHttp.ResultAsync(async () => (await Series(ViewerId, isAdmin: true).Upsert(UnknownBookId,
            new CalcEditionInputDto { Chapter = "Woche 1", PublishAt = DateTime.UtcNow }, default)).Result), 404, "Book not found.");
        Assert.False(await _db.CalcEditions.AnyAsync());
    }

    [Fact]
    public async Task BesitzerUndAdmin_duerfenVerwalten()
    {
        var bookId = await SeedBookAsync(isPublic: false);

        Assert.IsType<OkObjectResult>((await Series(OwnerId).ListManage(bookId, default)).Result);
        Assert.IsType<OkObjectResult>((await Series(ViewerId, isAdmin: true).ListManage(bookId, default)).Result);
    }

    /// <summary>Auch die Null-Rückgaben der Controller tragen jetzt <c>{ message }</c> (vorher leerer 404).</summary>
    [Fact]
    public async Task NotFound_ohneRumpf_gibtEsNichtMehr()
    {
        var bookId = await SeedBookAsync(isPublic: false);

        DomainHttp.AssertError(await Courses(ViewerId).GetFlashcardMarks(bookId, default), 404, "Book not found.");
        DomainHttp.AssertError(await Courses(ViewerId).MarkFlashcard(bookId, 1, default), 404, "Line not found.");
        DomainHttp.AssertError(await Series(OwnerId).Delete(bookId, 991198, default), 404, "Edition not found.");
        DomainHttp.AssertError(await Series(OwnerId).RemoveMember(bookId, ViewerId, default), 404, "Member not found.");
        DomainHttp.AssertError(Assert.IsAssignableFrom<IActionResult>((await Series(ViewerId).ListVisible(bookId, default)).Result),
            404, "Book not found.");
    }

    /// <summary>Repertoire: Trainer, Flashcards, Teilen-Link und Stellungssuche antworteten mit leerem 404/400.</summary>
    [Fact]
    public async Task Repertoire_FehlerantwortenOhneRumpf_gibtEsNichtMehr()
    {
        const int unknownRepertoireId = 991197;
        var c = Repertoires(ViewerId);

        DomainHttp.AssertError(await c.GetFlashcardMarks(unknownRepertoireId, default), 404, "Repertoire not found.");
        DomainHttp.AssertError(await c.MarkFlashcard(unknownRepertoireId, "k1", default), 404, "Repertoire not found.");
        DomainHttp.AssertError(Assert.IsAssignableFrom<IActionResult>((await c.TrainingLines(unknownRepertoireId, default)).Result),
            404, "Repertoire not found.");
        DomainHttp.AssertError(Assert.IsAssignableFrom<IActionResult>((await c.TrainingConfig(unknownRepertoireId, default)).Result),
            404, "Repertoire not found.");
        DomainHttp.AssertError(await c.TrainingPause(unknownRepertoireId, new SetPausedRequest(), default), 404, "Repertoire not found.");
        DomainHttp.AssertError(Assert.IsAssignableFrom<IActionResult>((await c.TrainingReset(unknownRepertoireId, default)).Result),
            404, "Repertoire not found.");
        DomainHttp.AssertError(Assert.IsAssignableFrom<IActionResult>((await c.ShareLine(unknownRepertoireId,
            new ShareLineInputDto { Pgn = "1. e4 e5 *" }, default)).Result), 404, "Repertoire not found.");
        DomainHttp.AssertError(Assert.IsAssignableFrom<IActionResult>((await c.GetSharedLine("a7-011-unbekannt", default)).Result),
            404, "Shared line not found.");
        DomainHttp.AssertError(Assert.IsAssignableFrom<IActionResult>((await c.PositionLookup(
            new PositionLookupRequestDto { Fen = " " }, default)).Result), 400, "FEN is required.");
        DomainHttp.AssertError(await c.SetUserSrConfig(new SetSrConfigRequest { Levels = new List<SrLevelDto>() }, default),
            400, "Invalid SR levels.");
        DomainHttp.AssertError(Assert.IsAssignableFrom<IActionResult>((await c.TrainingLineReview(unknownRepertoireId,
            new LineReviewRequest { LineKey = " " }, default)).Result), 400, "Line key is required.");
        // Die Dienst-Fehler kommen über den Filter mit derselben Form (vorher je Action von Hand gefangen).
        DomainHttp.AssertError(await DomainHttp.ResultAsync(async () => await c.GetCombinedPgn(unknownRepertoireId)),
            404, "Repertoire not found.");
    }

    /// <summary>Aufgabenblätter: alle elf Null-Rückgaben waren ein leerer 404.</summary>
    [Fact]
    public async Task Aufgabenblatt_FehlerantwortenOhneRumpf_gibtEsNichtMehr()
    {
        const int unknownSheetId = 991196;
        var c = Worksheets(ViewerId);

        DomainHttp.AssertError(Assert.IsAssignableFrom<IActionResult>((await c.Get(unknownSheetId)).Result), 404, "Worksheet not found.");
        DomainHttp.AssertError(await c.Delete(unknownSheetId), 404, "Worksheet not found.");
        DomainHttp.AssertError(Assert.IsAssignableFrom<IActionResult>((await c.Share(unknownSheetId)).Result), 404, "Worksheet not found.");
        DomainHttp.AssertError(await c.DeleteItem(unknownSheetId, 1), 404, "Item not found.");
        DomainHttp.AssertError(Assert.IsAssignableFrom<IActionResult>((await c.Shared("a7-011-unbekannt")).Result),
            404, "Shared worksheet not found.");
    }

    /// <summary>Repertoire-Upload und „→ Kurs umwandeln" fingen InvalidOperationException → 400 — damit auch einen
    /// verworfenen DbContext, ohne Log. Jetzt läuft der echte Fehler durch (im Betrieb 500 + Error-Log).</summary>
    [Fact]
    public async Task EchterFehler_wirdAuchImRepertoireNichtMehrZu400()
    {
        var controller = Repertoires(ViewerId);
        var bytes = System.Text.Encoding.UTF8.GetBytes("[Event \"x\"]\n\n1. e4 e5 *");
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "a.pgn");
        _db.Dispose();

        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => DomainHttp.ResultAsync(async () =>
            await controller.ConvertToCourse(991195)));
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => DomainHttp.ResultAsync(async () =>
            (await controller.UploadFile(991195, file)).Result));
    }

    /// <summary>Vorher: catch (InvalidOperationException ex) → 400 „Cannot access a disposed context instance…",
    /// ohne Log. Der Controller fängt nichts mehr — der echte Fehler läuft zum globalen Handler (500 + Error-Log).</summary>
    [Fact]
    public async Task EchterFehler_wirdNichtMehrZu400()
    {
        var controller = Courses(ViewerId);
        _db.Dispose();

        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => DomainHttp.ResultAsync(async () =>
            (await controller.Create(file: null, name: "Neuer Kurs")).Result));
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => DomainHttp.ResultAsync(async () =>
            await controller.Link(991103, new LinkCourseInputDto { LinkedBookId = 991104 })));
    }
}
