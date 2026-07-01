using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Nächtlicher Chessable-Kurslisten-Refresh: aktualisiert alle Bearer, sperrt tote Tokens
/// (Circuit-Breaker), benachrichtigt Admins bei neuen Kursen; erster Cache-Aufbau ist stumm.
/// </summary>
public class ChessableCourseRefreshServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly StubHandler _handler;
    private readonly ChessableCourseRefreshService _svc;

    public ChessableCourseRefreshServiceTests()
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
        var breaker = new ChessableBearerBreaker(_db, new BackgroundTaskQueue(), NullLogger<ChessableBearerBreaker>.Instance);
        _svc = new ChessableCourseRefreshService(_db, _encryption, proxy, breaker,
            new NotificationService(_db), NullLogger<ChessableCourseRefreshService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedAdminAsync(int id = 1)
    {
        _db.AppUsers.Add(new AppUser { Id = id, Username = "admin", PasswordHash = "x", IsAdmin = true });
        await _db.SaveChangesAsync();
    }

    private async Task<ChessableCredential> SeedCredAsync(int userId, string? cachedJson = null, DateTime? blockedAt = null)
    {
        _db.AppUsers.Add(new AppUser { Id = userId, Username = $"u{userId}", PasswordHash = "x" });
        var cred = new ChessableCredential
        {
            UserId = userId,
            EncryptedBearer = _encryption.Encrypt("bearer"),
            CachedCoursesJson = cachedJson,
            BlockedAt = blockedAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ChessableCredentials.Add(cred);
        await _db.SaveChangesAsync();
        return cred;
    }

    private void ReplyWithCourses(params ChessableCourseDto[] courses)
        => _handler.Reply = (_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(courses) };

    private void ReplyWithError(string message)
        => _handler.Reply = (_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { message }) };

    [Fact]
    public async Task NewCourse_NotifiesAdmins_AndUpdatesCache()
    {
        await SeedAdminAsync();
        await SeedCredAsync(42, cachedJson: "[{\"bid\":\"100\",\"name\":\"Course A\"}]");
        ReplyWithCourses(new ChessableCourseDto("100", "Course A"), new ChessableCourseDto("200", "Course B"));

        var summary = await _svc.RefreshAllAsync();

        Assert.Equal(1, summary.Refreshed);
        Assert.Equal(1, summary.NewCourses);
        var notif = await _db.Notifications.SingleAsync(n => n.UserId == 1);
        Assert.Equal(NotificationType.ChessableNewCourse, notif.Type);
        Assert.Contains("Course B", notif.DataJson!);
        var cred = await _db.ChessableCredentials.SingleAsync(c => c.UserId == 42);
        Assert.Contains("200", cred.CachedCoursesJson!);   // Cache aktualisiert
    }

    [Fact]
    public async Task FirstPopulation_NoPriorCache_DoesNotNotify()
    {
        await SeedAdminAsync();
        await SeedCredAsync(42, cachedJson: null);
        ReplyWithCourses(new ChessableCourseDto("100", "Course A"), new ChessableCourseDto("200", "Course B"));

        var summary = await _svc.RefreshAllAsync();

        Assert.Equal(1, summary.Refreshed);
        Assert.Equal(0, summary.NewCourses);
        Assert.Equal(0, await _db.Notifications.CountAsync());
        var cred = await _db.ChessableCredentials.SingleAsync(c => c.UserId == 42);
        Assert.NotNull(cred.CachedCoursesJson);            // trotzdem befüllt
    }

    [Fact]
    public async Task FatalError_TripsBreaker()
    {
        await SeedAdminAsync();
        await SeedCredAsync(42);
        ReplyWithError("User is banned or deleted");

        var summary = await _svc.RefreshAllAsync();

        Assert.Equal(1, summary.Blocked);
        Assert.Equal(0, summary.Refreshed);
        var cred = await _db.ChessableCredentials.SingleAsync(c => c.UserId == 42);
        Assert.NotNull(cred.BlockedAt);                    // Token gesperrt
    }

    [Fact]
    public async Task TransientError_DoesNotTripBreaker()
    {
        await SeedCredAsync(42);
        ReplyWithError("VPN-Ausgangs-IP gesperrt, IP rotieren");

        var summary = await _svc.RefreshAllAsync();

        Assert.Equal(1, summary.TransientErrors);
        Assert.Equal(0, summary.Blocked);
        var cred = await _db.ChessableCredentials.SingleAsync(c => c.UserId == 42);
        Assert.Null(cred.BlockedAt);
    }

    [Fact]
    public async Task AlreadyBlocked_IsSkipped_WithoutProxyCall()
    {
        await SeedCredAsync(42, blockedAt: DateTime.UtcNow);
        var called = false;
        _handler.Reply = (_, _) => { called = true; return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<ChessableCourseDto>()) }; };

        var summary = await _svc.RefreshAllAsync();

        Assert.Equal(1, summary.SkippedBlocked);
        Assert.False(called);                              // toter Token wird nicht angeklopft
    }

    [Fact]
    public void Scheduler_TimeUntilNextRun_TargetsNext0400Utc()
    {
        // 02:00 → heute 04:00 (2 h).
        Assert.Equal(TimeSpan.FromHours(2),
            ChessableCourseRefreshScheduler.TimeUntilNextRun(new DateTime(2026, 7, 1, 2, 0, 0, DateTimeKind.Utc)));
        // 05:00 → morgen 04:00 (23 h).
        Assert.Equal(TimeSpan.FromHours(23),
            ChessableCourseRefreshScheduler.TimeUntilNextRun(new DateTime(2026, 7, 1, 5, 0, 0, DateTimeKind.Utc)));
    }

    private class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Reply { get; set; }
            = (_, _) => new HttpResponseMessage(HttpStatusCode.OK);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(Reply(request, ct));
    }
}
