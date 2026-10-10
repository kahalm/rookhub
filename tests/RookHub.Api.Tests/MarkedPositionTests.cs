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

/// <summary>„+"-Markierungen guter Stellungen: eine je Nutzer und Stellung, Herkunft nur ergänzt, fremde Partie verworfen.</summary>
public class MarkedPositionTests : IDisposable
{
    private const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
    private readonly AppDbContext _db;
    private readonly MarkedPositionService _service;
    private readonly MarkedPositionsController _controller;

    public MarkedPositionTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _service = new MarkedPositionService(_db);
        _controller = new MarkedPositionsController(_service);
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId) => _controller.ControllerContext = new ControllerContext
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "Test")),
        },
    };

    private async Task<SavedGame> GameAsync(int userId)
    {
        if (!_db.AppUsers.Any(u => u.Id == userId))
            _db.AppUsers.Add(new AppUser { Id = userId, Username = "u" + userId, PasswordHash = "x" });
        var g = new SavedGame { UserId = userId, Source = "pgn", Pgn = "1. e4 *", ShareToken = "mp" + userId, CreatedAt = DateTime.UtcNow };
        _db.SavedGames.Add(g);
        await _db.SaveChangesAsync();
        return g;
    }

    [Fact]
    public async Task Mark_SamePositionTwice_IsOneRow_AndOriginOnlyFilledIn()
    {
        var game = await GameAsync(997101);

        await _service.MarkAsync(997101, new MarkPositionInputDto { Fen = AfterE4, Context = "board" });
        var (mark, err) = await _service.MarkAsync(997101, new MarkPositionInputDto
        {
            Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 7", Context = "mistake",
            SavedGameId = game.Id, Ply = 1, BestUci = "E7E5",
        });

        Assert.Null(err);
        Assert.Single(_db.MarkedPositions);
        Assert.Equal("board", mark!.Context);
        Assert.Equal(game.Id, mark.SavedGameId);
        Assert.Equal(1, mark.Ply);
        Assert.Equal("e7e5", mark.BestUci);
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3", mark.PositionKey);
    }

    [Fact]
    public async Task Mark_ForeignGame_AndBadValues_AreDropped()
    {
        var fremd = await GameAsync(997102);

        var (mark, _) = await _service.MarkAsync(997103, new MarkPositionInputDto
        {
            Fen = AfterE4, SavedGameId = fremd.Id, BestUci = "zz99", Context = "hack", Ply = -3,
        });

        Assert.Null(mark!.SavedGameId);
        Assert.Null(mark.BestUci);
        Assert.Null(mark.Ply);
        Assert.Equal("analysis", mark.Context);
    }

    [Fact]
    public async Task Mark_InvalidFen_IsRejected()
    {
        var (mark, err) = await _service.MarkAsync(997104, new MarkPositionInputDto { Fen = "kein fen" });
        Assert.Null(mark);
        Assert.Equal("invalidFen", err);
    }

    [Fact]
    public async Task Controller_MarkListUnmark_PerUser()
    {
        SetUser(997105);
        Assert.IsType<OkObjectResult>((await _controller.Mark(new MarkPositionInputDto { Fen = AfterE4 }, default)).Result);
        await _service.MarkAsync(997106, new MarkPositionInputDto { Fen = AfterE4 });

        var list = Assert.IsType<List<MarkedPositionDto>>(Assert.IsType<OkObjectResult>((await _controller.List(default)).Result).Value);
        Assert.Single(list);

        Assert.IsType<NoContentResult>(await _controller.Unmark(AfterE4.Replace(" 0 1", " 4 9"), default));
        Assert.IsType<NotFoundResult>(await _controller.Unmark(AfterE4, default));
        Assert.Single(_db.MarkedPositions); // die Markierung des anderen Nutzers bleibt
    }
}
