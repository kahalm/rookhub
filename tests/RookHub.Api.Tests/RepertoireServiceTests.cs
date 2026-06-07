using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
        var cache = new MemoryCache(new MemoryCacheOptions());
        _repertoireService = new RepertoireService(_db, new RepertoireAnalyzeService(_db, cache));
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
    public async Task UploadFile_RejectsNonPgnContent()
    {
        var userId = await CreateUserAsync();
        var rep = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto { Name = "Test" });

        // Enthaelt "1." (in "Chapter 1.") aber keinen echten Zug und kein Tag-Pair -> abgelehnt.
        var junk = "Chapter 1. Introduction\nThis is just prose, not a game.";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(junk));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repertoireService.UploadFileAsync(rep.Id, userId, "notes.pgn", stream));
    }

    [Fact]
    public async Task UploadFile_AcceptsHeaderOnlyPgn()
    {
        var userId = await CreateUserAsync();
        var rep = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto { Name = "Test" });

        var pgn = "[Event \"My Game\"]\n[Site \"?\"]\n\n*";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(pgn));
        var file = await _repertoireService.UploadFileAsync(rep.Id, userId, "headers.pgn", stream);
        Assert.Equal("headers.pgn", file.FileName);
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

    [Fact]
    public async Task CreateRepertoire_DefaultKindIsNone()
    {
        var userId = await CreateUserAsync();
        var rep = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto { Name = "Default" });
        Assert.Equal(Models.RepertoireKind.None, rep.Kind);
    }

    [Fact]
    public async Task CreateRepertoire_AcceptsKind()
    {
        var userId = await CreateUserAsync();
        var rep = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto
        {
            Name = "Sicilian",
            Kind = Models.RepertoireKind.Opening
        });
        Assert.Equal(Models.RepertoireKind.Opening, rep.Kind);

        var detail = await _repertoireService.GetByIdAsync(rep.Id, userId);
        Assert.Equal(Models.RepertoireKind.Opening, detail.Kind);
    }

    [Fact]
    public async Task UpdateRepertoire_ChangesKind()
    {
        var userId = await CreateUserAsync();
        var rep = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto { Name = "x" });

        var updated = await _repertoireService.UpdateAsync(rep.Id, userId, new UpdateRepertoireDto
        {
            Kind = Models.RepertoireKind.Endgame
        });
        Assert.Equal(Models.RepertoireKind.Endgame, updated.Kind);

        // Andere Felder unangetastet, wenn nicht im Update gesetzt
        Assert.Equal("x", updated.Name);
    }

    [Fact]
    public async Task GetExtensionListAsync_FiltersByKind()
    {
        var userId = await CreateUserAsync();
        await _repertoireService.CreateAsync(userId, new CreateRepertoireDto { Name = "open", Kind = Models.RepertoireKind.Opening });
        await _repertoireService.CreateAsync(userId, new CreateRepertoireDto { Name = "end", Kind = Models.RepertoireKind.Endgame });
        await _repertoireService.CreateAsync(userId, new CreateRepertoireDto { Name = "none" });

        var all = await _repertoireService.GetExtensionListAsync(userId);
        Assert.Equal(3, all.Count);

        var openings = await _repertoireService.GetExtensionListAsync(userId, Models.RepertoireKind.Opening);
        Assert.Single(openings);
        Assert.Equal("open", openings[0].Name);
        Assert.Equal(Models.RepertoireKind.Opening, openings[0].Kind);

        var endgames = await _repertoireService.GetExtensionListAsync(userId, Models.RepertoireKind.Endgame);
        Assert.Single(endgames);
        Assert.Equal("end", endgames[0].Name);

        var middlegames = await _repertoireService.GetExtensionListAsync(userId, Models.RepertoireKind.Middlegame);
        Assert.Empty(middlegames);
    }

    [Fact]
    public async Task GetExtensionListAsync_TotalSizeBytes_AggregatesFiles()
    {
        var userId = await CreateUserAsync();
        var rep = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto { Name = "x", Kind = Models.RepertoireKind.Opening });

        // Direkt RepertoireFiles einfuegen (Upload-Validierung umgehen).
        _db.RepertoireFiles.AddRange(
            new Models.RepertoireFile { RepertoireId = rep.Id, FileName = "a.pgn", PgnContent = "[Event \"a\"]\n1. e4", FileSize = 100 },
            new Models.RepertoireFile { RepertoireId = rep.Id, FileName = "b.pgn", PgnContent = "[Event \"b\"]\n1. d4", FileSize = 250 }
        );
        await _db.SaveChangesAsync();

        var list = await _repertoireService.GetExtensionListAsync(userId, Models.RepertoireKind.Opening);
        Assert.Single(list);
        Assert.Equal(350, list[0].TotalSizeBytes);
        Assert.Equal(2, list[0].FileCount);
    }
}
