using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// „Remember line": Kursname wird über den Chessable-Bearer aufgelöst — von der Extension
/// mitgeliefert (Vorrang), sonst serverseitig aus der gecachten Kursliste, ohne Live-Abruf
/// (N8-006). Ohne Bearer/Treffer bleibt der Name leer (kein Fehler).
/// </summary>
public class RememberedPositionServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly RememberedPositionService _svc;

    private const string Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public RememberedPositionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!" })
            .Build();
        _encryption = new EncryptionService(config);

        // Ohne ChessableProxyService: der Dienst hat keinen Weg mehr zu Chessable (N8-006).
        _svc = new RememberedPositionService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedCredAsync(int userId, string? cachedJson = null, DateTime? blockedAt = null)
    {
        _db.AppUsers.Add(new AppUser { Id = userId, Username = $"u{userId}", PasswordHash = "x" });
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = userId,
            EncryptedBearer = _encryption.Encrypt("bearer"),
            CachedCoursesJson = cachedJson,
            BlockedAt = blockedAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private static string CacheJson(params (string bid, string name)[] courses)
        => JsonSerializer.Serialize(courses.Select(c => new ChessableCourseDto(c.bid, c.name)).ToList());

    // ---- Codereview F4-014: die Liste rendert SourceUrl als Link ----

    [Theory]
    [InlineData("https://www.chessable.com/variation/123/", true)]
    [InlineData("/analysis/jobs", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,hi", false)]
    [InlineData("//phish.example/x", false)]
    [InlineData("/\\phish.example/x", false)]
    [InlineData("ftp://example.org/x", false)]
    public async Task SaveAsync_KeepsOnlyHttpLinksAndAppPaths(string raw, bool kept)
    {
        _db.AppUsers.Add(new AppUser { Id = 1, Username = "u1", PasswordHash = "x" });
        await _db.SaveChangesAsync();

        var result = await _svc.SaveAsync(1, new RememberLineInputDto { Fen = Fen, SourceUrl = raw });

        Assert.Equal(kept ? raw : null, result.SourceUrl);
        Assert.Equal(kept ? raw : null, (await _db.RememberedPositions.SingleAsync()).SourceUrl);
    }

    [Fact]
    public async Task ListAsync_DoesNotHandOutAnEarlierStoredUnsafeLink()
    {
        _db.AppUsers.Add(new AppUser { Id = 1, Username = "u1", PasswordHash = "x" });
        _db.RememberedPositions.Add(new RememberedPosition
        {
            UserId = 1, Fen = Fen, SourceUrl = "javascript:alert(1)", CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        Assert.Null(Assert.Single(await _svc.ListAsync(1)).SourceUrl);
    }

    [Fact]
    public async Task SaveAsync_ProvidedCourseName_TakesPrecedence()
    {
        await SeedCredAsync(1, CacheJson(("999", "Cached Name")));
        var dto = new RememberLineInputDto { Fen = Fen, CourseId = "999", CourseName = "Extension Name" };

        var result = await _svc.SaveAsync(1, dto);

        Assert.Equal("Extension Name", result.CourseName);
    }

    [Fact]
    public async Task SaveAsync_NoName_ResolvesFromCachedCourseList()
    {
        await SeedCredAsync(1, CacheJson(("116242", "Lifetime Repertoires: 1.e4"), ("999", "Other")));
        var dto = new RememberLineInputDto { Fen = Fen, CourseId = "116242" };

        var result = await _svc.SaveAsync(1, dto);

        Assert.Equal("Lifetime Repertoires: 1.e4", result.CourseName);
    }

    [Fact]
    public async Task SaveAsync_CacheMiss_NoLiveFetch_NameStaysEmpty_UntilTheCacheKnowsIt()
    {
        // N8-006: kein Live-Abruf bei Chessable (am Schalter Chessable:Enabled und am Bearer-Breaker vorbei, je Anfrage
        // auslösbar) — die Liste trägt den Namen später aus dem Cache nach.
        await SeedCredAsync(1, CacheJson(("999", "Other")));
        var dto = new RememberLineInputDto { Fen = Fen, CourseId = "116242" };

        var result = await _svc.SaveAsync(1, dto);

        Assert.True(result.Id > 0);
        Assert.Null(result.CourseName);

        (await _db.ChessableCredentials.SingleAsync()).CachedCoursesJson = CacheJson(("116242", "Later Cached"));
        await _db.SaveChangesAsync();
        Assert.Equal("Later Cached", (await _svc.ListAsync(1)).Single().CourseName);
    }

    [Fact]
    public async Task SaveAsync_NoCredential_LeavesNameNull()
    {
        _db.AppUsers.Add(new AppUser { Id = 1, Username = "u1", PasswordHash = "x" });
        await _db.SaveChangesAsync();
        var dto = new RememberLineInputDto { Fen = Fen, CourseId = "116242" };

        var result = await _svc.SaveAsync(1, dto);

        Assert.Null(result.CourseName);
    }

    [Fact]
    public async Task SaveAsync_CorruptCache_StillSavesWithoutName()
    {
        await SeedCredAsync(1, cachedJson: "{kaputt");
        var dto = new RememberLineInputDto { Fen = Fen, CourseId = "116242" };

        var result = await _svc.SaveAsync(1, dto);

        Assert.True(result.Id > 0);
        Assert.Null(result.CourseName);
    }

    [Fact]
    public async Task ListAsync_BackfillsNameFromCacheForOldEntries()
    {
        // Alt-Eintrag ohne Namen (vor dem Feature gespeichert).
        _db.AppUsers.Add(new AppUser { Id = 1, Username = "u1", PasswordHash = "x" });
        _db.RememberedPositions.Add(new RememberedPosition
        {
            UserId = 1, Fen = Fen, CourseId = "116242", CourseName = null, CreatedAt = DateTime.UtcNow,
        });
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = 1, EncryptedBearer = _encryption.Encrypt("bearer"),
            CachedCoursesJson = CacheJson(("116242", "Backfilled Course")),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var list = await _svc.ListAsync(1);

        Assert.Single(list);
        Assert.Equal("Backfilled Course", list[0].CourseName);
    }

    [Fact]
    public async Task ListAsync_AttachesLatestAnalysisJob_MatchedOnNormalizedFen()
    {
        await SeedCredAsync(1);
        _db.RememberedPositions.Add(new RememberedPosition { UserId = 1, Fen = Fen, CourseId = "1", CreatedAt = DateTime.UtcNow });
        _db.RememberedPositions.Add(new RememberedPosition { UserId = 1, Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", CreatedAt = DateTime.UtcNow.AddMinutes(-1) });
        _db.AnalysisJobs.AddRange(
            new AnalysisJob { UserId = 1, Fen = Fen.Replace(" 0 1", " 5 9"), EngineId = "e", TargetDepth = 30, MultiPv = 2,
                Status = AnalysisJobStatus.Paused, ReachedDepth = 18, UpdatedAt = DateTime.UtcNow.AddHours(-2),
                ResultJson = "{\"pvs\":[{\"cp\":10}]}", EvalText = "+0.10" },
            new AnalysisJob { UserId = 1, Fen = Fen, EngineId = "e", TargetDepth = 40, MultiPv = 3,
                Status = AnalysisJobStatus.Done, ReachedDepth = 40, UpdatedAt = DateTime.UtcNow,
                ResultJson = "{\"pvs\":[{\"cp\":35},{\"cp\":20}]}", EvalText = "+0.35" },
            new AnalysisJob { UserId = 2, Fen = Fen, EngineId = "e", TargetDepth = 30, MultiPv = 1, Status = AnalysisJobStatus.Done, ReachedDepth = 30 });
        await _db.SaveChangesAsync();

        var list = await _svc.ListAsync(1);

        var withJob = list.Single(p => p.Fen == Fen);
        Assert.NotNull(withJob.Analysis);
        Assert.Equal("done", withJob.Analysis!.Status);        // der jüngere Auftrag gewinnt
        Assert.Equal(40, withJob.Analysis.ReachedDepth);
        Assert.Equal(3, withJob.Analysis.MultiPv);
        Assert.Equal("+0.35", withJob.Analysis.EvalText);   // aus der EvalText-Spalte, ohne die Roh-Zeile zu laden
        Assert.Null(list.Single(p => p.Fen != Fen).Analysis);   // Stellung ohne Auftrag bleibt ohne Info
    }

    [Fact]
    public async Task DeleteAsync_RemovesOwnEntry_NotForeign()
    {
        var mine = await _svc.SaveAsync(1, new RememberLineInputDto { Fen = Fen, CourseName = "c" });
        await _svc.SaveAsync(2, new RememberLineInputDto { Fen = Fen, CourseName = "c" });

        Assert.False(await _svc.DeleteAsync(1, 99999));            // unbekannt
        Assert.False(await _svc.DeleteAsync(2, mine.Id));          // fremder Eintrag → nicht löschbar
        Assert.True(await _svc.DeleteAsync(1, mine.Id));           // eigener → gelöscht
        Assert.Empty(await _svc.ListAsync(1));
    }
}
