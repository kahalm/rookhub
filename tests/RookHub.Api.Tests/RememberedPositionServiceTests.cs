using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// „Remember line": Kursname wird über den Chessable-Bearer aufgelöst — von der Extension
/// mitgeliefert (Vorrang), sonst serverseitig aus der gecachten Kursliste (cache-first) bzw.
/// per Live-Abruf. Ohne Bearer/Treffer bleibt der Name leer (kein Fehler).
/// </summary>
public class RememberedPositionServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly StubHandler _handler;
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

        _handler = new StubHandler();
        var proxy = new ChessableProxyService(new HttpClient(_handler) { BaseAddress = new Uri("http://pc:8080") });
        _svc = new RememberedPositionService(_db, _encryption, proxy, NullLogger<RememberedPositionService>.Instance);
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

    private void ReplyWithCourses(params ChessableCourseDto[] courses)
        => _handler.Reply = (_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(courses) };

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
    public async Task SaveAsync_CacheMiss_LiveFallbackResolvesName()
    {
        await SeedCredAsync(1, cachedJson: null);
        ReplyWithCourses(new ChessableCourseDto("116242", "Live Fetched Course"));
        var dto = new RememberLineInputDto { Fen = Fen, CourseId = "116242" };

        var result = await _svc.SaveAsync(1, dto);

        Assert.Equal("Live Fetched Course", result.CourseName);
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
    public async Task SaveAsync_BlockedBearer_NoLiveFallback()
    {
        await SeedCredAsync(1, cachedJson: null, blockedAt: DateTime.UtcNow);
        _handler.Reply = (_, _) => throw new Exception("must not be called");
        var dto = new RememberLineInputDto { Fen = Fen, CourseId = "116242" };

        var result = await _svc.SaveAsync(1, dto);

        Assert.Null(result.CourseName);
    }

    [Fact]
    public async Task SaveAsync_LiveFetchThrows_StillSavesWithoutName()
    {
        await SeedCredAsync(1, cachedJson: null);
        _handler.Reply = (_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError);
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
    public async Task DeleteAsync_RemovesOwnEntry_NotForeign()
    {
        var mine = await _svc.SaveAsync(1, new RememberLineInputDto { Fen = Fen, CourseName = "c" });
        await _svc.SaveAsync(2, new RememberLineInputDto { Fen = Fen, CourseName = "c" });

        Assert.False(await _svc.DeleteAsync(1, 99999));            // unbekannt
        Assert.False(await _svc.DeleteAsync(2, mine.Id));          // fremder Eintrag → nicht löschbar
        Assert.True(await _svc.DeleteAsync(1, mine.Id));           // eigener → gelöscht
        Assert.Empty(await _svc.ListAsync(1));
    }

    private class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Reply { get; set; }
            = (_, _) => new HttpResponseMessage(HttpStatusCode.OK);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(Reply(request, ct));
    }
}
