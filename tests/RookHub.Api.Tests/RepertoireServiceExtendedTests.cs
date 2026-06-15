using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class RepertoireServiceExtendedTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RepertoireService _service;

    public RepertoireServiceExtendedTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        _service = new RepertoireService(_db, new RepertoireAnalyzeService(_db, cache));
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserAsync(string username = "testuser")
    {
        var user = new AppUser
        {
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = "hash"
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<(int UserId, int RepId)> CreateRepertoireWithFileAsync()
    {
        var userId = await CreateUserAsync();
        var rep = await _service.CreateAsync(userId, new CreateRepertoireDto { Name = "Test Repertoire" });
        var pgn = "[Event \"Test\"]\n1. e4 e5 *";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(pgn));
        await _service.UploadFileAsync(rep.Id, userId, "game.pgn", stream);
        return (userId, rep.Id);
    }

    #region GetById

    [Fact]
    public async Task GetById_ReturnsRepertoireWithFiles()
    {
        var (userId, repId) = await CreateRepertoireWithFileAsync();

        var result = await _service.GetByIdAsync(repId, userId);

        Assert.Equal("Test Repertoire", result.Name);
        Assert.Single(result.Files);
        Assert.Equal("game.pgn", result.Files[0].FileName);
    }

    [Fact]
    public async Task GetById_NotFound_Throws()
    {
        var userId = await CreateUserAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.GetByIdAsync(99999, userId));
    }

    [Fact]
    public async Task GetById_WrongUser_Throws()
    {
        var (_, repId) = await CreateRepertoireWithFileAsync();
        var otherUser = await CreateUserAsync("other");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.GetByIdAsync(repId, otherUser));
    }

    #endregion

    #region Update

    [Fact]
    public async Task Update_UpdatesName()
    {
        var userId = await CreateUserAsync();
        var rep = await _service.CreateAsync(userId, new CreateRepertoireDto { Name = "Old Name" });

        var result = await _service.UpdateAsync(rep.Id, userId, new UpdateRepertoireDto { Name = "New Name" });

        Assert.Equal("New Name", result.Name);
    }

    [Fact]
    public async Task Update_PartialUpdate_OnlyChangesProvided()
    {
        var userId = await CreateUserAsync();
        var rep = await _service.CreateAsync(userId, new CreateRepertoireDto
        {
            Name = "My Repertoire", Description = "Original desc", IsPublic = false
        });

        var result = await _service.UpdateAsync(rep.Id, userId, new UpdateRepertoireDto { Name = "Updated" });

        Assert.Equal("Updated", result.Name);
        Assert.Equal("Original desc", result.Description);
        Assert.False(result.IsPublic);
    }

    [Fact]
    public async Task UseForExtension_DefaultsTrue_AndCanBeToggled()
    {
        var userId = await CreateUserAsync();
        var rep = await _service.CreateAsync(userId, new CreateRepertoireDto { Name = "Rep" });
        Assert.True(rep.UseForExtension); // Default true (bestehendes Verhalten)

        var off = await _service.UpdateAsync(rep.Id, userId, new UpdateRepertoireDto { UseForExtension = false });
        Assert.False(off.UseForExtension);

        // Teil-Update ohne das Feld laesst es unveraendert.
        var nameOnly = await _service.UpdateAsync(rep.Id, userId, new UpdateRepertoireDto { Name = "Renamed" });
        Assert.False(nameOnly.UseForExtension);

        var on = await _service.UpdateAsync(rep.Id, userId, new UpdateRepertoireDto { UseForExtension = true });
        Assert.True(on.UseForExtension);
    }

    [Fact]
    public async Task Update_NotFound_Throws()
    {
        var userId = await CreateUserAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.UpdateAsync(99999, userId, new UpdateRepertoireDto { Name = "X" }));
    }

    #endregion

    #region DownloadFile

    [Fact]
    public async Task DownloadFile_ReturnsFileContent()
    {
        var (userId, repId) = await CreateRepertoireWithFileAsync();
        var file = await _db.RepertoireFiles.FirstAsync(f => f.RepertoireId == repId);

        var (fileName, content) = await _service.DownloadFileAsync(repId, file.Id, userId);

        Assert.Equal("game.pgn", fileName);
        Assert.Contains("[Event", content);
    }

    [Fact]
    public async Task DownloadFile_NotFound_Throws()
    {
        var userId = await CreateUserAsync();
        var rep = await _service.CreateAsync(userId, new CreateRepertoireDto { Name = "Test" });

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.DownloadFileAsync(rep.Id, 99999, userId));
    }

    #endregion

    #region DeleteFile

    [Fact]
    public async Task DeleteFile_RemovesFile()
    {
        var (userId, repId) = await CreateRepertoireWithFileAsync();
        var file = await _db.RepertoireFiles.FirstAsync(f => f.RepertoireId == repId);

        await _service.DeleteFileAsync(repId, file.Id, userId);

        Assert.Empty(await _db.RepertoireFiles.Where(f => f.RepertoireId == repId).ToListAsync());
    }

    [Fact]
    public async Task DeleteFile_NotFound_Throws()
    {
        var userId = await CreateUserAsync();
        var rep = await _service.CreateAsync(userId, new CreateRepertoireDto { Name = "Test" });

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.DeleteFileAsync(rep.Id, 99999, userId));
    }

    #endregion

    #region GetExtensionList

    [Fact]
    public async Task GetExtensionList_ReturnsList()
    {
        var userId = await CreateUserAsync();
        await _service.CreateAsync(userId, new CreateRepertoireDto { Name = "Rep1" });
        await _service.CreateAsync(userId, new CreateRepertoireDto { Name = "Rep2" });

        var result = await _service.GetExtensionListAsync(userId);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Name == "Rep1");
        Assert.Contains(result, r => r.Name == "Rep2");
    }

    #endregion

    #region UploadFile Validation

    [Fact]
    public async Task UploadFile_InvalidContent_Throws()
    {
        var userId = await CreateUserAsync();
        var rep = await _service.CreateAsync(userId, new CreateRepertoireDto { Name = "Test" });

        var invalidContent = "This is not PGN content at all";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalidContent));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UploadFileAsync(rep.Id, userId, "bad.pgn", stream));
    }

    #endregion

    #region Limits

    [Fact]
    public async Task CreateRepertoire_ExceedsMaxPerUser_Throws()
    {
        var userId = await CreateUserAsync();
        for (var i = 0; i < RepertoireService.MaxRepertoiresPerUser; i++)
        {
            await _service.CreateAsync(userId, new CreateRepertoireDto { Name = $"Rep{i}" });
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(userId, new CreateRepertoireDto { Name = "One Too Many" }));
        Assert.Contains("Maximum", ex.Message);
    }

    [Fact]
    public async Task UploadFile_ExceedsMaxPerRepertoire_Throws()
    {
        var userId = await CreateUserAsync();
        var rep = await _service.CreateAsync(userId, new CreateRepertoireDto { Name = "Test" });

        for (var i = 0; i < RepertoireService.MaxFilesPerRepertoire; i++)
        {
            var pgn = $"[Event \"Game {i}\"]\n1. e4 e5 *";
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(pgn));
            await _service.UploadFileAsync(rep.Id, userId, $"game{i}.pgn", stream);
        }

        var extraPgn = "[Event \"Extra\"]\n1. d4 d5 *";
        using var extraStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(extraPgn));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UploadFileAsync(rep.Id, userId, "extra.pgn", extraStream));
        Assert.Contains("Maximum", ex.Message);
    }

    #endregion
}
