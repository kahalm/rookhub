using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class ExtensionControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RepertoireService _service;
    private readonly ExtensionController _controller;

    public ExtensionControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var analyzeService = new RepertoireAnalyzeService(_db, cache);
        _service = new RepertoireService(_db, analyzeService);
        var trainingGoalService = new TrainingGoalService(_db);
        _controller = new ExtensionController(_service, analyzeService, trainingGoalService);
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
    public async Task GetRepertoires_WithForeignScope_Forbidden()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, scope: "admin"); // anderer Scope → kein Zugriff
        var result = await _controller.GetRepertoires();
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetPgn_WithForeignScope_Forbidden()
    {
        var user = await CreateUserAsync();
        var rep = new Repertoire { UserId = user.Id, Name = "x" };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        SetUser(user.Id, scope: "admin");
        var result = await _controller.GetPgn(rep.Id);
        Assert.IsType<ForbidResult>(result);
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
    public async Task RecordTrainingActivity_WithForeignScope_Forbidden()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id, scope: "admin");

        var result = await _controller.RecordTrainingActivity(new ChessableActivityInputDto { SecondsActive = 60 });

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(_db.ChessableActivities.Where(a => a.UserId == user.Id));
    }
}
