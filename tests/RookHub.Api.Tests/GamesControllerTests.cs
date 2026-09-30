using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class GamesControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SavedGameService _service;
    private readonly GamesController _controller;

    public GamesControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = TestServices.SavedGames(_db);
        _controller = new GamesController(_service, new GameMistakeProgressService(_db));
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
    }

    private async Task<AppUser> CreateUserAsync(string username = "testuser")
    {
        var user = new AppUser { Username = username, Email = $"{username}@test.com", PasswordHash = "hash" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<SavedGameDetailDto> SeedGameAsync(int userId)
        => await _service.SaveAsync(userId, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "c5" }, White = "a", Black = "b", Result = "0-1", ExternalId = Guid.NewGuid().ToString("N"),
        });

    [Fact]
    public async Task Save_SameExternalId_DedupsToSingleGame()
    {
        var user = await CreateUserAsync();
        var input = new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "c5" }, White = "a", Black = "b", Result = "0-1", ExternalId = "ext-123",
        };
        var first = await _service.SaveAsync(user.Id, input);
        var second = await _service.SaveAsync(user.Id, input);

        Assert.Equal(first.Id, second.Id);   // dieselbe Partie zurückgegeben
        Assert.Equal(1, _db.SavedGames.Count(g => g.UserId == user.Id && g.ExternalId == "ext-123"));
    }

    [Fact]
    public async Task Save_PersistsMoveCount_SoTheListNeedsNoPgn()
    {
        var user = await CreateUserAsync();
        var saved = await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "c5", "Nf3", "d6" }, ExternalId = "mc-1",
        });

        var row = _db.SavedGames.Single(g => g.Id == saved.Id);
        Assert.Equal(4, row.MoveCount);
    }

    [Fact]
    public async Task Save_Healing_UpdatesMoveCountWithThePgn()
    {
        // TryHeal ersetzt das PGN - der Zaehler MUSS mitwandern, sonst zeigt die Liste
        // dauerhaft die alte Zuglaenge einer Partie, die inzwischen laenger ist.
        var user = await CreateUserAsync();
        var input = new SaveGameInputDto { Source = "lichess", Moves = new() { "e4", "c5" }, ExternalId = "mc-2" };
        var first = await _service.SaveAsync(user.Id, input);

        input.Moves = new() { "e4", "c5", "Nf3", "d6", "d4" };
        await _service.SaveAsync(user.Id, input);

        Assert.Equal(5, _db.SavedGames.Single(g => g.Id == first.Id).MoveCount);
    }

    [Fact]
    public async Task List_CountsAndBackfillsRowsSavedBeforeTheCounterExisted()
    {
        // Altbestand: MoveCount == null. Die Liste muss trotzdem die richtige Zahl zeigen
        // UND sie nachtragen, damit dieselbe Zeile ihr PGN nie wieder dafuer hergeben muss.
        var user = await CreateUserAsync();
        var saved = await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "c5", "Nf3" }, ExternalId = "mc-3",
        });
        var row = _db.SavedGames.Single(g => g.Id == saved.Id);
        row.MoveCount = null;                      // Zustand vor der Einfuehrung des Feldes
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var list = await _service.ListAsync(user.Id);

        Assert.Equal(3, list.Single(g => g.Id == saved.Id).MoveCount);
        _db.ChangeTracker.Clear();
        Assert.Equal(3, _db.SavedGames.Single(g => g.Id == saved.Id).MoveCount);   // nachgetragen
    }

    [Fact]
    public async Task List_ReturnsOwnGamesNewestFirst()
    {
        var user = await CreateUserAsync();
        await SeedGameAsync(user.Id);
        await SeedGameAsync(user.Id);
        SetUser(user.Id);

        var result = (await _controller.List()).Result as OkObjectResult;
        var items = (List<SavedGameDto>)result!.Value!;
        Assert.Equal(2, items.Count);
        Assert.All(items, g => Assert.False(string.IsNullOrEmpty(g.ShareToken)));
    }

    [Fact]
    public async Task Get_OwnGame_ReturnsPgn()
    {
        var user = await CreateUserAsync();
        var seeded = await SeedGameAsync(user.Id);
        SetUser(user.Id);

        var result = (await _controller.Get(seeded.Id)).Result as OkObjectResult;
        var dto = result!.Value as SavedGameDetailDto;
        Assert.Contains("e4 c5", dto!.Pgn);
    }

    [Fact]
    public async Task Get_ForeignGame_NotFound()
    {
        var owner = await CreateUserAsync("owner");
        var other = await CreateUserAsync("other");
        var seeded = await SeedGameAsync(owner.Id);
        SetUser(other.Id);

        var result = await _controller.Get(seeded.Id);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Delete_RemovesOwnGame()
    {
        var user = await CreateUserAsync();
        var seeded = await SeedGameAsync(user.Id);
        SetUser(user.Id);

        var result = await _controller.Delete(seeded.Id);
        Assert.IsType<NoContentResult>(result);
        Assert.Empty(_db.SavedGames.Where(g => g.Id == seeded.Id));
    }

    [Fact]
    public async Task GetShared_ByToken_WorksWithoutOwnership()
    {
        var owner = await CreateUserAsync("owner");
        var seeded = await SeedGameAsync(owner.Id);
        // kein SetUser → öffentlicher Zugriff
        var result = (await _controller.GetShared(seeded.ShareToken)).Result as OkObjectResult;
        var dto = result!.Value as SharedGameDto;
        Assert.Equal("lichess", dto!.Source);
        Assert.Contains("e4 c5", dto.Pgn);
    }

    [Fact]
    public async Task GetShared_UnknownToken_NotFound()
    {
        var result = await _controller.GetShared("does-not-exist");
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetShared_OwnerPlayedBlackOnLichess_OwnerSideBlack()
    {
        var owner = await CreateUserAsync("blackowner");
        _db.UserProfiles.Add(new UserProfile { UserId = owner.Id, LichessUsername = "SchwarzSpieler" });
        await _db.SaveChangesAsync();
        var saved = await _service.SaveAsync(owner.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "c5" }, White = "Gegner",
            Black = "schwarzspieler", // case-insensitiv gegen den Profil-Username
            Result = "0-1", ExternalId = "side-1",
        });

        var shared = await _service.GetSharedAsync(saved.ShareToken);
        Assert.Equal("black", shared!.OwnerSide);
    }

    /// <summary>Oeffnet der Besitzer seinen eigenen Teilen-Link, bekommt er die Id seiner Partie — die Seite wechselt
    /// damit auf /games/{id} (0.526.3). Fremde und anonyme Aufrufer bekommen sie nicht.</summary>
    [Fact]
    public async Task GetShared_OwnGameId_nurFuerDenBesitzer()
    {
        var owner = await CreateUserAsync("besitzer");
        var other = await CreateUserAsync("gast");
        var saved = await _service.SaveAsync(owner.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "c5" }, White = "a", Black = "b", Result = "0-1", ExternalId = "own-1",
        });

        Assert.Equal(saved.Id, (await _service.GetSharedAsync(saved.ShareToken, owner.Id))!.OwnGameId);
        Assert.Null((await _service.GetSharedAsync(saved.ShareToken, other.Id))!.OwnGameId);
        Assert.Null((await _service.GetSharedAsync(saved.ShareToken))!.OwnGameId);
    }

    [Fact]
    public async Task GetShared_OwnerPlayedWhiteOnChessCom_OwnerSideWhite_UsesChessComName()
    {
        var owner = await CreateUserAsync("whiteowner");
        // lichess-Name absichtlich anders — bei Quelle chess.com zählt NUR der chess.com-Username.
        _db.UserProfiles.Add(new UserProfile { UserId = owner.Id, ChessComUsername = "WeissSpieler", LichessUsername = "andererName" });
        await _db.SaveChangesAsync();
        var saved = await _service.SaveAsync(owner.Id, new SaveGameInputDto
        {
            Source = "chess.com", Moves = new() { "e4", "c5" }, White = "WeissSpieler", Black = "Gegner",
            Result = "1-0", ExternalId = "side-2",
        });

        var shared = await _service.GetSharedAsync(saved.ShareToken);
        Assert.Equal("white", shared!.OwnerSide);
    }

    [Fact]
    public async Task GetShared_NoProfileOrNoNameMatch_OwnerSideNull()
    {
        var owner = await CreateUserAsync("nomatch");
        var saved = await _service.SaveAsync(owner.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "c5" }, White = "a", Black = "b",
            Result = "*", ExternalId = "side-3",
        });

        var shared = await _service.GetSharedAsync(saved.ShareToken);
        Assert.Null(shared!.OwnerSide);
    }

    [Fact]
    public async Task Save_WithElo_SharedViewExposesEloFromPgn()
    {
        var owner = await CreateUserAsync("owner");
        var saved = await _service.SaveAsync(owner.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "c5" }, White = "a", Black = "b",
            Result = "0-1", ExternalId = "elo-1", WhiteElo = 1832, BlackElo = 2011,
        });

        Assert.Contains("[WhiteElo \"1832\"]", saved.Pgn);
        Assert.Contains("[BlackElo \"2011\"]", saved.Pgn);
        Assert.Equal(1832, saved.WhiteElo);
        Assert.Equal(2011, saved.BlackElo);

        var shared = await _service.GetSharedAsync(saved.ShareToken);
        Assert.Equal(1832, shared!.WhiteElo);
        Assert.Equal(2011, shared.BlackElo);
    }

    [Fact]
    public async Task Save_Resave_HealsExistingGameWithEloAndMoreMoves_KeepsToken()
    {
        var user = await CreateUserAsync();
        // Erstsave: wenige Züge, kein Elo (wie eine alt gespeicherte, lückenhafte Partie).
        var first = await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "e5" }, White = "a", Black = "b",
            Result = "*", ExternalId = "heal-1",
        });
        Assert.Null(first.WhiteElo);

        // Re-Save derselben Partie mit mehr Zügen + Elo → heilt in-place, gleiches Token.
        var second = await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "e5", "Nf3", "Nc6" }, White = "a", Black = "b",
            Result = "1-0", ExternalId = "heal-1", WhiteElo = 2006, BlackElo = 2070,
        });

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.ShareToken, second.ShareToken);   // Link bleibt gleich
        Assert.Equal(2006, second.WhiteElo);
        Assert.Equal(2070, second.BlackElo);
        Assert.Contains("Nf3", second.Pgn);
        Assert.Equal("1-0", second.Result);
        Assert.Equal(1, _db.SavedGames.Count(g => g.UserId == user.Id && g.ExternalId == "heal-1"));
    }

    [Fact]
    public async Task Save_Resave_FewerMovesNoElo_DoesNotTruncate()
    {
        var user = await CreateUserAsync();
        var first = await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "e5", "Nf3", "Nc6" }, White = "a", Black = "b",
            Result = "1-0", ExternalId = "heal-2", WhiteElo = 2006,
        });
        var second = await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "e5" }, White = "a", Black = "b",
            Result = "*", ExternalId = "heal-2",
        });
        Assert.Equal(first.Id, second.Id);
        Assert.Contains("Nc6", second.Pgn);       // NICHT gekürzt
        Assert.Equal(2006, second.WhiteElo);      // Elo bleibt
        Assert.Equal("1-0", second.Result);
    }

    // ===== Gleiche ExternalId, andere Partie (N8-001: lichess-Analysebrett meldete „analysis") ====

    [Fact]
    public async Task Save_Resave_SameExternalIdOtherMoves_CreatesNewGame_OriginalUntouched()
    {
        var user = await CreateUserAsync();
        var logger = new CapturingLogger<SavedGameService>();
        var service = new SavedGameService(_db, TestServices.GameAnalyses(_db), TestServices.Analyze(_db), logger);
        var first = await service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "e5", "Nf3", "Nc6" }, White = "a", Black = "b",
            Result = "1-0", ExternalId = "analysis",
        });

        // Andere Partie auf demselben Analysebrett, MEHR Halbzüge: früher überschrieb TryHeal die erste.
        var second = await service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "d4", "d5", "c4", "e6", "Nc3", "Nf6" }, White = "c", Black = "d",
            Result = "0-1", ExternalId = "analysis",
        });

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.ShareToken, second.ShareToken);   // Link gehört zur neuen Partie
        Assert.Contains("c4", second.Pgn);
        var original = _db.SavedGames.AsNoTracking().Single(g => g.Id == first.Id);
        Assert.Equal(first.Pgn, original.Pgn);                  // hinter dem alten Link unverändert
        Assert.Equal("analysis", original.ExternalId);
        Assert.Null(_db.SavedGames.AsNoTracking().Single(g => g.Id == second.Id).ExternalId);
        var warn = Assert.Single(logger.Events, e => e.Level == LogLevel.Warning);
        Assert.Equal(first.Id, warn.State["ExistingGameId"]);
        Assert.Equal(second.Id, warn.State["SavedGameId"]);
    }

    [Fact]
    public async Task Save_Resave_SameExternalIdShorterOtherMoves_ReturnsLinkOfNewGame()
    {
        var user = await CreateUserAsync();
        var first = await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "e5", "Nf3", "Nc6" }, ExternalId = "analysis",
        });

        // WENIGER Halbzüge und eine andere Partie: früher kam der Link der ersten zurück.
        var second = await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "d4", "d5" }, ExternalId = "analysis",
        });

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.ShareToken, second.ShareToken);
        Assert.Equal(2, second.MoveCount);
        Assert.Equal(2, _db.SavedGames.Count(g => g.UserId == user.Id));
    }

    [Fact]
    public async Task Save_Resave_ContinuationWithCheckMarksAndLegacyGaps_StillHeals()
    {
        var user = await CreateUserAsync();
        // Alt gespeichert über die DOM-Auslese: „…"-Platzhalter in der Zugliste, Schach ohne „+".
        var first = await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "…", "f5", "Qh5" }, ExternalId = "heal-3",
        });

        var second = await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "f5", "Qh5+", "g6", "Qxg6" }, ExternalId = "heal-3",
            WhiteElo = 1500,
        });

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.ShareToken, second.ShareToken);
        Assert.Contains("Qxg6", second.Pgn);
        Assert.Equal(1, _db.SavedGames.Count(g => g.UserId == user.Id));
    }

    // ===== „Partie analysieren" + Bewertungskurve ============================

    /// <summary>Öffentlicher Aufruf ohne Anmeldung: der Controller liest die UserId trotzdem (falls ein
    /// Token mitkommt) — ohne Claims darf das nicht werfen.</summary>
    private void SetAnonymous()
        => _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

    [Fact]
    public async Task Analyze_Absage_400MitGrund_wieBeimEinwurf()
    {
        var user = await CreateUserAsync();   // ohne Engine
        var seeded = await SeedGameAsync(user.Id);
        SetUser(user.Id);

        var result = await _controller.Analyze(seeded.Id, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var reason = bad.Value!.GetType().GetProperty("reason")!.GetValue(bad.Value);
        Assert.Equal(GuessUploadReason.NoEngine, reason);
    }

    [Fact]
    public async Task Analyze_fremdePartie_404()
    {
        var owner = await CreateUserAsync("owner");
        var other = await CreateUserAsync("other");
        var seeded = await SeedGameAsync(owner.Id);
        SetUser(other.Id);

        Assert.IsType<NotFoundResult>((await _controller.Analyze(seeded.Id, CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await _controller.AnalyzeShared("gibt-es-nicht", CancellationToken.None)).Result);
    }

    [Fact]
    public async Task SharedEvals_anonym_ohneAnalyse_none()
    {
        var owner = await CreateUserAsync("owner");
        var seeded = await SeedGameAsync(owner.Id);
        SetAnonymous();

        var ok = Assert.IsType<OkObjectResult>((await _controller.SharedEvals(seeded.ShareToken, CancellationToken.None)).Result);
        Assert.Equal("none", ((GameEvalsDto)ok.Value!).Status);
        Assert.IsType<NotFoundResult>((await _controller.SharedEvals("gibt-es-nicht", CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Evals_fremdePartie_404()
    {
        var owner = await CreateUserAsync("owner");
        var other = await CreateUserAsync("other");
        var seeded = await SeedGameAsync(owner.Id);
        SetUser(other.Id);

        Assert.IsType<NotFoundResult>((await _controller.Evals(seeded.Id, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Save_ImplausibleOrMissingElo_OmittedFromPgnAndDto()
    {
        var owner = await CreateUserAsync("owner");
        var saved = await _service.SaveAsync(owner.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "c5" }, White = "a", Black = "b",
            Result = "0-1", ExternalId = "elo-2", WhiteElo = 42, BlackElo = null,
        });

        Assert.DoesNotContain("WhiteElo", saved.Pgn);
        Assert.DoesNotContain("BlackElo", saved.Pgn);
        Assert.Null(saved.WhiteElo);
        Assert.Null(saved.BlackElo);
    }

    // ── PGN hochladen ──────────────────────────────────────────────────

    private const string TwoGames = """
        [Event "Klubturnier"]
        [Site "Schwaz"]
        [Date "2026.09.20"]
        [Round "3"]
        [White "Anna"]
        [Black "Berta"]
        [Result "1-0"]
        [WhiteElo "1850"]
        [BlackElo "1720"]
        [TimeControl "5400+30"]

        1. e4 {Mein Lieblingszug.} e5 2. Nf3 (2. f4 exf4) Nc6 3. Bb5 a6 1-0

        [Event "Blitz"]
        [White "Carla"]
        [Black "Dora"]
        [Result "0-1"]

        1. d4 d5 2. c4 e6 0-1
        """;

    [Fact]
    public async Task Import_CreatesOneGamePerPgnGame_KeepsHeadersAndComments_DropsVariations()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);

        var ok = Assert.IsType<OkObjectResult>((await _controller.Import(new PgnImportRequestDto { Pgn = TwoGames })).Result);
        var res = Assert.IsType<PgnImportResultDto>(ok.Value);

        Assert.Equal(2, res.Imported);
        Assert.Empty(res.Failed);
        var first = await _db.SavedGames.SingleAsync(g => g.Id == res.Ids[0]);
        Assert.Equal(SavedGameService.ImportSource, first.Source);
        Assert.Equal("Anna", first.White);
        Assert.Equal("1-0", first.Result);
        Assert.Equal(6, first.MoveCount);
        Assert.Equal(1850, first.WhiteElo);
        Assert.Equal(1720, first.BlackElo);
        Assert.Equal(new DateTime(2026, 9, 20), first.PlayedAt?.Date);
        Assert.Contains("[Event \"Klubturnier\"]", first.Pgn);
        Assert.Contains("[TimeControl \"5400+30\"]", first.Pgn);
        Assert.Contains("{Mein Lieblingszug.}", first.Pgn);
        Assert.DoesNotContain("f4", first.Pgn);                     // Variante fällt weg
        Assert.Equal(new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6" }, GamePlies.Parse(first.Pgn)!.Value.Plies.Select(p => p.San));
    }

    [Fact]
    public async Task Import_Twice_CreatesNothingNew_AndReportsBrokenGamesWithoutCuttingThem()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);
        await _controller.Import(new PgnImportRequestDto { Pgn = TwoGames });

        var broken = TwoGames + "\n\n[White \"Eva\"]\n[Black \"Fritz\"]\n\n1. e4 e5 2. Ke3 Ke6 *\n";
        var ok = Assert.IsType<OkObjectResult>((await _controller.Import(new PgnImportRequestDto { Pgn = broken })).Result);
        var res = Assert.IsType<PgnImportResultDto>(ok.Value);

        Assert.Equal(0, res.Imported);
        Assert.Equal(2, res.Duplicates);
        var fail = Assert.Single(res.Failed);
        Assert.Equal(3, fail.Index);
        Assert.Equal("Eva", fail.White);
        Assert.Equal("illegal", fail.Reason);
        Assert.Equal(2, await _db.SavedGames.CountAsync());          // keine halbe Partie angelegt
    }

    [Fact]
    public async Task Import_BomAtTheStart_AndResultOnlyGame_AreReadCorrectly()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);
        // Manche Windows-Programme schreiben ein BOM; eine Partie nur mit Ergebnis ist „keine Züge", kein illegaler Zug.
        var pgn = "\uFEFF" + TwoGames + "\n\n[White \"Eva\"]\n[Black \"Fritz\"]\n[Result \"1-0\"]\n\n 1-0\n";
        var ok = Assert.IsType<OkObjectResult>((await _controller.Import(new PgnImportRequestDto { Pgn = pgn })).Result);
        var res = Assert.IsType<PgnImportResultDto>(ok.Value);
        Assert.Equal(2, res.Imported);
        Assert.Equal((3, "noMoves"), (Assert.Single(res.Failed).Index, res.Failed[0].Reason));
        Assert.Equal("Anna", (await _db.SavedGames.OrderBy(g => g.Id).FirstAsync()).White);
    }

    [Fact]
    public async Task Import_EmptyOrHuge_IsABadRequest_AndGamesStayPrivatePerUser()
    {
        var user = await CreateUserAsync();
        var other = await CreateUserAsync("other");
        SetUser(user.Id);
        Assert.IsType<BadRequestObjectResult>((await _controller.Import(new PgnImportRequestDto { Pgn = "  " })).Result);
        Assert.IsType<BadRequestObjectResult>((await _controller.Import(
            new PgnImportRequestDto { Pgn = new string('x', SavedGameService.MaxImportChars + 1) })).Result);

        await _controller.Import(new PgnImportRequestDto { Pgn = TwoGames });
        SetUser(other.Id);
        var ok = Assert.IsType<OkObjectResult>((await _controller.Import(new PgnImportRequestDto { Pgn = TwoGames })).Result);
        Assert.Equal(2, Assert.IsType<PgnImportResultDto>(ok.Value).Imported);   // Dublette gilt nur je Nutzer
    }
}
