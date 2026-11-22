using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RookHub.Api.Controllers.Og;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Og;
using RookHub.Api.Services.TrainingVideos;

namespace RookHub.Api.Tests.Og;

/// <summary>Link-Vorschau der Teilen-Links, die bis 2026-11-22 keine hatten: Trainerstunde <c>/tl/</c> (+ drill), Vereinspartie über den
/// Liga-Link <c>/s/{token}/club-games/{id}</c>, anonyme Zusammenfassung <c>/ga/</c> (ohne Namen), Meisterpartie, öffentlicher Kurs.
/// Je Route: Tags + Bild, Anonymisierung, abgelaufen/widerrufen → keine Vorschau. Spieler erfunden.</summary>
public class OgShareLinkTests : IDisposable
{
    private const string Base = "https://rh.example";
    private const string RealName = "Ackermann, Anna";
    private const string RealFide = "1234567";
    private const int Tnr = 4711, Round = 2;
    private const string LessonToken = "lessonTokenAAAAAAAAAAAAAAA";
    private const string Pgn = "[Event \"Landesliga Runde 2\"]\n[White \"Ackermann, Anna\"]\n[Black \"Brunner, Bert\"]\n[Result \"1-0\"]\n"
        + "[WhiteElo \"1800\"]\n[BlackElo \"1750\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 1-0\n";

    private readonly AppDbContext _db = OgCardTests.NewDb();
    public void Dispose() => _db.Dispose();

    private sealed class FakeExplorer(string? pgn) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(pgn is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(pgn) });
        }
    }

    private OgMetaService Meta(HttpMessageHandler? explorer = null)
    {
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        var clubService = new LeagueClubService(_db, NullLogger<LeagueClubService>.Instance);
        return new(null!, null!, null!, null!, _db, NullLogger<OgMetaService>.Instance,
            leagueClubGames: new LeagueShareClubGames(_db, league, new LeagueFixtureGames(_db), clubService),
            anonGames: new AnonGameShares(_db),
            lessonShares: new TrainingLessonShares(_db),
            lessons: new TrainingVideoService(_db, Options.Create(new TrainingVideoOptions { VideoRoot = Path.GetTempPath() })),
            explorer: explorer is null ? null : new LocalExplorerClient(new HttpClient(explorer) { BaseAddress = new Uri("http://explorer.test/") },
                NullLogger<LocalExplorerClient>.Instance),
            memory: new MemoryCache(new MemoryCacheOptions()));
    }

    // ---- Pfade ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("/tl/abc_DEF-1", "lesson", "abc_DEF-1")]
    [InlineData("/tl/abc_DEF-1/drill", "ldrill", "abc_DEF-1")]
    [InlineData("/ga/abc_DEF-1/summary", "ganon", "abc_DEF-1")]
    [InlineData("/s/abc_DEF-1/club-games/241", "scgame", "abc_DEF-1~241")]
    [InlineData("/s/abc_DEF-1/club-games/241/summary?x=1", "scgame", "abc_DEF-1~241")]
    [InlineData("/explorer/game/masters/AbCd1234", "master", "AbCd1234")]
    public void ParsePath_NewShareRoutes(string path, string kind, string id)
    {
        var r = OgMetaService.ParsePath(path);
        Assert.Equal((kind, id), (r!.Value.Kind, r.Value.Id));
        Assert.Null(OgMetaService.ParsePath(path, OgIndexHtmlProvider.LeagueHubSite));   // RookHub-Routen, nicht LeagueHub
    }

    [Theory]
    [InlineData("/tl")]
    [InlineData("/tl/abc/other")]
    [InlineData("/ga/abc")]                         // nur die Zusammenfassung ist der anonyme Link
    [InlineData("/s/abc/club-games/x")]
    [InlineData("/s/abc/club-games/0")]
    [InlineData("/s/a~b/club-games/5")]
    [InlineData("/explorer/game/masters/kurz")]
    [InlineData("/mate1")]                          // Kurs-Kurz-URL nur mit Markierung des nginx
    public void ParsePath_NoPreview(string path) => Assert.Null(OgMetaService.ParsePath(path));

    [Theory]
    [InlineData("/mate1", "mate1")]
    [InlineData("/Mate1?utm=1", "mate1")]
    public void ParseCoursePath_SlugOnly(string path, string id) => Assert.Equal(("course", id), OgMetaService.ParseCoursePath(path));

    [Fact]
    public void ParseCoursePath_ChapterIsEncoded_NotASlugIsNothing()
    {
        var r = OgMetaService.ParseCoursePath("/noel/KW%2046");
        Assert.Equal("course", r!.Value.Kind);
        Assert.StartsWith("noel~", r.Value.Id);
        Assert.DoesNotContain("/", r.Value.Id);
        Assert.Null(OgMetaService.ParseCoursePath("/a/b/c"));
        Assert.Null(OgMetaService.ParseCoursePath("/x_y"));
        Assert.Null(OgMetaService.ParseCoursePath(""));
    }

    // ---- /tl ---------------------------------------------------------------------------------------------------------------

    private async Task<int> LessonAsync(DateTime expires, DateTime? revoked = null)
    {
        var lesson = new TrainingLesson
        {
            UserId = 10, Date = new DateOnly(2026, 11, 17), ImportedAt = DateTime.UtcNow, DurationSec = 3000, Summary = "Sizilianisch, Endspiele.",
            Games = [new TrainingLessonGame { Key = "g1", Kind = "own", White = RealName, Black = "Brunner, Bert", Position = 0 }],
            Segments =
            [
                new TrainingSegment { Position = 0, StartSec = 10, GameKey = "g1", Fen = "rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2", Move = "Nf3" },
                new TrainingSegment { Position = 1, StartSec = 50, Comment = "nur Text" },
            ],
        };
        _db.TrainingLessons.Add(lesson);
        await _db.SaveChangesAsync();
        _db.TrainingLessonShares.Add(new TrainingLessonShare { LessonId = lesson.Id, Token = LessonToken, CreatedAt = DateTime.UtcNow, ExpiresAt = expires, RevokedAt = revoked });
        await _db.SaveChangesAsync();
        return lesson.Id;
    }

    [Fact]
    public async Task Lesson_ValidLink_PageAndCard_ShortCache_NoNames()
    {
        await LessonAsync(DateTime.UtcNow.AddDays(7));
        var page = await Meta().ResolvePageAsync($"/tl/{LessonToken}", Base);
        Assert.Equal("Training session · 17 Nov 2026", page!.Title);
        Assert.Equal("Sizilianisch, Endspiele.", page.Description);
        Assert.Equal($"{Base}/api/og/img/lesson/{LessonToken}.png?c={OgMetaService.CardVersion}", page.ImageUrl);

        var drill = await Meta().ResolvePageAsync($"/tl/{LessonToken}/drill", Base);
        Assert.StartsWith("Training drill · 17 Nov 2026", drill!.Title);
        Assert.Contains("/api/og/img/ldrill/", drill.ImageUrl);

        var board = await Meta().ResolveBoardAsync("lesson", LessonToken);
        Assert.Equal(OgMetaService.EditableMaxAge, board!.MaxAge);              // läuft ab, widerrufbar
        Assert.Equal("Training session", board.Card!.Kicker);
        Assert.StartsWith("rnbqkbnr/pp1ppppp/8/2p5/4P3", board.Card.Boards[0].Fen);
        var text = string.Join("|", board.Card.Lines.Append(board.Card.Title ?? "").Append(OgController.Inject("<head></head>", page)));
        Assert.DoesNotContain("Ackermann", text);
        Assert.DoesNotContain("Brunner", text);
        OgCardTests.AssertCardPng(OgCardTests.Images().RenderCard(board.Card));
        Assert.NotNull(await Meta().ResolveBoardAsync("ldrill", LessonToken));
    }

    [Theory]
    [InlineData(true, false)]    // abgelaufen
    [InlineData(false, true)]    // widerrufen
    public async Task Lesson_ExpiredOrRevoked_NoPreview(bool expired, bool revoked)
    {
        await LessonAsync(expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddDays(7), revoked ? DateTime.UtcNow : null);
        Assert.Null(await Meta().ResolvePageAsync($"/tl/{LessonToken}", Base));
        Assert.Null(await Meta().ResolvePageAsync($"/tl/{LessonToken}/drill", Base));
        Assert.Null(await Meta().ResolveBoardAsync("lesson", LessonToken));
        Assert.Null(await Meta().ResolveBoardAsync("ldrill", LessonToken));
    }

    // ---- /s/{token}/club-games/{id} ------------------------------------------------------------------------------------------

    private async Task<int> LeagueLinkGameAsync(bool anon, DateOnly expires)
    {
        await TestClubs.SeedAsync(_db);
        _db.LeagueRounds.Add(new LeagueRound { Tnr = Tnr, Round = Round, Date = new DateOnly(2026, 10, 11) });
        var lg = new LeagueGame { Tnr = Tnr, Round = Round, MatchNo = 1, Board = 1, HomeTeam = "Testdorf", AwayTeam = "Bergheim",
            HomePlayer = RealName, AwayPlayer = "Brunner, Bert", HomeColor = "w", Result = "1 - 0" };
        _db.LeagueGames.Add(lg);
        await _db.SaveChangesAsync();
        var c = new LeagueClubGame { ClubId = TestClubs.HomeId, Year = 2026, White = anon ? "Testdorf" : RealName, Black = "Brunner, Bert",
            Result = "1-0", Plies = 6, Pgn = Pgn, MovesHash = "h1", Anonymized = anon, Event = "Landesliga Runde 2", WhiteElo = 1800, BlackElo = 1750,
            WhiteRealName = anon ? RealName : null, WhiteRealFide = anon ? RealFide : null };
        LeagueGameLinks.Set(c, lg);
        var other = new LeagueClubGame { ClubId = TestClubs.HomeId, Year = 2026, White = "X", Black = "Y", Result = "0-1", Plies = 2, Pgn = "1. d4 d5 0-1", MovesHash = "h2" };
        _db.LeagueClubGames.AddRange(c, other);
        _db.LeagueShares.Add(new LeagueShare { Token = "LINK1", ClubId = TestClubs.HomeId, Tnr = Tnr, Round = Round, Team = "Testdorf", Expires = expires, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        return c.Id;
    }

    [Fact]
    public async Task LeagueLinkGame_Anonymized_OnlyAnonName_NoEventNoElo_ShortCache()
    {
        var id = await LeagueLinkGameAsync(anon: true, new DateOnly(2099, 1, 1));
        foreach (var path in new[] { $"/s/LINK1/club-games/{id}", $"/s/LINK1/club-games/{id}/summary" })
        {
            var page = await Meta().ResolvePageAsync(path, Base);
            Assert.Equal("Testdorf – Brunner, Bert (Elo 1750)", page!.Title);
            Assert.Equal("1-0 · Partie auf RookHub nachspielen", page.Description);
            Assert.Equal($"{Base}/api/og/img/scgame/LINK1~{id}.png?c={OgMetaService.CardVersion}", page.ImageUrl);
            AssertNoRealName(OgController.Inject("<html><head></head></html>", page));
        }
        var board = await Meta().ResolveBoardAsync("scgame", $"LINK1~{id}");
        Assert.Equal(new[] { "Testdorf", "Brunner, Bert" }, board!.Card!.Players.Select(p => p.Name));
        Assert.Null(board.Card.Players[0].Elo);
        AssertNoRealName(string.Join("|", board.Card.Lines.Append(board.Card.Kicker ?? "").Append(board.Card.Title ?? "")));
        Assert.Equal(OgMetaService.EditableMaxAge, board.MaxAge);
        OgCardTests.AssertCardPng(OgCardTests.Images().RenderCard(board.Card));
    }

    [Fact]
    public async Task LeagueLinkGame_ExpiredLinkOrGameNotInFixture_NoPreview()
    {
        var id = await LeagueLinkGameAsync(anon: false, new DateOnly(2099, 1, 1));
        var foreign = _db.LeagueClubGames.Single(g => g.MovesHash == "h2").Id;
        Assert.NotNull(await Meta().ResolvePageAsync($"/s/LINK1/club-games/{id}", Base));
        Assert.Null(await Meta().ResolvePageAsync($"/s/LINK1/club-games/{foreign}", Base));   // nicht an einem Brett der Begegnung
        Assert.Null(await Meta().ResolveBoardAsync("scgame", $"LINK1~{foreign}"));
        Assert.Null(await Meta().ResolvePageAsync($"/s/NIX/club-games/{id}", Base));

        var share = _db.LeagueShares.Single();
        share.Expires = new DateOnly(2020, 1, 1);                                              // abgelaufen
        await _db.SaveChangesAsync();
        Assert.Null(await Meta().ResolvePageAsync($"/s/LINK1/club-games/{id}", Base));
        Assert.Null(await Meta().ResolveBoardAsync("scgame", $"LINK1~{id}"));
    }

    // ---- /ga/{token}/summary ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnonSummary_GeneralCard_WithoutAnyName_RevokedIsNothing()
    {
        _db.SavedGames.Add(new SavedGame { UserId = 10, Source = "Landesliga", Pgn = Pgn, White = RealName, Black = "Brunner, Bert", Result = "1-0",
            WhiteElo = 1800, ShareToken = "normalTokenAAAAAAAAAA", AnonShareToken = "anonTokenAAAAAAAAAAAA" });
        await _db.SaveChangesAsync();
        var page = await Meta().ResolvePageAsync("/ga/anonTokenAAAAAAAAAAAA/summary", Base);
        Assert.Equal("Game summary", page!.Title);
        Assert.Equal("1-0 · 3 moves · an anonymous game summary on RookHub", page.Description);
        var board = await Meta().ResolveBoardAsync("ganon", "anonTokenAAAAAAAAAAAA");
        Assert.Empty(board!.Card!.Players);
        Assert.Equal(OgMetaService.ExpiringMaxAge, board.MaxAge);
        var all = string.Join("|", board.Card.Lines.Append(board.Card.Kicker ?? "").Append(board.Card.Title ?? "").Append(OgController.Inject("<head></head>", page)));
        foreach (var leak in new[] { "Ackermann", "Brunner", "1800", "1750", "Landesliga" }) Assert.DoesNotContain(leak, all);
        OgCardTests.AssertCardPng(OgCardTests.Images().RenderCard(board.Card));

        // Der normale Link ist kein anonymer; widerrufen (neues Token) → nichts.
        Assert.Null(await Meta().ResolvePageAsync("/ga/normalTokenAAAAAAAAAA/summary", Base));
        _db.SavedGames.Single().AnonShareToken = null;
        await _db.SaveChangesAsync();
        Assert.Null(await Meta().ResolvePageAsync("/ga/anonTokenAAAAAAAAAAAA/summary", Base));
        Assert.Null(await Meta().ResolveBoardAsync("ganon", "anonTokenAAAAAAAAAAAA"));
    }

    // ---- /explorer/game/masters/{id} -------------------------------------------------------------------------------------

    [Fact]
    public async Task MasterGame_TitleWithElo_CardImmutable_ExplorerAskedOnce()
    {
        var explorer = new FakeExplorer("[Event \"Tata Steel\"]\n[Date \"2019.01.20\"]\n[White \"Carlsen, Magnus\"]\n[Black \"Giri, Anish\"]\n"
            + "[Result \"1/2-1/2\"]\n[WhiteElo \"2835\"]\n[BlackElo \"2783\"]\n\n1. d4 Nf6 2. c4 e6 1/2-1/2\n");
        var meta = Meta(explorer);
        var page = await meta.ResolvePageAsync("/explorer/game/masters/AbCd1234", Base);
        Assert.Equal("Carlsen, Magnus (Elo 2835) – Giri, Anish (Elo 2783)", page!.Title);
        Assert.Equal("1/2-1/2 · Tata Steel · 2019 · master game — replay it on RookHub", page.Description);
        var board = await meta.ResolveBoardAsync("master", "AbCd1234");
        Assert.Equal("Master game", board!.Card!.Kicker);
        Assert.Null(board.MaxAge);                                                // veröffentlichte Partie: fest
        Assert.Equal(1, explorer.Calls);                                          // Arbeitsspeicher wie die Seite
        OgCardTests.AssertCardPng(OgCardTests.Images().RenderCard(board.Card));

        Assert.Null(await Meta(new FakeExplorer(null)).ResolvePageAsync("/explorer/game/masters/Zzzz9999", Base));
        Assert.Null(await Meta(explorer).ResolveBoardAsync("master", "../etc"));
    }

    // ---- /{slug}, /{slug}/{kapitel} --------------------------------------------------------------------------------------

    private async Task CourseAsync(bool isPublic = true)
    {
        var book = new Book { FileName = "m.pgn", DisplayName = "Mate in one", Description = "Easy mates for beginners.", IsPublic = isPublic,
            PublicSlug = "mate1", Source = new BookSource() };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        _db.BookPuzzles.AddRange(
            new BookPuzzle { BookId = book.Id, Chapter = "Basics", Fen = "6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1", Moves = "a1a8", StartPly = -1, Round = "1" },
            new BookPuzzle { BookId = book.Id, Chapter = "KW 46", Fen = "k7/8/1K6/8/8/8/8/7Q w - - 0 1", Moves = "h1a1", StartPly = -1, Round = "2" });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Course_OnlyWithNginxMark_PublicBook_ChapterCard()
    {
        await CourseAsync();
        Assert.Null(await Meta().ResolvePageAsync("/mate1", Base));             // ohne Markierung: keine Kurs-Route
        var page = await Meta().ResolvePageAsync("/mate1", Base, course: true);
        Assert.Equal("Mate in one", page!.Title);
        Assert.Equal("Easy mates for beginners.", page.Description);
        Assert.Equal($"{Base}/mate1", page.CanonicalUrl);

        var chapter = await Meta().ResolvePageAsync("/mate1/kw%2046", Base, course: true);
        Assert.Equal("KW 46 · Mate in one", chapter!.Title);
        Assert.Equal("1 position · free chess course on RookHub — no account needed", chapter.Description);
        var id = OgMetaService.ParseCoursePath("/mate1/kw%2046")!.Value.Id;
        var board = await Meta().ResolveBoardAsync("course", id);
        Assert.Equal(("Course chapter", "KW 46"), (board!.Card!.Kicker, board.Card.Title));
        Assert.StartsWith("k7/8/1K6", board.Card.Boards[0].Fen);
        Assert.Equal(OgMetaService.ExpiringMaxAge, board.MaxAge);
        OgCardTests.AssertCardPng(OgCardTests.Images().RenderCard(board.Card));

        Assert.Null(await Meta().ResolvePageAsync("/mate1/unbekannt", Base, course: true));
        Assert.Null(await Meta().ResolvePageAsync("/dashboard", Base, course: true));
        Assert.Null(await Meta().ResolvePageAsync("/mate1", Base, default, OgIndexHtmlProvider.LeagueHubSite, course: true));
    }

    [Fact]
    public async Task Course_NotPublic_NoPreview()
    {
        await CourseAsync(isPublic: false);
        Assert.Null(await Meta().ResolvePageAsync("/mate1", Base, course: true));
        Assert.Null(await Meta().ResolveBoardAsync("course", "mate1"));
    }

    private static void AssertNoRealName(string text)
    {
        Assert.DoesNotContain("Ackermann", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Anna", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RealFide, text);
        Assert.DoesNotContain("Runde", text);
    }
}
