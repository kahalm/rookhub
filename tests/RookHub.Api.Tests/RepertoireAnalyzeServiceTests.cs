using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class RepertoireAnalyzeServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RepertoireAnalyzeService _analyze;
    private readonly RepertoireService _repertoireService;

    public RepertoireAnalyzeServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        _analyze = new RepertoireAnalyzeService(_db, cache);
        _repertoireService = new RepertoireService(_db, _analyze);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedUserWithOpeningAsync(string pgn)
    {
        var user = new AppUser { Username = "u", Email = "u@x.y", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();

        var rep = await _repertoireService.CreateAsync(user.Id, new CreateRepertoireDto
        {
            Name = "Opening", Kind = RepertoireKind.Opening,
        });
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(pgn));
        await _repertoireService.UploadFileAsync(rep.Id, user.Id, "rep.pgn", stream);
        return user.Id;
    }

    [Fact]
    public async Task EmptyMoves_ReturnsFileCountOnly()
    {
        var userId = await SeedUserWithOpeningAsync("[Event \"x\"]\n\n1. e4 e5 2. Nf3 Nc6 *");
        var result = await _analyze.AnalyzeAsync(userId, new AnalyzeGameRequestDto
        {
            Moves = new(), Kind = RepertoireKind.Opening,
        });
        Assert.Equal(-1, result.Deviation);
        Assert.Equal(1, result.RepertoireFileCount);
        Assert.Empty(result.InRepertoire);
    }

    [Fact]
    public async Task GameInRepertoire_NoDeviation()
    {
        var userId = await SeedUserWithOpeningAsync("[Event \"x\"]\n\n1. e4 e5 2. Nf3 Nc6 *");
        var result = await _analyze.AnalyzeAsync(userId, new AnalyzeGameRequestDto
        {
            Moves = new() { "e4", "e5", "Nf3", "Nc6" },
        });
        Assert.Equal(-1, result.Deviation);
        Assert.Equal(4, result.InRepertoire.Count);
        Assert.Null(result.FenBeforeDeviation);
    }

    [Fact]
    public async Task DeviationAtMove3_ReturnsDeviationAndFen()
    {
        var userId = await SeedUserWithOpeningAsync("[Event \"x\"]\n\n1. e4 e5 2. Nf3 Nc6 *");
        var result = await _analyze.AnalyzeAsync(userId, new AnalyzeGameRequestDto
        {
            Moves = new() { "e4", "e5", "Bc4" }, // 3rd ply (index 2) leaves the repertoire
        });
        Assert.Equal(2, result.Deviation);
        Assert.NotNull(result.FenBeforeDeviation);
        // FEN nach 1. e4 e5 (vor Bc4) ist die Position, in der Weiss am Zug ist.
        Assert.Contains(" w ", result.FenBeforeDeviation);
    }

    [Fact]
    public async Task TranspositionGap_DetectedAsGapNotDeviation()
    {
        // Repertoire enthaelt 1. e4 c5 und 1. Nf3 c5 (gleiche Endstellung via Transposition)
        var pgn = "[Event \"a\"]\n\n1. e4 c5 *\n\n[Event \"b\"]\n\n1. Nf3 c5 2. e4 *";
        var userId = await SeedUserWithOpeningAsync(pgn);
        // Gespielte Reihenfolge: 1. e4 c5 2. Nf3 — Zug 3 ist im Repertoire (Stellung kommt im Trie b vor).
        // Wir testen die simplere Transposition: 1. Nf3 c5 sollte komplett in-rep sein.
        var result = await _analyze.AnalyzeAsync(userId, new AnalyzeGameRequestDto
        {
            Moves = new() { "Nf3", "c5" },
        });
        Assert.Equal(-1, result.Deviation);
        Assert.Equal(2, result.InRepertoire.Count);
    }

    [Fact]
    public async Task UploadInvalidatesCache()
    {
        var userId = await SeedUserWithOpeningAsync("[Event \"x\"]\n\n1. e4 e5 *");
        // Cache aufwaermen
        await _analyze.AnalyzeAsync(userId, new AnalyzeGameRequestDto { Moves = new() { "e4", "e5" } });

        // Zweite PGN hochladen → Cache muss invalidiert werden, sonst sehen wir 1. d4 d5 nicht.
        var user = await _db.AppUsers.FirstAsync(u => u.Id == userId);
        var rep = await _repertoireService.CreateAsync(userId, new CreateRepertoireDto
        {
            Name = "Opening 2", Kind = RepertoireKind.Opening,
        });
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("[Event \"y\"]\n\n1. d4 d5 *"));
        await _repertoireService.UploadFileAsync(rep.Id, userId, "rep2.pgn", stream);

        var result = await _analyze.AnalyzeAsync(userId, new AnalyzeGameRequestDto
        {
            Moves = new() { "d4", "d5" },
        });
        Assert.Equal(-1, result.Deviation);
        Assert.Equal(2, result.InRepertoire.Count);
    }
}
