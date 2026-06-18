using System.Security.Claims;
using System.Text;
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

public class RepertoireControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RepertoireService _service;
    private readonly RepertoireController _controller;

    public RepertoireControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        _service = new RepertoireService(_db, new RepertoireAnalyzeService(_db, cache));
        _controller = new RepertoireController(_service, ReprocessTestHelper.Build(_db));
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
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

    private async Task<Repertoire> CreateRepertoireAsync(int userId, string name = "Test Rep")
    {
        var rep = new Repertoire { UserId = userId, Name = name };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        return rep;
    }

    // ---- GetAll ----

    [Fact]
    public async Task GetAll_ReturnsOk_WithRepertoires()
    {
        var user = await CreateUserAsync();
        await CreateRepertoireAsync(user.Id, "Rep1");
        await CreateRepertoireAsync(user.Id, "Rep2");
        SetUser(user.Id);

        var result = await _controller.GetAll();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var reps = okResult.Value as List<RepertoireDto>;
        Assert.Equal(2, reps!.Count);
    }

    [Fact]
    public async Task GetAll_ReturnsEmpty_ForNewUser()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);

        var result = await _controller.GetAll();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var reps = okResult.Value as List<RepertoireDto>;
        Assert.Empty(reps!);
    }

    // ---- Create ----

    [Fact]
    public async Task Create_ReturnsCreated()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);

        var result = await _controller.Create(new CreateRepertoireDto
        {
            Name = "New Rep",
            Description = "Desc",
            IsPublic = true
        });

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var rep = createdResult.Value as RepertoireDto;
        Assert.NotNull(rep);
        Assert.Equal("New Rep", rep.Name);
    }

    // ---- GetById ----

    [Fact]
    public async Task GetById_ReturnsOk()
    {
        var user = await CreateUserAsync();
        var rep = await CreateRepertoireAsync(user.Id);
        SetUser(user.Id);

        var result = await _controller.GetById(rep.Id);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenWrongUser()
    {
        var user1 = await CreateUserAsync("user1");
        var user2 = await CreateUserAsync("user2");
        var rep = await CreateRepertoireAsync(user1.Id);
        SetUser(user2.Id);

        var result = await _controller.GetById(rep.Id);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ---- Update ----

    [Fact]
    public async Task Update_ReturnsOk()
    {
        var user = await CreateUserAsync();
        var rep = await CreateRepertoireAsync(user.Id);
        SetUser(user.Id);

        var result = await _controller.Update(rep.Id, new UpdateRepertoireDto { Name = "Updated" });

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var updated = okResult.Value as RepertoireDto;
        Assert.Equal("Updated", updated!.Name);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenMissing()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);

        var result = await _controller.Update(99999, new UpdateRepertoireDto { Name = "x" });

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ---- Delete ----

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var user = await CreateUserAsync();
        var rep = await CreateRepertoireAsync(user.Id);
        SetUser(user.Id);

        var result = await _controller.Delete(rep.Id);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenMissing()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);

        var result = await _controller.Delete(99999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---- UploadFile ----

    [Fact]
    public async Task UploadFile_ReturnsBadRequest_WhenNoFile()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);

        var result = await _controller.UploadFile(1, null!);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UploadFile_ReturnsBadRequest_WhenNotPgn()
    {
        var user = await CreateUserAsync();
        var rep = await CreateRepertoireAsync(user.Id);
        SetUser(user.Id);

        var file = CreateFormFile("test.txt", "[Event \"Test\"] 1. e4 e5");

        var result = await _controller.UploadFile(rep.Id, file);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UploadFile_ReturnsOk_WithValidPgn()
    {
        var user = await CreateUserAsync();
        var rep = await CreateRepertoireAsync(user.Id);
        SetUser(user.Id);

        var file = CreateFormFile("game.pgn", "[Event \"Test\"] 1. e4 e5");

        var result = await _controller.UploadFile(rep.Id, file);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task UploadFile_ReturnsNotFound_WhenRepertoireMissing()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);

        var file = CreateFormFile("game.pgn", "[Event \"Test\"] 1. e4 e5");

        var result = await _controller.UploadFile(99999, file);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ---- DownloadFile ----

    [Fact]
    public async Task DownloadFile_ReturnsFile()
    {
        var user = await CreateUserAsync();
        var rep = await CreateRepertoireAsync(user.Id);
        _db.RepertoireFiles.Add(new RepertoireFile
        {
            RepertoireId = rep.Id,
            FileName = "game.pgn",
            PgnContent = "[Event \"Test\"] 1. e4",
            FileSize = 20
        });
        await _db.SaveChangesAsync();
        var fileId = (await _db.RepertoireFiles.FirstAsync()).Id;
        SetUser(user.Id);

        var result = await _controller.DownloadFile(rep.Id, fileId);

        Assert.IsType<FileContentResult>(result);
    }

    [Fact]
    public async Task DownloadFile_ReturnsNotFound_WhenMissing()
    {
        var user = await CreateUserAsync();
        var rep = await CreateRepertoireAsync(user.Id);
        SetUser(user.Id);

        var result = await _controller.DownloadFile(rep.Id, 99999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---- DeleteFile ----

    [Fact]
    public async Task DeleteFile_ReturnsNoContent()
    {
        var user = await CreateUserAsync();
        var rep = await CreateRepertoireAsync(user.Id);
        _db.RepertoireFiles.Add(new RepertoireFile
        {
            RepertoireId = rep.Id,
            FileName = "game.pgn",
            PgnContent = "[Event \"Test\"]",
            FileSize = 14
        });
        await _db.SaveChangesAsync();
        var fileId = (await _db.RepertoireFiles.FirstAsync()).Id;
        SetUser(user.Id);

        var result = await _controller.DeleteFile(rep.Id, fileId);

        Assert.IsType<NoContentResult>(result);
    }

    // ---- GetCombinedPgn ----

    [Fact]
    public async Task GetCombinedPgn_ReturnsContent()
    {
        var user = await CreateUserAsync();
        var rep = await CreateRepertoireAsync(user.Id);
        _db.RepertoireFiles.Add(new RepertoireFile
        {
            RepertoireId = rep.Id,
            FileName = "game.pgn",
            PgnContent = "[Event \"Test\"] 1. e4",
            FileSize = 20
        });
        await _db.SaveChangesAsync();
        SetUser(user.Id);

        var result = await _controller.GetCombinedPgn(rep.Id);

        Assert.IsType<ContentResult>(result);
    }

    [Fact]
    public async Task GetCombinedPgn_ReturnsNotFound_WhenMissing()
    {
        var user = await CreateUserAsync();
        SetUser(user.Id);

        var result = await _controller.GetCombinedPgn(99999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    private static IFormFile CreateFormFile(string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream"
        };
    }
}
