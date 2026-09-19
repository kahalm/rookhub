using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

// HINWEIS Scope-Prüfung: die 17 „if (ScopeGuard() …)"-Zeilen der Actions sind durch EIN
// Klassen-Attribut ersetzt (`[RequireExtensionScope]`). Ein direkt instanziierter Controller
// führt keine Filter aus — die frühere „falscher Scope → Forbid"-Prüfung je Action konnte hier
// also nicht mehr greifen. Sie lebt jetzt in `RequireExtensionScopeTests` (Filter + Verdrahtung).
public class ExtensionControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RepertoireService _service;
    private readonly ExtensionController _controller;
    private readonly ParseStub _parse;
    private readonly ChessableIngestSessionStore _ingestSessions = new();
    private readonly ChessableImportService _chessableImport;

    /// <summary>Steht fuer piratechess: beantwortet NUR den Parse-Endpunkt (mit einer Kurs-Linie je
    /// Aufruf) — jeder andere Aufruf bleibt unerreichbar, wie ohne Stub.</summary>
    private sealed class ParseStub : HttpMessageHandler
    {
        public int Calls;
        /// <summary>Chessable-Stil-PGN: FEN + [%tqu]. piratechess zaehlt die Kapitel je Aufruf von vorn,
        /// liefert also IMMER "002.001" — der Versatz im Server muss daraus verschiedene Runden machen.</summary>
        private const string Pgn = "[Event \"Test Book\"]\n[Round \"002.001\"]\n[White \"Line\"]\n[Result \"*\"]\n"
            + "[SetUp \"1\"]\n[FEN \"rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2\"]\n\n"
            + "{ [%tqu \"En\",\"Finde den Zug\"] Pointe. } 2.Nf3 Nc6 3. Bb5 $1 a6 *\n";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.AbsolutePath != "/api/chessable/direct/course/parse")
                throw new HttpRequestException("Connection refused");
            Calls++;
            var json = System.Text.Json.JsonSerializer.Serialize(new { pgn = Pgn, name = "Course", lineCount = 1 });
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    public ExtensionControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var analyzeService = new RepertoireAnalyzeService(_db, cache);
        _service = TestServices.Repertoire(_db, analyze: analyzeService);
        var trainingGoalService = new TrainingGoalService(_db);
        var encryption = new EncryptionService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!" })
            .Build());
        _parse = new ParseStub();
        var chessableProxy = new ChessableProxyService(new HttpClient(_parse) { BaseAddress = new Uri("http://pc:8080") });
        var rememberedService = new RememberedPositionService(_db, encryption, chessableProxy,
            NullLogger<RememberedPositionService>.Instance);
        var savedGameService = new SavedGameService(_db);
        var bgQueue = new NoOpBackgroundTaskQueue();
        var rateLimiterConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var chessableImport = _chessableImport = new ChessableImportService(_db, encryption, chessableProxy, _service,
            new PgnImportService(_db), bgQueue, new NotificationService(_db),
            new ChessableBearerBreaker(_db, bgQueue, NullLogger<ChessableBearerBreaker>.Instance),
            new ChessableRateLimiter(_db, rateLimiterConfig), NullLogger<ChessableImportService>.Instance);
        _controller = new ExtensionController(_service, analyzeService, trainingGoalService, rememberedService,
            savedGameService, new SharedLineService(_db), chessableProxy, chessableImport,
            _ingestSessions,
            new ChessableTrainedLineService(_db, new RepertoireTrainingService(_db)),
            new ChessableProblemMoveService(_db),
            new ChessableReviewLineService(_db, new PgnImportService(_db)),
            new ChessableSessionMoveService(_db),
            NullLogger<ExtensionController>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId, string? scope = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (scope != null) claims.Add(new Claim("scope", scope));
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    private async Task<AppUser> CreateUserAsync(string username = "testuser")
    {
        var user = new AppUser
        {
            Username = username,
            Email = $"{username}@test.com",
            PasswordHash = "hash"
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task GetRepertoires_ReturnsOk()
    {
        var user = await CreateUserAsync();
        _db.Repertoires.Add(new Repertoire { UserId = user.Id, Name = "Rep1" });
        await _db.SaveChangesAsync();
        SetUser(user.Id);

        var result = await _controller.GetRepertoires();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var reps = okResult.Value as List<ExtensionRepertoireDto>;
        Assert.Single(reps!);
    }

    [Fact]
    public async Task GetRepertoires_ReturnsEmpty_ForNewUser()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);

        var result = await _controller.GetRepertoires();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var reps = okResult.Value as List<ExtensionRepertoireDto>;
        Assert.Empty(reps!);
    }

    [Fact]
    public async Task GetPgn_ReturnsContent()
    {
        var user = await CreateUserAsync();
        var rep = new Repertoire { UserId = user.Id, Name = "Rep1" };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        _db.RepertoireFiles.Add(new RepertoireFile
        {
            RepertoireId = rep.Id,
            FileName = "game.pgn",
            PgnContent = "[Event \"Test\"] 1. e4",
            FileSize = 20
        });
        await _db.SaveChangesAsync();
        SetUser(user.Id);

        var result = await _controller.GetPgn(rep.Id);

        Assert.IsType<ContentResult>(result);
    }

    [Fact]
    public async Task GetPgn_ReturnsNotFound_WhenMissing()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);

        var result = await _controller.GetPgn(99999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetPgn_ReturnsNotFound_WhenWrongUser()
    {
        var user1 = await CreateUserAsync("user1");
        var user2 = await CreateUserAsync("user2");
        var rep = new Repertoire { UserId = user1.Id, Name = "Rep1" };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        SetUser(user2.Id);

        var result = await _controller.GetPgn(rep.Id);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetRepertoires_FiltersByKind()
    {
        var user = await CreateUserAsync();
        _db.Repertoires.AddRange(
            new Repertoire { UserId = user.Id, Name = "open", Kind = RepertoireKind.Opening },
            new Repertoire { UserId = user.Id, Name = "end", Kind = RepertoireKind.Endgame },
            new Repertoire { UserId = user.Id, Name = "none" }
        );
        await _db.SaveChangesAsync();
        SetUser(user.Id);

        var all = (await _controller.GetRepertoires()).Result as OkObjectResult;
        Assert.Equal(3, ((List<ExtensionRepertoireDto>)all!.Value!).Count);

        var openings = (await _controller.GetRepertoires("opening")).Result as OkObjectResult;
        var openList = (List<ExtensionRepertoireDto>)openings!.Value!;
        Assert.Single(openList);
        Assert.Equal("open", openList[0].Name);
        Assert.Equal(RepertoireKind.Opening, openList[0].Kind);
    }

    [Fact]
    public async Task GetRepertoires_ExcludesNotFlaggedForExtension()
    {
        var user = await CreateUserAsync();
        _db.Repertoires.AddRange(
            new Repertoire { UserId = user.Id, Name = "on", UseForExtension = true },
            new Repertoire { UserId = user.Id, Name = "off", UseForExtension = false }
        );
        await _db.SaveChangesAsync();
        SetUser(user.Id);

        var result = (await _controller.GetRepertoires()).Result as OkObjectResult;
        var reps = (List<ExtensionRepertoireDto>)result!.Value!;
        Assert.Single(reps);
        Assert.Equal("on", reps[0].Name);
    }

    [Fact]
    public async Task GetRepertoires_InvalidKind_Returns400()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);
        var result = await _controller.GetRepertoires("hyperspeed");
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetRepertoires_WithExtensionScope_Allowed()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, scope: "extension");
        var result = await _controller.GetRepertoires();
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task RecordTrainingActivity_PersistsRow()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, scope: "extension");

        var result = await _controller.RecordTrainingActivity(new ChessableActivityInputDto { SecondsActive = 120, MovesTrained = 8 });

        Assert.IsType<OkObjectResult>(result);
        var row = Assert.Single(_db.ChessableActivities.Where(a => a.UserId == user.Id));
        Assert.Equal(120, row.TimeSeconds);
        Assert.Equal(8, row.MovesTrained);
    }

    [Fact]
    public async Task RecordTrainingActivity_RejectsNonPositiveSeconds()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, scope: "extension");

        var result = await _controller.RecordTrainingActivity(new ChessableActivityInputDto { SecondsActive = 0 });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(_db.ChessableActivities.Where(a => a.UserId == user.Id));
    }

    [Fact]
    public async Task RememberLine_PersistsAndIsListed()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, scope: "extension");
        const string fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";

        var save = await _controller.RememberLine(new RememberLineInputDto { Fen = fen, CourseId = "228856", SourceUrl = "https://www.chessable.com/course/228856/1/" });
        Assert.IsType<OkObjectResult>(save.Result);

        var row = Assert.Single(_db.RememberedPositions.Where(p => p.UserId == user.Id));
        Assert.Equal(fen, row.Fen);
        Assert.Equal("228856", row.CourseId);

        var list = (await _controller.GetRememberedLines()).Result as OkObjectResult;
        var items = (List<RememberedPositionDto>)list!.Value!;
        Assert.Single(items);
        Assert.Equal(fen, items[0].Fen);
    }

    [Fact]
    public async Task RememberLine_RejectsInvalidFen()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, scope: "extension");

        var result = await _controller.RememberLine(new RememberLineInputDto { Fen = "not-a-fen" });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(_db.RememberedPositions.Where(p => p.UserId == user.Id));
    }

    [Fact]
    public async Task SaveGame_PersistsPgnAndShareToken()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, scope: "extension");

        var save = await _controller.SaveGame(new SaveGameInputDto
        {
            Source = "chesscom",
            Moves = new() { "e4", "e5", "Nf3" },
            White = "alice",
            Black = "bob",
            Result = "1-0",
            ExternalId = "12345",
            SourceUrl = "https://www.chess.com/analysis/game/live/12345",
        });
        var dto = Assert.IsType<OkObjectResult>(save.Result).Value as SavedGameDetailDto;
        Assert.NotNull(dto);
        Assert.False(string.IsNullOrEmpty(dto!.ShareToken));
        Assert.Equal(3, dto.MoveCount);
        Assert.Contains("1. e4 e5 2. Nf3", dto.Pgn);
        Assert.Contains("[White \"alice\"]", dto.Pgn);

        var row = Assert.Single(_db.SavedGames.Where(g => g.UserId == user.Id));
        Assert.Equal("chess.com", row.Source);
        Assert.Equal("12345", row.ExternalId);
    }

    [Fact]
    public async Task SaveGame_DedupsBySourceAndExternalId()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, scope: "extension");
        SaveGameInputDto Input() => new() { Source = "lichess", Moves = new() { "d4", "d5" }, ExternalId = "abcd1234" };

        var first = await _controller.SaveGame(Input());
        var second = await _controller.SaveGame(Input());

        var firstDto = Assert.IsType<OkObjectResult>(first.Result).Value as SavedGameDetailDto;
        var secondDto = Assert.IsType<OkObjectResult>(second.Result).Value as SavedGameDetailDto;
        Assert.Equal(firstDto!.Id, secondDto!.Id);
        Assert.Single(_db.SavedGames.Where(g => g.UserId == user.Id));
    }

    [Fact]
    public async Task SaveGame_RejectsEmptyMoves()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, scope: "extension");

        var result = await _controller.SaveGame(new SaveGameInputDto { Source = "lichess", Moves = new() });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(_db.SavedGames.Where(g => g.UserId == user.Id));
    }

    private static ChessableIngestChapter Chapter(params string[] lines)
        => new("{\"list\":{\"name\":\"Ch\",\"data\":[]}}", lines.ToList());

    [Fact]
    public async Task ChessableIngestChunk_TakesTheCourseNameFromTheLines_NotFromThePageText()
    {
        SetUser(7, scope: "extension");
        // Der Seitentext der Kurskachel (Titel + Fortschrittsbadges in EINEM Link, Repertoire 265 am 2026-09-19).
        const string garbage = "Short & Sweet0%Priority0/15variations✓ 0/15";
        const string line = "{\"game\":{\"bid\":163418,\"name\":\"Short & Sweet: Caro-Kann\",\"title\":\"Line 1\"}}";

        var r1 = await _controller.ChessableIngestChunk(
            new ChessableIngestChunkRequest("sess-name", "163418", "book", garbage, Chapter(line), false), default);
        Assert.IsType<OkObjectResult>(r1);

        var import = await _db.ChessableImports.SingleAsync();
        Assert.Equal("Short & Sweet: Caro-Kann", import.CourseName);
        Assert.Equal("Short & Sweet: Caro-Kann", (await _db.Books.SingleAsync()).DisplayName);
    }

    [Fact]
    public async Task ChessableIngestLive_TakesTheCourseNameFromTheLines()
    {
        SetUser(7, scope: "extension");
        const string line = "{\"game\":{\"bid\":163418,\"name\":\"Short & Sweet: Caro-Kann\"}}";
        var res = await _controller.ChessableIngestLive(
            new ChessableLiveIngestRequest("163418", "repertoire", "Short & Sweet0%Priority0/15variations✓ 0/15",
                new List<ChessableIngestChapter> { Chapter(line) }), default);
        Assert.IsType<OkObjectResult>(res);
        Assert.Equal("Short & Sweet: Caro-Kann", (await _db.Repertoires.SingleAsync()).Name);
    }

    [Fact]
    public async Task ChessableIngestChunk_ImportsEveryChunkImmediately_WithoutOverwritingTheOneBefore()
    {
        SetUser(7, scope: "extension");

        var r1 = await _controller.ChessableIngestChunk(
            new ChessableIngestChunkRequest("sess-1", "424242", "book", "Course", Chapter("{\"game\":{}}", "{\"game\":{}}"), false), default);
        var ack1 = Assert.IsType<ChessableIngestChunkAck>(Assert.IsType<OkObjectResult>(r1).Value);
        Assert.False(ack1.Done);
        Assert.Equal(1, ack1.Chapters);
        Assert.Equal(1, ack1.Imported);
        // Schon VOR dem letzten Chunk steht die Linie in der Datenbank — das ist der Punkt des Umbaus.
        Assert.Equal(1, await _db.BookPuzzles.CountAsync());
        // Das neue Buch heisst wie der Kurs, nicht wie seine Datei (so hiess es nach dem ersten Live-Import).
        Assert.Equal("Course", (await _db.Books.SingleAsync()).DisplayName);

        var r2 = await _controller.ChessableIngestChunk(
            new ChessableIngestChunkRequest("sess-1", "424242", "book", "Course", Chapter("{\"game\":{}}"), false), default);
        var ack2 = Assert.IsType<ChessableIngestChunkAck>(Assert.IsType<OkObjectResult>(r2).Value);
        Assert.Equal(2, ack2.Chapters);
        Assert.Equal(2, ack2.Imported);
        // Beide Chunks kamen mit derselben Kapitelnummer vom Parser; ohne Versatz waere es EINE Linie.
        Assert.Equal(2, await _db.BookPuzzles.CountAsync());
        Assert.Equal(2, _parse.Calls);
    }

    [Fact]
    public async Task ChessableIngestChunk_Final_ClosesTheImport()
    {
        SetUser(7, scope: "extension");
        await _controller.ChessableIngestChunk(
            new ChessableIngestChunkRequest("sess-2", "424242", "book", "Course", Chapter("{\"game\":{}}"), false), default);

        var res = await _controller.ChessableIngestChunk(
            new ChessableIngestChunkRequest("sess-2", "424242", "book", "Course", Chapter("{\"game\":{}}"), true), default);
        var dto = Assert.IsType<ChessableIngestResultDto>(Assert.IsType<OkObjectResult>(res).Value);
        Assert.Equal(2, dto.Imported);

        var import = await _db.ChessableImports.SingleAsync();
        Assert.Equal(ChessableImportStatus.Completed, import.Status);
        Assert.Equal(2, import.ChaptersDone);
        Assert.NotNull(import.ResultId);
        // Genau EINE Benachrichtigung je Sitzung, nicht eine je Chunk.
        Assert.Equal(1, await _db.Notifications.CountAsync(n => n.UserId == 7));
    }

    [Fact]
    public async Task ChessableIngestChunk_NumbersChaptersSeamlessly()
    {
        SetUser(7, scope: "extension");
        for (var i = 0; i < 3; i++)
            await _controller.ChessableIngestChunk(
                new ChessableIngestChunkRequest("sess-n", "424242", "book", "Course", Chapter("{\"game\":{}}"), false), default);

        // Der Parser liefert jedem Chunk „002.001"; im Buch stehen sie als 002, 003, 004 — nicht 002, 004, 006.
        var rounds = await _db.BookPuzzles.OrderBy(p => p.Round).Select(p => p.Round).ToListAsync();
        Assert.Equal(new[] { "002.001", "003.001", "004.001" }, rounds);
    }

    [Fact]
    public async Task ChessableIngestChunk_Aborted_ClosesTheImportWithWhatWasFetched()
    {
        SetUser(7, scope: "extension");
        await _controller.ChessableIngestChunk(
            new ChessableIngestChunkRequest("sess-a", "424242", "book", "Course", Chapter("{\"game\":{}}"), false), default);

        var res = await _controller.ChessableIngestChunk(
            new ChessableIngestChunkRequest("sess-a", "424242", "book", "Course", null, true, Aborted: true), default);
        var ack = Assert.IsType<ChessableIngestChunkAck>(Assert.IsType<OkObjectResult>(res).Value);
        Assert.True(ack.Done);
        Assert.Equal(1, ack.Imported);

        var import = await _db.ChessableImports.SingleAsync();
        Assert.Equal(ChessableImportStatus.Failed, import.Status);
        Assert.Contains("1 Linien übernommen", import.Error);
        // Die Inflight-Marke ist prozessweit und nach Import-Id verschluesselt — parallele Testklassen mit
        // eigener InMemory-DB teilen sich Id 1. Deshalb hier ueber den Store pruefen (Sitzung samt Marke weg).
        Assert.Equal(0, _ingestSessions.Count);
        Assert.Equal(1, await _db.BookPuzzles.CountAsync());   // das Geholte bleibt
    }

    [Fact]
    public async Task ChessableIngestChunk_ExpiredSession_IsClosedByTheWatchdog()
    {
        SetUser(7, scope: "extension");
        await _controller.ChessableIngestChunk(
            new ChessableIngestChunkRequest("sess-x", "424242", "book", "Course", Chapter("{\"game\":{}}"), false), default);
        var import = await _db.ChessableImports.SingleAsync();
        Assert.Equal(ChessableImportStatus.Running, import.Status);
        Assert.Equal(1, _ingestSessions.Count);

        // Browser zu — kein Chunk mehr. Nach der TTL schliesst der Watchdog den Datensatz.
        _ingestSessions.Ttl = TimeSpan.Zero;
        var closed = await ChessableImportWatchdogService.CloseExpiredBrowserSessionsAsync(_ingestSessions, _chessableImport);

        Assert.Equal(1, closed);
        await _db.Entry(import).ReloadAsync();
        Assert.Equal(ChessableImportStatus.Failed, import.Status);
        Assert.Contains("ohne Abschluss", import.Error);
        Assert.Equal(0, _ingestSessions.Count);
    }

    [Fact]
    public async Task ChessableIngestChunk_FinalWithoutAnyChapter_IsBadRequest()
    {
        SetUser(7, scope: "extension");
        var res = await _controller.ChessableIngestChunk(
            new ChessableIngestChunkRequest("sess-3", "424242", "book", "Course", null, true), default);
        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task ChessableIngestChunk_LineOidsCountMismatch_ReturnsBadRequest()
    {
        SetUser(7, scope: "extension");
        var chapter = new ChessableIngestChapter("{\"list\":{\"data\":[]}}", new List<string> { "{\"game\":{}}" }, new List<string> { "1", "2" });
        var res = await _controller.ChessableIngestChunk(
            new ChessableIngestChunkRequest("sess-oids", "424242", "book", "Course", chapter, false), default);
        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task ChessableIngestLive_LineWithoutContentAndWithoutOids_ReturnsBadRequest()
    {
        SetUser(7, scope: "extension");
        var chapter = new ChessableIngestChapter("{\"list\":{\"data\":[]}}", new List<string> { null! });
        var res = await _controller.ChessableIngestLive(
            new ChessableLiveIngestRequest("424242", "repertoire", "Course", new List<ChessableIngestChapter> { chapter }), default);
        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Theory]
    [InlineData("12a")]
    [InlineData("0")]
    [InlineData("")]
    public async Task ChessableCachedLines_InvalidOid_ReturnsBadRequest(string oid)
    {
        SetUser(7, scope: "extension");
        var res = await _controller.ChessableCachedLines(new ChessableCachedLinesRequest(new List<string> { "11", oid }), default);
        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task ChessableCachedLines_TooManyOids_ReturnsBadRequest()
    {
        SetUser(7, scope: "extension");
        var oids = Enumerable.Range(1, 10001).Select(i => i.ToString()).ToList();
        var res = await _controller.ChessableCachedLines(new ChessableCachedLinesRequest(oids), default);
        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task ChessableCachedLines_PiratechessUnreachable_ReturnsEmptyList()
    {
        // Weich: ohne Cache-Auskunft holt die Extension eben alle Linien selbst — kein Fehler für den Nutzer.
        SetUser(7, scope: "extension");
        var res = await _controller.ChessableCachedLines(new ChessableCachedLinesRequest(new List<string> { "11" }), default);
        var dto = Assert.IsType<ChessableCachedLinesDto>(Assert.IsType<OkObjectResult>(res).Value);
        Assert.Empty(dto.Oids);
    }

    [Fact]
    public async Task ChessableIngestChunk_InvalidBid_ReturnsBadRequest()
    {
        SetUser(7, scope: "extension");
        var res = await _controller.ChessableIngestChunk(
            new ChessableIngestChunkRequest("sess-1", "nope", "book", null, Chapter("{\"game\":{}}"), false), default);
        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task ChessableIngestChunk_FinalWithoutSession_ReturnsBadRequest()
    {
        SetUser(7, scope: "extension");
        // finaler Chunk ohne Kapitel und ohne vorherige Chunks → nichts zu importieren
        var res = await _controller.ChessableIngestChunk(
            new ChessableIngestChunkRequest("ghost", "424242", "book", null, null, true), default);
        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task ChessableReviewLines_StoresRawAndReturnsCount()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, scope: "extension");
        var dto = new ChessableReviewLinesInputDto
        {
            Bid = "228856",
            Entries = new()
            {
                new ChessableReviewLineEntryDto { Oid = "1", Json = "{\"lesson\":{\"moves\":[]}}" },
                new ChessableReviewLineEntryDto { Oid = "2", Json = "{\"lesson\":{\"moves\":[]}}" },
            },
        };

        var res = await _controller.ChessableReviewLines(dto, default);

        var ok = Assert.IsType<OkObjectResult>(res);
        var stored = (int)ok.Value!.GetType().GetProperty("stored")!.GetValue(ok.Value)!;
        Assert.Equal(2, stored);
        Assert.Equal(2, _db.ChessableReviewLines.Count(r => r.UserId == user.Id && r.Bid == "228856"));
    }

    [Fact]
    public async Task ChessableReviewLines_TriggersMerge_SeedsCourseFromReview()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, scope: "extension");
        var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "getReview-sample.json"));

        var res = await _controller.ChessableReviewLines(new ChessableReviewLinesInputDto
        {
            Bid = "228856",
            Entries = new() { new ChessableReviewLineEntryDto { Oid = "36730415", Json = fixture } },
        }, default);

        var ok = Assert.IsType<OkObjectResult>(res);
        var merged = (int)ok.Value!.GetType().GetProperty("merged")!.GetValue(ok.Value)!;
        Assert.Equal(1, merged);

        // Der Merge nach dem Speichern hat die Review-Linie in den Kurs (Buch) gespült.
        var fileName = $"chessable-u{user.Id}-228856.pgn";
        var bp = _db.BookPuzzles.Single(b => b.BookFileName == fileName);
        Assert.Equal("review", bp.Source);
        Assert.Equal("36730415", bp.ChessableOid);
    }

    [Fact]
    public async Task ChessableReviewLines_InvalidBid_BadRequest()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, scope: "extension");

        var res = await _controller.ChessableReviewLines(new ChessableReviewLinesInputDto
        {
            Bid = "not-a-bid",
            Entries = new() { new ChessableReviewLineEntryDto { Oid = "1", Json = "{}" } },
        }, default);

        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task ChessableReviewLinesAnon_StoresByUid_NoAccountNeeded()
    {
        // KEIN SetUser: der anonyme Endpoint braucht keinen RookHub-Account/Scope.
        var dto = new AnonymousChessableReviewLinesInputDto
        {
            Uid = "790927",
            Bid = "228856",
            Entries = new() { new ChessableReviewLineEntryDto { Oid = "1", Json = "{\"lesson\":{\"moves\":[]}}" } },
        };

        var res = await _controller.ChessableReviewLinesAnon(dto, default);

        var ok = Assert.IsType<OkObjectResult>(res);
        var stored = (int)ok.Value!.GetType().GetProperty("stored")!.GetValue(ok.Value)!;
        Assert.Equal(1, stored);
        Assert.Equal(1, _db.AnonymousChessableReviewLines.Count(r => r.ChessableUid == "790927" && r.Bid == "228856"));
        Assert.Empty(_db.ChessableReviewLines);   // nichts an einen Account gebunden
    }

    [Fact]
    public async Task ChessableReviewLinesAnon_InvalidUid_BadRequest()
    {
        var res = await _controller.ChessableReviewLinesAnon(new AnonymousChessableReviewLinesInputDto
        {
            Uid = "not-a-uid",
            Bid = "228856",
            Entries = new() { new ChessableReviewLineEntryDto { Oid = "1", Json = "{}" } },
        }, default);

        Assert.IsType<BadRequestObjectResult>(res);
        Assert.Empty(_db.AnonymousChessableReviewLines);
    }

    // ---- Unerwartete Chessable-Antwort (RepCheck ≥ 1.60.0 stoppt „Kurs holen") ----

    private ChessableResponseAlertService Alerts() => new(_db,
        new AdminMessageService(_db, new NotificationService(_db)), NullLogger<ChessableResponseAlertService>.Instance);

    [Theory]
    [InlineData("abc", "getGame", "1")]
    [InlineData("104929", "saveProgressAndReturnNewProgressInfo", "1")]
    [InlineData("104929", "getGame", "-5")]
    public async Task ChessableUnexpectedResponse_InvalidInput_BadRequest(string bid, string endpoint, string oid)
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, "extension");

        var res = await _controller.ChessableUnexpectedResponse(new ChessableUnexpectedResponseInputDto
        {
            Bid = bid, Endpoint = endpoint, Oid = oid, Message = "User is banned or deleted",
        }, Alerts(), default);

        Assert.IsType<BadRequestObjectResult>(res);
        Assert.Empty(_db.AdminMessages);
    }

    [Fact]
    public async Task ChessableUnexpectedResponse_BanMessage_CreatesAdminMessageForTheCaller()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, "extension");

        var res = await _controller.ChessableUnexpectedResponse(new ChessableUnexpectedResponseInputDto
        {
            Bid = "104929", Endpoint = "getGame", Oid = "17672584", Status = 200, Reason = "error",
            Message = "User is banned or deleted", ExtensionVersion = "1.60.0",
        }, Alerts(), default);

        var ok = Assert.IsType<OkObjectResult>(res);
        var dto = Assert.IsType<ChessableUnexpectedResponseResultDto>(ok.Value);
        Assert.True(dto.Banned);
        Assert.True(dto.AdminNotified);
        Assert.Equal(user.Id, Assert.Single(_db.AdminMessages).UserId);
    }

    [Fact]
    public async Task ChessableUnexpectedResponse_OtherResponse_OnlyLogged()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, "extension");

        var res = await _controller.ChessableUnexpectedResponse(new ChessableUnexpectedResponseInputDto
        {
            Bid = "207313", Endpoint = "getList", Lid = "42", Status = 200, Reason = "shape", Snippet = "{}",
        }, Alerts(), default);

        var dto = Assert.IsType<ChessableUnexpectedResponseResultDto>(Assert.IsType<OkObjectResult>(res).Value);
        Assert.False(dto.Banned);
        Assert.Empty(_db.AdminMessages);
    }
}
