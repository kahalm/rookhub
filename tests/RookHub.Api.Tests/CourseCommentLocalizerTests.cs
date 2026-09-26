using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
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
/// Ausliefern ueber <c>?lang=</c> (<see cref="CourseCommentLocalizer"/>): ersetzt wird nur, wo der
/// Fingerabdruck zum Original im DTO passt; Titel und Kapitel bleiben (Schluessel), die Uebersetzung kommt
/// als Label; ohne <c>lang</c> bleibt das DTO exakt, wie es war.
/// </summary>
public class CourseCommentLocalizerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseCommentLocalizer _localizer;

    public CourseCommentLocalizerTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _localizer = new CourseCommentLocalizer(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Book> BookAsync(string? lang = "en", bool isPublic = false)
    {
        var book = new Book
        {
            FileName = $"b-{Guid.NewGuid():N}.pgn", DisplayName = "Kurs", CommentLanguage = lang, IsPublic = isPublic,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource(),
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    private async Task<BookPuzzle> LineAsync(Book book, string round = "001", string? title = "Titel",
        string? chapter = "Kapitel", string? comment = "Kommentar", Dictionary<int, string>? moves = null,
        bool infoOnly = false)
    {
        var line = new BookPuzzle
        {
            LineId = $"{book.FileName}:{round}", BookFileName = book.FileName, BookId = book.Id, Round = round,
            Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", Moves = infoOnly ? "" : "e2e4 e7e5",
            Title = title, Chapter = chapter, Comment = comment, IsInfoOnly = infoOnly,
            MoveComments = JsonSerializer.Serialize(moves ?? new Dictionary<int, string> { [-1] = "Einleitung", [0] = "Zug" }),
        };
        _db.BookPuzzles.Add(line);
        await _db.SaveChangesAsync();
        return line;
    }

    /// <summary>Ein Kurs-Satz; je Stelle (Uebersetzung, Vorlage) — die Vorlage bestimmt den Fingerabdruck.</summary>
    private async Task SetAsync(BookPuzzle line, string lang, CommentOrigin origin,
        params (int Slot, string Text, string Source)[] texts)
    {
        var set = new CommentSet { BookPuzzleId = line.Id, Language = lang, Origin = origin, TranslatedFrom = "en" };
        foreach (var (slot, text, source) in texts)
            set.Texts.Add(new CommentText { Ply = slot, Text = text, SourceHash = CourseTextHash.Of(source) });
        _db.CommentSets.Add(set);
        await _db.SaveChangesAsync();
    }

    private async Task<BookPuzzleDto> DtoAsync(int id)
        => BookPuzzleService.MapToDto(await _db.BookPuzzles.Include(bp => bp.Book).SingleAsync(bp => bp.Id == id));

    private async Task<BookPuzzle> FullyTranslatedAsync(Book book)
    {
        var line = await LineAsync(book);
        await SetAsync(line, "de", CommentOrigin.Machine,
            (CourseTextSlots.Comment, "Kommentar DE", "Kommentar"),
            (CourseTextSlots.Intro, "Einleitung DE", "Einleitung"),
            (0, "Zug DE", "Zug"),
            (CourseTextSlots.Title, "Titel DE", "Titel"),
            (CourseTextSlots.Chapter, "Kapitel DE", "Kapitel"));
        return line;
    }

    [Fact]
    public async Task Apply_OhneLang_DtoExaktUnveraendert()
    {
        var line = await FullyTranslatedAsync(await BookAsync());
        var dto = await DtoAsync(line.Id);
        var before = JsonSerializer.Serialize(dto);

        await _localizer.ApplyAsync(new List<BookPuzzleDto> { dto }, null);
        await _localizer.ApplyAsync(new List<BookPuzzleDto> { dto }, "   ");

        Assert.Equal(before, JsonSerializer.Serialize(dto));
    }

    [Theory]
    [InlineData("deutsch-lang")]
    [InlineData("d")]
    [InlineData("de'; --")]
    public async Task Apply_UngueltigesLang_WirdIgnoriert(string lang)
    {
        var line = await FullyTranslatedAsync(await BookAsync());
        var dto = await DtoAsync(line.Id);
        var before = JsonSerializer.Serialize(dto);

        await _localizer.ApplyAsync(dto, lang);

        Assert.Equal(before, JsonSerializer.Serialize(dto));
    }

    [Fact]
    public async Task Apply_Uebersetzt_TitelUndKapitelBleiben_LabelsGesetzt()
    {
        var line = await FullyTranslatedAsync(await BookAsync());
        var dto = await DtoAsync(line.Id);

        await _localizer.ApplyAsync(dto, "DE");

        Assert.Equal("Kommentar DE", dto.Comment);
        Assert.Equal("Einleitung DE", dto.MoveComments![-1]);
        Assert.Equal("Zug DE", dto.MoveComments[0]);
        Assert.Equal("Titel", dto.Title);          // Schluessel bleibt
        Assert.Equal("Kapitel", dto.Chapter);      // Schluessel bleibt
        Assert.Equal("Titel DE", dto.TitleLabel);
        Assert.Equal("Kapitel DE", dto.ChapterLabel);
        Assert.Equal("de", dto.CommentLanguage);
        Assert.True(dto.CommentMachine);
        Assert.Equal(new[] { "en", "de" }, dto.CommentLanguages);   // Quelle zuerst
    }

    /// <summary>Hat die Aufbereitung einen Kommentar geaendert, passt der Fingerabdruck nicht mehr — dort kommt
    /// das Original, der Rest bleibt uebersetzt.</summary>
    [Fact]
    public async Task Apply_VeralteterText_OriginalBleibt()
    {
        var line = await LineAsync(await BookAsync(), comment: "Neuer Kommentar");
        await SetAsync(line, "de", CommentOrigin.Machine,
            (CourseTextSlots.Comment, "Alter Kommentar DE", "Alter Kommentar"),
            (0, "Zug DE", "Zug"));
        var dto = await DtoAsync(line.Id);

        await _localizer.ApplyAsync(dto, "de");

        Assert.Equal("Neuer Kommentar", dto.Comment);
        Assert.Equal("Zug DE", dto.MoveComments![0]);
        Assert.Equal("Einleitung", dto.MoveComments[-1]);   // gab es gar nicht uebersetzt
        Assert.Null(dto.TitleLabel);
        Assert.Equal("de", dto.CommentLanguage);
    }

    [Fact]
    public async Task Apply_SpracheOhneSatz_OriginalMitQuellsprache()
    {
        var line = await FullyTranslatedAsync(await BookAsync());
        var dto = await DtoAsync(line.Id);

        await _localizer.ApplyAsync(dto, "fr");

        Assert.Equal("Kommentar", dto.Comment);
        Assert.Null(dto.ChapterLabel);
        Assert.Equal("en", dto.CommentLanguage);
        Assert.False(dto.CommentMachine);
        Assert.Equal(new[] { "en", "de" }, dto.CommentLanguages);
    }

    /// <summary>Die Quellsprache als lang liefert das Original — auch wenn es (nach einer Korrektur der
    /// Quellsprache) einen Satz dieser Sprache gibt.</summary>
    [Fact]
    public async Task Apply_LangIstQuelle_ImmerOriginal()
    {
        var line = await FullyTranslatedAsync(await BookAsync(lang: "de"));
        var dto = await DtoAsync(line.Id);

        await _localizer.ApplyAsync(dto, "de");

        Assert.Equal("Kommentar", dto.Comment);
        Assert.Null(dto.TitleLabel);
        Assert.Equal("de", dto.CommentLanguage);
        Assert.False(dto.CommentMachine);
        Assert.Equal(new[] { "de" }, dto.CommentLanguages);
    }

    [Fact]
    public async Task Apply_MehrereLinien_EineAbfrageJeArt_JedeLinieRichtig()
    {
        var book = await BookAsync();
        var translated = await FullyTranslatedAsync(book);
        var plain = await LineAsync(book, "002", comment: "Nur Original");
        var dtos = new List<BookPuzzleDto> { await DtoAsync(translated.Id), await DtoAsync(plain.Id) };

        await _localizer.ApplyAsync(dtos, "de");

        Assert.Equal("Kommentar DE", dtos[0].Comment);
        Assert.Equal("Nur Original", dtos[1].Comment);
        Assert.Equal("en", dtos[1].CommentLanguage);
        Assert.Equal(new[] { "en" }, dtos[1].CommentLanguages);
    }

    [Fact]
    public async Task ApplyChapters_LabelsAusDenLinien_NameBleibtSchluessel()
    {
        var book = await BookAsync();
        await FullyTranslatedAsync(book);
        var chapters = new List<CourseChapterDto>
        {
            new() { Index = 0, Name = "Kapitel" },
            new() { Index = 1, Name = "Anderes Kapitel" },
            new() { Index = 2, Name = null },
        };

        await _localizer.ApplyAsync(book.Id, chapters, "de");

        Assert.Equal("Kapitel", chapters[0].Name);
        Assert.Equal("Kapitel DE", chapters[0].Label);
        Assert.Null(chapters[1].Label);
        Assert.Null(chapters[2].Label);
    }

    [Fact]
    public async Task ApplyDetail_SetztLabelAnDenVerwaltungskapiteln()
    {
        var book = await BookAsync();
        await FullyTranslatedAsync(book);
        var detail = new CourseDetailDto { BookId = book.Id, Chapters = [new CourseManageChapterDto { Name = " Kapitel " }] };

        await _localizer.ApplyAsync(detail, "de");

        Assert.Equal("Kapitel DE", detail.Chapters[0].Label);
    }

    [Fact]
    public async Task ApplyCalc_PositionenUndKapitelsummen()
    {
        var book = await BookAsync();
        var line = await FullyTranslatedAsync(book);
        var calcBook = new CalcBookDto
        {
            BookId = book.Id,
            Positions = [new CalcPositionListItemDto { Id = line.Id, Title = "Titel", Chapter = "Kapitel" }],
            Chapters = [new CalcChapterSummaryDto { Chapter = "Kapitel" }],
        };
        var position = new CalcPositionDto { Id = line.Id, BookId = book.Id, Title = "Titel", Chapter = "Kapitel", Comment = "Kommentar" };
        var publicBook = new CalcPublicBookDto
        {
            BookId = book.Id,
            Positions = [new CalcPublicPositionDto { Id = line.Id, Title = "Titel", Chapter = "Kapitel", Comment = "Kommentar" }],
        };

        await _localizer.ApplyAsync(calcBook, "de");
        await _localizer.ApplyAsync(position, "de");
        await _localizer.ApplyAsync(publicBook, "de");

        Assert.Equal("Titel DE", calcBook.Positions[0].TitleLabel);
        Assert.Equal("Kapitel DE", calcBook.Positions[0].ChapterLabel);
        Assert.Equal("Kapitel DE", calcBook.Chapters[0].Label);
        Assert.Equal("Kommentar DE", position.Comment);
        Assert.Equal("Titel", position.Title);
        Assert.Equal("Titel DE", position.TitleLabel);
        Assert.True(position.CommentMachine);
        Assert.Equal("de", position.CommentLanguage);
        Assert.Equal("Kommentar DE", publicBook.Positions[0].Comment);
        Assert.Equal("Kapitel DE", publicBook.Positions[0].ChapterLabel);
    }

    [Theory]
    [InlineData("de", "de")]
    [InlineData(" DE ", "de")]
    [InlineData("und", "und")]
    [InlineData("pt-br", "pt-br")]
    [InlineData("", null)]
    [InlineData("deutsch", null)]
    [InlineData("de_DE", null)]
    public void NormalizeLanguage_NurKuerzel(string raw, string? expected)
        => Assert.Equal(expected, CourseCommentLocalizer.NormalizeLanguage(raw));

    // ── Endpunkte ────────────────────────────────────────────────────────────────────────────────

    private CourseController CourseController(CourseCommentLocalizer? localizer)
    {
        var controller = new CourseController(TestServices.Course(_db), new CourseStatsService(_db),
            ReprocessTestHelper.Build(_db), new RecordingReprocessLauncher(), new CourseAuthoringService(_db),
            new FlashcardMarkService(_db), TestServices.Conversion(_db), localizer);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "Admin")], "Test")),
            },
        };
        return controller;
    }

    [Fact]
    public async Task CourseEndpoints_MitLang_Uebersetzt_OhneLang_Original()
    {
        var book = await BookAsync(isPublic: true);
        await FullyTranslatedAsync(book);
        var controller = CourseController(_localizer);

        var plain = (List<BookPuzzleDto>)((OkObjectResult)(await controller.GetAllPuzzles(book.Id)).Result!).Value!;
        var german = (List<BookPuzzleDto>)((OkObjectResult)(await controller.GetAllPuzzles(book.Id, "de")).Result!).Value!;
        var publicGerman = (List<BookPuzzleDto>)((OkObjectResult)(await controller.GetPublicCourse(book.Id, null, null, "de")).Result!).Value!;
        var next = (CourseNextPuzzleDto)((OkObjectResult)await controller.GetNext(book.Id, lang: "de")).Value!;
        var chapters = (List<CourseChapterDto>)((OkObjectResult)(await controller.GetChapters(book.Id, "de")).Result!).Value!;
        var detail = (CourseDetailDto)((OkObjectResult)(await controller.GetDetail(book.Id, CancellationToken.None, "de")).Result!).Value!;

        Assert.Equal("Kommentar", plain[0].Comment);
        Assert.Null(plain[0].CommentLanguages);
        Assert.Equal("Kommentar DE", german[0].Comment);
        Assert.Equal("Kommentar DE", publicGerman[0].Comment);
        Assert.Equal("Kommentar DE", next.Puzzle!.Comment);
        Assert.Equal("Kapitel DE", chapters[0].Label);
        Assert.Equal("Kapitel DE", detail.Chapters[0].Label);
    }

    [Fact]
    public async Task BookPuzzleEndpoints_MitLang_Uebersetzt()
    {
        var book = await BookAsync(isPublic: true);
        var line = await FullyTranslatedAsync(book);
        var controller = new BookPuzzleController(
            new BookPuzzleService(_db, NullLogger<BookPuzzleService>.Instance, new NoOpTaskQueue()),
            new DailyLeaderboardService(_db), HintTestHelper.Build(_db), new NoOpTaskQueue(), _db,
            localizer: _localizer)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var byId = (BookPuzzleDto)((OkObjectResult)await controller.GetById(line.Id, "de")).Value!;
        var next = (BookPuzzleDto)((OkObjectResult)await controller.GetNextInBook(line.Id, "de")).Value!;
        var random = (BookPuzzleDto)((OkObjectResult)await controller.GetRandomInBook(line.Id, "de")).Value!;
        var plain = (BookPuzzleDto)((OkObjectResult)await controller.GetById(line.Id)).Value!;

        Assert.Equal("Kommentar DE", byId.Comment);
        Assert.Equal("Kommentar DE", next.Comment);     // Buch mit einer Linie: „naechste" ist wieder sie
        Assert.Equal("Kommentar DE", random.Comment);
        Assert.Equal("Kommentar", plain.Comment);
    }
}
