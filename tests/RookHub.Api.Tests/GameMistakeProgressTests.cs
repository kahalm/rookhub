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

/// <summary>Fehler-Training: was gefunden wurde, bleibt gefunden — und die Übersicht zeigt „x von y · z offen".</summary>
public class GameMistakeProgressTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GameMistakeProgressService _service;
    private readonly GamesController _controller;

    public GameMistakeProgressTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new GameMistakeProgressService(_db);
        _controller = new GamesController(TestServices.SavedGames(_db), _service);
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
        };
    }

    private async Task<SavedGame> GameAsync(int userId, string externalId = "999001")
    {
        _db.AppUsers.Add(new AppUser { Id = userId, Username = "u" + userId, PasswordHash = "x" });
        var game = new SavedGame
        {
            UserId = userId, Source = "chess.com", ExternalId = externalId, Pgn = "1. e4 e5 *",
            MoveCount = 2, ShareToken = "tok" + externalId, CreatedAt = DateTime.UtcNow,
        };
        _db.SavedGames.Add(game);
        await _db.SaveChangesAsync();
        return game;
    }

    [Fact]
    public async Task Record_StoresProgress_AndCountsOpenTasks()
    {
        var game = await GameAsync(9901);

        var stand = await _service.RecordAsync(9901, game.Id, total: 7, solved: new[] { 12, 34 });

        Assert.NotNull(stand);
        Assert.Equal(7, stand!.Total);
        Assert.Equal(2, stand.Solved);
        Assert.Equal(5, stand.Open);
        Assert.Equal(new[] { 12, 34 }, stand.SolvedPlies);
    }

    [Fact]
    public async Task Record_IsAdditive_SecondRunNeverTakesAwayWhatWasFound()
    {
        var game = await GameAsync(9902);

        await _service.RecordAsync(9902, game.Id, 7, new[] { 12, 34 });
        var zweiter = await _service.RecordAsync(9902, game.Id, 7, new[] { 34, 56 });

        Assert.Equal(new[] { 12, 34, 56 }, zweiter!.SolvedPlies);
        Assert.Equal(3, zweiter.Solved);
        Assert.Equal(4, zweiter.Open);
        Assert.Single(_db.GameMistakeProgresses);
    }

    [Fact]
    public async Task Record_Twice_WithTheSameRun_ChangesNothing()
    {
        var game = await GameAsync(9903);

        var erster = await _service.RecordAsync(9903, game.Id, 4, new[] { 8 });
        var wiederholt = await _service.RecordAsync(9903, game.Id, 4, new[] { 8 });

        Assert.Equal(erster!.Solved, wiederholt!.Solved);
        Assert.Equal(erster.Open, wiederholt.Open);
    }

    [Fact]
    public async Task Record_ForeignGame_IsNotFound()
    {
        var game = await GameAsync(9904);

        Assert.Null(await _service.RecordAsync(9905, game.Id, 3, new[] { 1 }));
        Assert.Empty(_db.GameMistakeProgresses);
    }

    [Fact]
    public async Task Record_ClampsNonsense_TotalNeverBelowFound_PliesInRange()
    {
        var game = await GameAsync(9906);

        // Halbzüge außerhalb der Partie (negativ, jenseits der 600) fallen weg; „0 Aufgaben" bei zwei
        // gefundenen wäre eine Anzeige „2 von 0".
        var stand = await _service.RecordAsync(9906, game.Id, total: 0, solved: new[] { -5, 3, 700, 9 });

        Assert.Equal(new[] { 3, 9 }, stand!.SolvedPlies);
        Assert.Equal(2, stand.Total);
        Assert.Equal(0, stand.Open);
    }

    [Fact]
    public async Task List_CarriesTheProgressOfEachGame()
    {
        var game = await GameAsync(9907);
        await _service.RecordAsync(9907, game.Id, 5, new[] { 2, 4 });
        SetUser(9907);

        var liste = Assert.IsType<OkObjectResult>((await _controller.List()).Result).Value as List<SavedGameDto>;

        var eintrag = Assert.Single(liste!);
        Assert.NotNull(eintrag.Mistakes);
        Assert.Equal(5, eintrag.Mistakes!.Total);
        Assert.Equal(2, eintrag.Mistakes.Solved);
        Assert.Equal(3, eintrag.Mistakes.Open);
    }

    [Fact]
    public async Task List_WithoutTraining_LeavesTheEntryEmpty()
    {
        await GameAsync(9908);
        SetUser(9908);

        var liste = Assert.IsType<OkObjectResult>((await _controller.List()).Result).Value as List<SavedGameDto>;

        Assert.Null(Assert.Single(liste!).Mistakes);
    }

    [Fact]
    public async Task Controller_RecordsAndReadsBack()
    {
        var game = await GameAsync(9909);
        SetUser(9909);

        var gemeldet = Assert.IsType<OkObjectResult>(
            (await _controller.RecordMistakes(game.Id, new MistakeProgressInputDto { Total = 6, Solved = new() { 5 } }, default)).Result).Value;
        var gelesen = Assert.IsType<OkObjectResult>((await _controller.Mistakes(game.Id, default)).Result).Value;

        Assert.Equal(5, ((GameMistakeProgressDto)gemeldet!).Open);
        Assert.Equal(new[] { 5 }, ((GameMistakeProgressDto)gelesen!).SolvedPlies);
    }

    [Fact]
    public async Task Controller_UntrainedGame_HasNoProgressYet()
    {
        var game = await GameAsync(9910);
        SetUser(9910);

        Assert.IsType<NotFoundResult>((await _controller.Mistakes(game.Id, default)).Result);
    }
}
