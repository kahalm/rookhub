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

    // ---- Codereview F4-014: der Herkunfts-Link wird als „Original oeffnen" gerendert, auch auf /g/{token} ----

    [Theory]
    [InlineData("https://www.chess.com/game/live/184299739920", true)]
    [InlineData("https://lichess.org/abcdEFGH", true)]
    [InlineData("http://m.lichess.org/abcdEFGH", true)]
    [InlineData("https://phish.example/rookhub-login", false)]
    [InlineData("https://chess.com.phish.example/x", false)]
    [InlineData("https://lichess.org@phish.example/x", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,hi", false)]
    [InlineData("//phish.example/x", false)]
    [InlineData("/g/abc", false)]
    public void SafeSourceUrl_KeepsOnlyHttpLinksOfTheTwoSources(string raw, bool kept)
        => Assert.Equal(kept ? raw : null, SavedGameService.SafeSourceUrl(raw));

    [Fact]
    public async Task Save_WithAForeignSourceUrl_StoresNoLink_AndNoSiteHeader()
    {
        // Fund-Weg: ueber die Save-Schnittstelle eine Phishing-Adresse als SourceUrl ablegen und den Teilen-Link
        // verbreiten — auf /g/<token> stuende „Original oeffnen" und fuehrte auf die fremde Seite.
        var user = await CreateUserAsync();

        var saved = await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "c5" }, SourceUrl = "https://phish.example/rookhub-login",
        });

        Assert.Null(saved.SourceUrl);
        Assert.Matches(ShareTokensTests.Format, saved.ShareToken);
        Assert.Null((await _db.SavedGames.SingleAsync()).SourceUrl);
        Assert.DoesNotContain("phish.example", saved.Pgn);
        Assert.Null((await _service.GetSharedAsync(saved.ShareToken))!.SourceUrl);
    }

    [Fact]
    public async Task Save_WithThePlatformLink_KeepsIt()
    {
        var user = await CreateUserAsync();

        var saved = await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "chess.com", Moves = new() { "e4", "c5" }, SourceUrl = "https://www.chess.com/game/live/1",
        });

        Assert.Equal("https://www.chess.com/game/live/1", saved.SourceUrl);
        Assert.Equal("https://www.chess.com/game/live/1", (await _service.GetSharedAsync(saved.ShareToken))!.SourceUrl);
    }

    [Fact]
    public async Task EarlierStoredForeignSourceUrl_IsNotHandedOut()
    {
        // Altbestand: frueher ungeprueft gespeichert — die Teilen-Seite, die Liste und das Detail liefern ihn nicht aus.
        var user = await CreateUserAsync();
        var saved = await SeedGameAsync(user.Id);
        var row = await _db.SavedGames.SingleAsync();
        row.SourceUrl = "https://phish.example/rookhub-login";
        await _db.SaveChangesAsync();

        Assert.Null((await _service.GetSharedAsync(saved.ShareToken))!.SourceUrl);
        Assert.Null(Assert.Single(await _service.ListAsync(user.Id)).SourceUrl);
        Assert.Null((await _service.GetAsync(user.Id, saved.Id))!.SourceUrl);
    }

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

    /// <summary>Prod-Partie 63 (08.10.): der erste Save kam ganz ohne Metadaten. Ein zweiter mit denselben Zügen trägt
    /// Spieler, Bedenkzeit und Datum nach — auch ohne neue Züge oder Wertung; Link und Id bleiben.</summary>
    [Fact]
    public async Task Save_Resave_SameMoves_FillsMissingPlayersTimeControlAndDate()
    {
        var user = await CreateUserAsync();
        var moves = new List<string> { "e4", "e5", "Nf3" };
        var first = await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "chess.com", Moves = moves, Result = "1-0", ExternalId = "heal-meta",
        });
        Assert.Null(first.White);

        var second = await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "chess.com", Moves = moves, Result = "1-0", ExternalId = "heal-meta",
            White = "kahalm", Black = "ukker1992", TimeControl = "180+2", PlayedAt = new DateTime(2026, 10, 8, 13, 33, 57, DateTimeKind.Utc),
        });

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.ShareToken, second.ShareToken);
        Assert.Equal(("kahalm", "ukker1992"), (second.White, second.Black));
        Assert.Contains("[White \"kahalm\"]", second.Pgn);
        Assert.Contains("[TimeControl \"180+2\"]", second.Pgn);
        var row = _db.SavedGames.AsNoTracking().Single(g => g.Id == first.Id);
        Assert.Equal("180+2", row.TimeControl);
        Assert.NotNull(row.PlayedAt);
    }

    /// <summary>Ein Heil-Save, dem selbst etwas fehlt (hier die Namen), behält die gespeicherten — in den Spalten UND im PGN-Kopf.</summary>
    [Fact]
    public async Task Save_Resave_HealWithoutNames_KeepsStoredNamesInPgn()
    {
        var user = await CreateUserAsync();
        await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "e5" }, White = "a", Black = "b", Result = "*", ExternalId = "heal-keep",
        });
        var healed = await _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "e5", "Nf3" }, Result = "*", ExternalId = "heal-keep", WhiteElo = 2000,
        });
        Assert.Equal(("a", "b"), (healed.White, healed.Black));
        Assert.Contains("[White \"a\"]", healed.Pgn);
        Assert.Contains("[Black \"b\"]", healed.Pgn);
    }

    /// <summary>Hat die gespeicherte Partie schon alles, ändert ein gleicher Save nichts (kein unnötiges Neuschreiben).</summary>
    [Fact]
    public void FillsMissing_NurWennWirklichEtwasFehlt()
    {
        var full = new SavedGame { White = "a", Black = "b", TimeControl = "600", PlayedAt = DateTime.UtcNow };
        Assert.False(SavedGameService.FillsMissing(full, new SaveGameInputDto { White = "x", Black = "y", TimeControl = "180+2", PlayedAt = DateTime.UtcNow }));
        Assert.True(SavedGameService.FillsMissing(new SavedGame { Black = "b", TimeControl = "600", PlayedAt = DateTime.UtcNow },
            new SaveGameInputDto { White = "x" }));
        Assert.False(SavedGameService.FillsMissing(new SavedGame(), new SaveGameInputDto { White = " ", TimeControl = "kaputt" }));
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

    // ===== Elo aus der Liga (0.737.0, Wunsch 2026-10-10: „beim Anschauen und Teilen die Elo oben im Namen") ====

    [Fact]
    public async Task Detail_AndShared_CopyOfAClubGame_TakesTheEloFromTheLeagueRoster()
    {
        var user = await CreateUserAsync();
        var club = new LeagueClubGame { ClubId = 1, White = "Mitteregger, Gottfried", WhiteFide = "1651846", Black = "Schwaz",
            BlackRealFide = "1700001", Result = "1/2-1/2", Pgn = "1. e4 c6 1/2-1/2", MovesHash = "h", LeagueTnr = 20 };
        _db.LeagueClubGames.Add(club);
        _db.LeaguePlayers.AddRange(
            new LeaguePlayer { Tnr = 10, Team = "Freibauer", Name = "Mitteregger, Gottfried", FideId = "1651846", EloI = 1700 },
            new LeaguePlayer { Tnr = 20, Team = "Freibauer", Name = "Mitteregger, Gottfried", FideId = "1651846", EloI = 1826 },
            new LeaguePlayer { Tnr = 20, Team = "Schwaz", Name = "Erlacher, Herbert", FideId = "1700001", EloN = 1712 });
        await _db.SaveChangesAsync();
        var game = new SavedGame { UserId = user.Id, Source = "pgn", White = "Mitteregger, Gottfried", Black = "Erlacher, Herbert",
            Pgn = "[White \"Mitteregger, Gottfried\"]\n[Black \"Erlacher, Herbert\"]\n\n1. e4 c6 1/2-1/2", ShareToken = "tok173",
            LeagueClubGameId = club.Id, CreatedAt = DateTime.UtcNow };
        _db.SavedGames.Add(game);
        await _db.SaveChangesAsync();

        var detail = await _service.GetAsync(user.Id, game.Id);
        Assert.Equal((1826, 1712), (detail!.WhiteElo, detail.BlackElo));          // Liga der Paarung vor älterer Meldeliste
        var shared = await _service.GetSharedAsync("tok173");
        Assert.Equal((1826, 1712), (shared!.WhiteElo, shared.BlackElo));

        // Teilen: die Linkvorschau trägt die Elo im Titel
        var meta = new RookHub.Api.Services.Og.OgMetaService(_service, null!, null!, null!, _db,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RookHub.Api.Services.Og.OgMetaService>.Instance);
        var page = await meta.ResolvePageAsync("/g/tok173", "https://rookhub.example");
        Assert.Equal("Mitteregger, Gottfried (Elo 1826) – Erlacher, Herbert (Elo 1712)", page!.Title);

        // Trainingslink (0.745.0): eigene Überschrift und eigenes Bild
        var train = await meta.ResolvePageAsync("/g/tok173?train=black", "https://rookhub.example");
        Assert.Equal("Verbessere dich — spiele deine Fehler neu", train!.Title);
        Assert.Contains("Erlacher, Herbert", train.Description);
        Assert.StartsWith("https://rookhub.example/api/og/img/train/black-tok173.png", train.ImageUrl);
        Assert.Equal("https://rookhub.example/g/tok173?train=black", train.CanonicalUrl);
        var board = await meta.ResolveBoardAsync("train", "black-tok173");
        Assert.True(board!.Flip);
        Assert.Equal("Die Fehler von Erlacher, Herbert", board.Train!.Note);
        Assert.Null(await meta.ResolveBoardAsync("train", "green-tok173"));
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

    /// <summary>0.666.1: die Seite schickt <c>?book=0</c> — ein <c>bool</c>-Parameter hätte das mit 400 abgewiesen (die Partie-Seite
    /// zeigte weder Kurve noch Analyse). Der Parameter ist deshalb ein String und nimmt auch 0/1.</summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData(" FALSE ", false)]
    [InlineData("no", false)]
    public void WithBook_nimmtAuch0Und1(string? raw, bool expected)
        => Assert.Equal(expected, GamesController.WithBook(raw));

    [Fact]
    public void Evals_Book_ist_ein_String_damit_0_nicht_mit_400_endet()
    {
        foreach (var name in new[] { nameof(GamesController.Evals), nameof(GamesController.SharedEvals) })
            Assert.Equal(typeof(string),
                typeof(GamesController).GetMethod(name)!.GetParameters().Single(p => p.Name == "book").ParameterType);
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

    // ── Eigene Seite beim Import + Partien aus einer Stellung (Sparring gegen Maia) ─────

    /// <summary>Zweispringerspiel nach 4.O-O — Schwarz am Zug.</summary>
    private const string FenBlackToMove = "r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQ1RK1 b kq - 5 4";
    /// <summary>Dieselbe Partie nach 4...Bc5 — Weiß am Zug.</summary>
    private const string FenWhiteToMove = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQ1RK1 w kq - 6 5";

    private static string SparringPgn(string fen, string moveText, bool setUp = true)
        => "[Event \"Sparring vs Maia\"]\n[Site \"RookHub\"]\n[Date \"2026.10.02\"]\n[Round \"-\"]\n"
           + "[White \"anna\"]\n[Black \"Maia 1600\"]\n[Result \"*\"]\n"
           + (setUp ? "[SetUp \"1\"]\n" : "") + $"[FEN \"{fen}\"]\n\n{moveText}\n";

    /// <summary>chess.js schreibt eine Partie, die mit Schwarz am Zug beginnt, als „4. ... Bc5" (Nummer, Leerzeichen,
    /// drei Punkte als eigenes Token). Vorher blieben die drei Punkte als „Zug" übrig, und der Import meldete die
    /// Partie als illegal — gegen die Dev-API gemessen (2026-10-02). Die kompakte Form und die ohne Nummer gingen schon vorher,
    /// ebenso eine Stellung mit Weiß am Zug, mit und ohne <c>[SetUp]</c>.</summary>
    [Theory]
    [InlineData(FenBlackToMove, true, "4. ... Bc5 5. c3 d6 6. d4 exd4 7. cxd4 Bb6 *", 7)]
    [InlineData(FenBlackToMove, true, "4... Bc5 5. c3 d6 6. d4 exd4 7. cxd4 Bb6 *", 7)]
    [InlineData(FenBlackToMove, true, "Bc5 5. c3 d6 6. d4 exd4 7. cxd4 Bb6 *", 7)]
    [InlineData(FenWhiteToMove, true, "5. c3 d6 6. d4 exd4 7. cxd4 Bb6 *", 6)]
    [InlineData(FenWhiteToMove, false, "5. c3 d6 6. d4 exd4 7. cxd4 Bb6 *", 6)]
    public async Task ImportPgn_FromAPosition_AllMoveNumberForms_AreAccepted(string fen, bool setUp, string moveText, int plies)
    {
        var user = await CreateUserAsync();

        var res = await _service.ImportPgnAsync(user.Id, SparringPgn(fen, moveText, setUp));

        Assert.Empty(res.Failed);
        Assert.Equal(1, res.Imported);
        var game = await _db.SavedGames.SingleAsync(g => g.Id == res.Ids[0]);
        Assert.Equal(plies, game.MoveCount);
        Assert.Contains($"[FEN \"{fen}\"]", game.Pgn);
        var (header, parsed) = GamePlies.Parse(game.Pgn)!.Value;
        Assert.Equal(fen, header.StartFen);
        Assert.Equal(plies, parsed.Count);
        Assert.Equal(fen == FenBlackToMove ? "Bc5" : "c3", parsed[0].San);
        Assert.Equal("Bb6", parsed[^1].San);
        if (fen == FenBlackToMove) Assert.Contains("4... Bc5 5. c3 d6", game.Pgn);
    }

    [Fact]
    public async Task Import_WithOwnerSideBlack_SetsTheSide_AndDetermineOwnerSideReturnsIt()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);

        var res = await ImportAsync(new PgnImportRequestDto
        {
            Pgn = SparringPgn(FenBlackToMove, "4... Bc5 5. c3 d6 *"), OwnerSide = "black",
        });

        var game = await _db.SavedGames.SingleAsync(g => g.Id == Assert.Single(res.Ids));
        Assert.Equal("black", game.OwnerSide);
        Assert.Equal("black", SavedGameService.DetermineOwnerSide(game, null));
        Assert.Equal("black", (await _service.GetAsync(user.Id, game.Id))!.OwnerSide);
    }

    [Theory]
    [InlineData("both")]
    [InlineData("Black")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Import_WithoutOrWithInvalidOwnerSide_LeavesItUnset(string? ownerSide)
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);

        var res = await ImportAsync(new PgnImportRequestDto { Pgn = TwoGames, OwnerSide = ownerSide });

        Assert.Equal(2, res.Imported);
        Assert.All(await _db.SavedGames.ToListAsync(), g => Assert.Null(g.OwnerSide));
    }

    /// <summary>Eine Dublette bleibt, wie sie ist — auch ihre Seite. Zweimal auf „Partie analysieren" klicken legt
    /// nichts doppelt an und dreht nichts um.</summary>
    [Fact]
    public async Task Import_Duplicate_KeepsItsOwnerSide()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);
        var pgn = SparringPgn(FenBlackToMove, "4... Bc5 5. c3 d6 *");
        var first = await ImportAsync(new PgnImportRequestDto { Pgn = pgn, OwnerSide = "black" });

        var again = await ImportAsync(new PgnImportRequestDto { Pgn = pgn, OwnerSide = "white" });
        var unset = await ImportAsync(new PgnImportRequestDto { Pgn = pgn });

        Assert.Equal((0, 1), (again.Imported, again.Duplicates));
        Assert.Equal(first.Ids, again.Ids);
        Assert.Equal(first.Ids, unset.Ids);
        Assert.Equal("black", (await _db.SavedGames.SingleAsync()).OwnerSide);
    }

    private async Task<PgnImportResultDto> ImportAsync(PgnImportRequestDto body)
        => Assert.IsType<PgnImportResultDto>(Assert.IsType<OkObjectResult>((await _controller.Import(body)).Result).Value);

    // ── Deckel je Konto (A6-007) ─────

    private const string ThirdGame = "[White \"Eva\"]\n[Black \"Fritz\"]\n[Result \"1-0\"]\n\n1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 4. Qxf7# 1-0\n";
    private const string FourthGame = "[White \"Gus\"]\n[Black \"Hans\"]\n\n1. d4 Nf6 *\n";

    private async Task<PgnImportResultDto> ImportAsync(string pgn)
        => Assert.IsType<PgnImportResultDto>(Assert.IsType<OkObjectResult>(
            (await _controller.Import(new PgnImportRequestDto { Pgn = pgn })).Result).Value);

    /// <summary>Am Zähldeckel legt der Import nichts Neues mehr an (Grund <c>quota</c>), Dubletten bleiben Dubletten,
    /// und der Deckel zählt auch INNERHALB eines Uploads mit. Ohne Deckel füllte ein Skript mit wechselndem
    /// Round-Header die Datenbank beliebig.</summary>
    [Fact]
    public async Task Import_AtTheGameCap_NewGamesFailWithQuota_DuplicatesStillCount_OtherUsersUnaffected()
    {
        var user = await CreateUserAsync();
        var other = await CreateUserAsync("other");
        _service.GamesPerUserCap = 3;
        SetUser(user.Id);

        var first = await ImportAsync(TwoGames + "\n\n" + ThirdGame + "\n\n" + FourthGame);
        Assert.Equal(3, first.Imported);                               // der Deckel zählt im selben Upload mit
        Assert.Equal((4, "quota", "Gus"), (Assert.Single(first.Failed).Index, first.Failed[0].Reason, first.Failed[0].White));

        var again = await ImportAsync(TwoGames + "\n\n" + FourthGame);
        Assert.Equal(0, again.Imported);
        Assert.Equal(2, again.Duplicates);                              // Vorhandenes bleibt erreichbar
        Assert.Equal((3, "quota"), (Assert.Single(again.Failed).Index, again.Failed[0].Reason));
        Assert.Equal(3, await _db.SavedGames.CountAsync(g => g.UserId == user.Id));

        SetUser(other.Id);
        Assert.Equal(1, (await ImportAsync(FourthGame)).Imported);     // der Deckel gilt je Konto
    }

    /// <summary>Der Zeichendeckel: die Partie, die das Konto über die Summe brächte, bleibt draußen — eine kleinere
    /// dahinter passt noch. Kommentare und Kopfdaten gehen ungekürzt ins PGN, der Zähldeckel allein reicht nicht.</summary>
    [Fact]
    public async Task Import_AtTheCharsCap_TheOverflowingGameFails_ASmallerOneBehindStillFits()
    {
        var probe = await CreateUserAsync("probe");
        SetUser(probe.Id);
        var ids = (await ImportAsync(TwoGames)).Ids;
        var big = (await _db.SavedGames.SingleAsync(g => g.Id == ids[0])).Pgn.Length;
        var small = (await _db.SavedGames.SingleAsync(g => g.Id == ids[1])).Pgn.Length;
        Assert.True(big > small + 10);

        var user = await CreateUserAsync();
        SetUser(user.Id);
        _service.PgnCharsPerUserCap = small + 10;
        var res = await ImportAsync(TwoGames);

        Assert.Equal(1, res.Imported);
        Assert.Equal((1, "quota", "Anna"), (Assert.Single(res.Failed).Index, res.Failed[0].Reason, res.Failed[0].White));
        Assert.Equal("Carla", (await _db.SavedGames.SingleAsync(g => g.UserId == user.Id)).White);
    }

    /// <summary>„Partie speichern" (Extension) am Zähldeckel: eine NEUE Partie wird abgewiesen (eine
    /// <see cref="ArgumentException"/> — der Controller antwortet wie bisher 400), das Heilen einer schon
    /// gespeicherten Partie geht weiter.</summary>
    [Fact]
    public async Task Save_AtTheGameCap_NewGameIsRefused_ButHealingAKnownGameStillWorks()
    {
        var user = await CreateUserAsync();
        _service.GamesPerUserCap = 1;
        var saved = await _service.SaveAsync(user.Id, new SaveGameInputDto { Source = "lichess", Moves = new() { "e4", "c5" }, ExternalId = "cap-1" });

        var ex = await Assert.ThrowsAsync<SavedGameQuotaException>(() => _service.SaveAsync(user.Id,
            new SaveGameInputDto { Source = "lichess", Moves = new() { "d4" }, ExternalId = "cap-2" }));
        Assert.IsAssignableFrom<ArgumentException>(ex);

        var healed = await _service.SaveAsync(user.Id, new SaveGameInputDto { Source = "lichess", Moves = new() { "e4", "c5", "Nf3" }, ExternalId = "cap-1" });
        Assert.Equal(saved.Id, healed.Id);
        Assert.Equal(3, (await _db.SavedGames.SingleAsync()).MoveCount);
    }

    /// <summary>Die Zugliste der Extension ging ungeprüft ins PGN — ein „Zug" mit Megabytes Text machte jede Partie
    /// beliebig groß, der Zähldeckel allein wäre kein Größendeckel.</summary>
    [Fact]
    public async Task Save_OverlongMoveToken_IsRejected()
    {
        var user = await CreateUserAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveAsync(user.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", new string('x', SavedGameService.MaxSanLength + 1) }, ExternalId = "long-1",
        }));
        Assert.Equal(0, await _db.SavedGames.CountAsync());
        await _service.SaveAsync(user.Id, new SaveGameInputDto { Source = "lichess", Moves = new() { "e4", "exd8=Q+!" }, ExternalId = "ok-1" });
    }

    /// <summary>Die Belegung (Zahl + Zeichensumme) muss der ECHTE Provider in EINE Abfrage übersetzen — InMemory
    /// rechnet jeden Ausdruck, ein <c>string.Length</c> ohne SQL-Gegenstück fiele erst auf MariaDB auf.</summary>
    [Fact]
    public void UsageQuery_TranslatesToOneAggregateQuery_OnTheRealProvider()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql("server=localhost;database=x;user=x;password=x", DbServerVersion.Current)
            .Options);
        var sql = SavedGameService.UsageQuery(db, 7).ToQueryString();
        Assert.Contains("COUNT(*)", sql);
        Assert.Contains("SUM(", sql);
        Assert.Contains("CHAR_LENGTH", sql);
        Assert.Contains("GROUP BY", sql);
    }
}
