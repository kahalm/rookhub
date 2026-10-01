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
