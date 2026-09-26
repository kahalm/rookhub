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

/// <summary>„Ähnliche Meisterpartien" (0.544.0): die längste gemeinsame Zugfolge mit kommentierten Partien des Rohbestands,
/// die Abzweigung benannt, aufgefüllt aus flacheren Stufen.</summary>
public class SimilarGamesTests : IDisposable
{
    private readonly AppDbContext _db;

    public SimilarGamesTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private SimilarGamesService Service() => new(_db, new LibraryGameService(_db, null!));

    // Spanisch: 1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 4.Ba4 Nf6 5.O-O Be7 6.Re1 b5 7.Bb3 d6
    private const string Pgn = "[White \"Ich\"]\n[Black \"Gegner\"]\n[Opening \"Ruy Lopez\"]\n\n"
        + "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 6. Re1 b5 7. Bb3 d6 *";

    private async Task<(int UserId, int GameId, string Token)> SeedAsync(string pgn = Pgn)
    {
        var user = new AppUser { Username = "u" + Guid.NewGuid().ToString("N")[..8], PasswordHash = "x" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        var game = new SavedGame
        {
            UserId = user.Id, Source = "lichess", Pgn = pgn, ShareToken = Guid.NewGuid().ToString("N")[..20], White = "Ich", Black = "Gegner",
        };
        _db.SavedGames.Add(game);
        await _db.SaveChangesAsync();
        return (user.Id, game.Id, game.ShareToken);
    }

    private async Task<LibraryGame> MasterAsync(string line, int score, string white, int commented = 5,
        LibraryGameStatus status = LibraryGameStatus.New)
    {
        var g = new LibraryGame
        {
            OpeningLine = line, White = white, Black = "B", Event = "E", PlayedOn = new DateOnly(2001, 1, 1), Annotator = "A",
            Score = score, CommentedPlies = commented, Status = status, Pgn = "1. e4 *",
        };
        _db.LibraryGames.Add(g);
        await _db.SaveChangesAsync();
        return g;
    }

    [Fact]
    public async Task DeepestSharedLine_First_ThenFilledFromShallowerLevels_WithTheDeviationNamed()
    {
        var (userId, gameId, _) = await SeedAsync();
        await MasterAsync("e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O Be7 Re1 b5 Bb3 O-O c3 d5", 70, "Marshall");   // bis 7.Bb3, dann 7...O-O
        await MasterAsync("e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O b5", 90, "Archangelsk");                      // bis 5.O-O, dann 5...b5
        await MasterAsync("e4 e5 Nf3 Nc6 Bb5 Nf6 O-O Nxe4", 99, "Berlin");                               // bis 3.Bb5, dann 3...Nf6
        await MasterAsync("e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O Be7 Re1 b5 Bb3 O-O", 60, "Unkommentiert", commented: 0);
        await MasterAsync("e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O Be7 Re1 b5 Bb3 O-O", 95, "Dublette", status: LibraryGameStatus.Duplicate);
        await MasterAsync("d4 d5 c4", 100, "Anderes");

        var dto = (await Service().ForOwnAsync(userId, gameId))!;

        Assert.Equal("Ruy Lopez", dto.Opening);
        Assert.Equal(13, dto.SharedPlies);
        Assert.Equal("1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 4.Ba4 Nf6 5.O-O Be7 6.Re1 b5 7.Bb3", dto.SharedLine);
        Assert.Equal(["Marshall", "Archangelsk", "Berlin"], dto.Items.Select(i => i.Game.White));
        var marshall = dto.Items[0];
        Assert.Equal((13, "7.Bb3", "7...O-O", "7...d6"), (marshall.SharedPlies, marshall.LastSharedMove, marshall.MasterMove, marshall.GameMove));
        Assert.Equal((9, "5...b5", "5...Be7"), (dto.Items[1].SharedPlies, dto.Items[1].MasterMove, dto.Items[1].GameMove));
        Assert.Equal((5, "3...Nf6", "3...a6"), (dto.Items[2].SharedPlies, dto.Items[2].MasterMove, dto.Items[2].GameMove));
    }

    [Fact]
    public async Task TooShortATie_AnotherStart_OrAForeignGame_GiveNothing()
    {
        var (userId, gameId, _) = await SeedAsync();
        await MasterAsync("e4 e5 Nf3 Nf6", 90, "Petrov");   // nur drei gemeinsame Halbzüge — „auch 1.e4" ist keine Ähnlichkeit
        Assert.Empty((await Service().ForOwnAsync(userId, gameId))!.Items);
        Assert.Null(await Service().ForOwnAsync(userId + 99, gameId));

        var (u2, g2, _) = await SeedAsync("[FEN \"8/8/8/8/8/8/4k3/4K2R w K - 0 1\"]\n\n1. Rh2+ *");
        Assert.Empty((await Service().ForOwnAsync(u2, g2))!.Items);
    }

    [Fact]
    public async Task SharedLink_AnonymousSeesItToo_WithWhatIsAlreadyPlayable()
    {
        var (_, _, token) = await SeedAsync();
        var master = await MasterAsync("e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O b5", 90, "Archangelsk");
        var owner = new AppUser { Username = "pool", PasswordHash = "x" };
        _db.AppUsers.Add(owner);
        await _db.SaveChangesAsync();
        _db.GameAnalyses.Add(new GameAnalysis
        {
            UserId = owner.Id, Pgn = "x", StartFen = "startpos", Status = GameAnalysisStatus.Done, LibraryGameId = master.Id, IsPublic = true,
        });
        await _db.SaveChangesAsync();

        var c = new SimilarGamesController(Service()) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var dto = Assert.IsType<SimilarGamesDto>(Assert.IsType<OkObjectResult>((await c.Shared(token, default)).Result).Value);

        var item = Assert.Single(dto.Items);
        Assert.True(item.Game.InPool);
        Assert.False(item.Game.Requested);
        Assert.NotNull(item.Game.GameAnalysisId);
        Assert.IsType<NotFoundResult>((await c.Shared("unbekannt", default)).Result);
    }

    [Fact]
    public async Task OwnEndpoint_ForeignGameIs404()
    {
        var (userId, gameId, _) = await SeedAsync();
        var c = new SimilarGamesController(Service())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, (userId + 1).ToString())], "t")),
                },
            },
        };
        Assert.IsType<NotFoundResult>((await c.Own(gameId, default)).Result);
    }
}
