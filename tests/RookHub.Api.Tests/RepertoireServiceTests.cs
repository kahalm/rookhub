using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class RepertoireServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RepertoireService _repertoireService;

    public RepertoireServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repertoireService = new RepertoireService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserAsync()
    {
        var user = new Models.AppUser
        {
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = "hash"
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task CreateRepertoire_ReturnsNewRepertoire()
    {
        var userId = await CreateUserAsync();
        var result = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto
        {
            Name = "My Opening Book",
            Description = "Sicilian lines",
            IsPublic = false
        });

        Assert.Equal("My Opening Book", result.Name);
        Assert.Equal(0, result.FileCount);
    }

    [Fact]
    public async Task UploadFile_AddsFileToRepertoire()
    {
        var userId = await CreateUserAsync();
        var rep = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto { Name = "Test" });

        var pgnContent = "1. e4 e5 2. Nf3 Nc6 *";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(pgnContent));
        var file = await _repertoireService.UploadFileAsync(rep.Id, userId, "game1.pgn", stream);

        Assert.Equal("game1.pgn", file.FileName);
        Assert.True(file.FileSize > 0);
    }

    [Fact]
    public async Task GetCombinedPgn_CombinesAllFiles()
    {
        var userId = await CreateUserAsync();
        var rep = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto { Name = "Test" });

        var pgn1 = "1. e4 e5 *";
        var pgn2 = "1. d4 d5 *";
        using var s1 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(pgn1));
        using var s2 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(pgn2));
        await _repertoireService.UploadFileAsync(rep.Id, userId, "g1.pgn", s1);
        await _repertoireService.UploadFileAsync(rep.Id, userId, "g2.pgn", s2);

        var combined = await _repertoireService.GetCombinedPgnAsync(rep.Id, userId);
        Assert.Contains("1. e4 e5", combined);
        Assert.Contains("1. d4 d5", combined);
    }

    [Fact]
    public async Task DeleteRepertoire_RemovesFromDb()
    {
        var userId = await CreateUserAsync();
        var rep = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto { Name = "Test" });

        await _repertoireService.DeleteAsync(rep.Id, userId);

        var all = await _repertoireService.GetAllAsync(userId);
        Assert.Empty(all);
    }
}
