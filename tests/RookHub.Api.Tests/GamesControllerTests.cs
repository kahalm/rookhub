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
        _service = new SavedGameService(_db);
        _controller = new GamesController(_service);
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
}
