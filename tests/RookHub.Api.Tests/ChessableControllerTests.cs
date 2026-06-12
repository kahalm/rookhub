using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class ChessableControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly StubHttpMessageHandler _handler;
    private readonly ChessableProxyService _proxy;
    private readonly ChessableController _controller;

    public ChessableControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!"
            })
            .Build();
        _encryption = new EncryptionService(config);

        _handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://piratechess-api:8080") };
        _proxy = new ChessableProxyService(httpClient);

        _controller = new ChessableController(_db, _encryption, _proxy, new BackgroundTaskQueue(), NullLogger<ChessableController>.Instance);
        SetUser(42);
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    private async Task SeedUserAsync(int id)
    {
        _db.AppUsers.Add(new AppUser { Id = id, Username = $"u{id}", PasswordHash = "x" });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetCredentials_None_ReturnsHasCredentialsFalse()
    {
        await SeedUserAsync(42);

        var result = await _controller.GetCredentials();

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ChessableCredentialResponse>(ok.Value);
        Assert.False(body.HasCredentials);
        Assert.Null(body.MaskedBearer);
    }

    [Fact]
    public async Task SaveCredentials_NewBearer_StoresEncryptedAndReturnsMasked()
    {
        await SeedUserAsync(42);

        var result = await _controller.SaveCredentials(new SaveChessableBearerRequest("super-secret-bearer-token-1234"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ChessableCredentialResponse>(ok.Value);
        Assert.True(body.HasCredentials);
        Assert.StartsWith("supe", body.MaskedBearer);
        Assert.EndsWith("1234", body.MaskedBearer);
        Assert.Contains("*", body.MaskedBearer);

        var cred = await _db.ChessableCredentials.SingleAsync(c => c.UserId == 42);
        Assert.NotEqual("super-secret-bearer-token-1234", cred.EncryptedBearer);
        Assert.Equal("super-secret-bearer-token-1234", _encryption.Decrypt(cred.EncryptedBearer));
    }

    [Fact]
    public async Task SaveCredentials_ExistingBearer_Overwrites()
    {
        await SeedUserAsync(42);
        await _controller.SaveCredentials(new SaveChessableBearerRequest("first-token-abcdefgh"));
        await _controller.SaveCredentials(new SaveChessableBearerRequest("second-token-zzzzzzzz"));

        var cred = await _db.ChessableCredentials.SingleAsync(c => c.UserId == 42);
        Assert.Equal("second-token-zzzzzzzz", _encryption.Decrypt(cred.EncryptedBearer));
    }

    [Fact]
    public async Task SaveCredentials_EmptyBearer_Returns400()
    {
        await SeedUserAsync(42);

        var result = await _controller.SaveCredentials(new SaveChessableBearerRequest("   "));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DeleteCredentials_Existing_Removes()
    {
        await SeedUserAsync(42);
        await _controller.SaveCredentials(new SaveChessableBearerRequest("token-to-delete-1234"));

        var result = await _controller.DeleteCredentials();

        Assert.IsType<NoContentResult>(result);
        Assert.False(await _db.ChessableCredentials.AnyAsync(c => c.UserId == 42));
    }

    [Fact]
    public async Task DeleteCredentials_NoneExist_StillNoContent()
    {
        await SeedUserAsync(42);

        var result = await _controller.DeleteCredentials();

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Test_NoBearerSaved_Returns400()
    {
        await SeedUserAsync(42);

        var result = await _controller.Test(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Test_WithBearer_ForwardsToProxyAndReturnsResult()
    {
        await SeedUserAsync(42);
        await _controller.SaveCredentials(new SaveChessableBearerRequest("real-bearer-1234567890"));

        _handler.Reply = (req, ct) =>
        {
            Assert.Equal("/api/chessable/direct/test", req.RequestUri!.AbsolutePath);
            return JsonResponse(HttpStatusCode.OK, new ChessableTestResultDto("uid-123", 7));
        };

        var result = await _controller.Test(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ChessableTestResultDto>(ok.Value);
        Assert.Equal("uid-123", body.Uid);
        Assert.Equal(7, body.CourseCount);
    }

    [Fact]
    public async Task Test_ProxyReturnsError_Maps400WithMessage()
    {
        await SeedUserAsync(42);
        await _controller.SaveCredentials(new SaveChessableBearerRequest("bad-bearer-1234567890"));

        _handler.Reply = (_, _) =>
            JsonResponse(HttpStatusCode.BadRequest, new { message = "Invalid bearer" });

        var result = await _controller.Test(CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Invalid bearer", JsonSerializer.Serialize(bad.Value));
    }

    [Fact]
    public async Task Courses_WithBearer_ReturnsList()
    {
        await SeedUserAsync(42);
        await _controller.SaveCredentials(new SaveChessableBearerRequest("real-bearer-1234567890"));

        _handler.Reply = (req, _) =>
        {
            Assert.Equal("/api/chessable/direct/courses", req.RequestUri!.AbsolutePath);
            return JsonResponse(HttpStatusCode.OK, new[]
            {
                new ChessableCourseDto("100", "Course A"),
                new ChessableCourseDto("200", "Course B")
            });
        };

        var result = await _controller.Courses(refresh: false, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ChessableCoursesDto>(ok.Value);
        Assert.Equal(2, body.Courses.Count);
        Assert.Equal("Course A", body.Courses[0].Name);
        Assert.NotNull(body.CachedAt); // wurde beim Abruf gecacht
    }

    [Fact]
    public async Task Courses_CacheHit_ReturnsCachedWithoutProxy()
    {
        await SeedUserAsync(42);
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = 42,
            EncryptedBearer = _encryption.Encrypt("b"),
            CachedCoursesJson = "[{\"bid\":\"1\",\"name\":\"Cached Course\"}]",
            CoursesCachedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        _handler.Reply = (_, _) => throw new Exception("Proxy darf bei Cache-Hit nicht aufgerufen werden");

        var result = await _controller.Courses(refresh: false, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ChessableCoursesDto>(ok.Value);
        Assert.Single(body.Courses);
        Assert.Equal("Cached Course", body.Courses[0].Name);
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T payload)
    {
        return new HttpResponseMessage(status)
        {
            Content = JsonContent.Create(payload)
        };
    }

    // ---- Kurs-Import (async Start) ----

    [Fact]
    public async Task StartImport_InvalidTarget_Returns400()
    {
        await SeedUserAsync(42);
        var result = await _controller.StartImport("123", new StartChessableImportRequest("nonsense", null));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task StartImport_NoBearer_Returns400()
    {
        await SeedUserAsync(42);
        var result = await _controller.StartImport("123", new StartChessableImportRequest("repertoire", null));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task StartImport_Valid_CreatesRunningImport()
    {
        await SeedUserAsync(42);
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = 42,
            EncryptedBearer = _encryption.Encrypt("bearer"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.StartImport("bid-1", new StartChessableImportRequest("book", "My Course"));

        var accepted = Assert.IsType<AcceptedResult>(result);
        var dto = Assert.IsType<ChessableImportDto>(accepted.Value);
        Assert.Equal("book", dto.Target);
        Assert.Equal("running", dto.Status);
        Assert.Equal("My Course", dto.CourseName);
        Assert.True(await _db.ChessableImports.AnyAsync(i => i.UserId == 42 && i.Bid == "bid-1" && i.Status == "running"));
    }

    [Fact]
    public async Task Courses_MarksImportedVariants()
    {
        await SeedUserAsync(42);
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = 42,
            EncryptedBearer = _encryption.Encrypt("b"),
            CachedCoursesJson = "[{\"bid\":\"1\",\"name\":\"A\"},{\"bid\":\"2\",\"name\":\"B\"}]",
            CoursesCachedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _db.ChessableImports.Add(new ChessableImport { UserId = 42, Bid = "1", Target = "repertoire", Status = "completed", CreatedAt = DateTime.UtcNow });
        _db.ChessableImports.Add(new ChessableImport { UserId = 42, Bid = "2", Target = "book", Status = "completed", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _controller.Courses(refresh: false, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ChessableCoursesDto>(ok.Value);
        var a = body.Courses.Single(c => c.Bid == "1");
        var b = body.Courses.Single(c => c.Bid == "2");
        Assert.True(a.ImportedRepertoire);
        Assert.False(a.ImportedBook);
        Assert.True(b.ImportedBook);
        Assert.False(b.ImportedRepertoire);
    }

    [Fact]
    public async Task Disclaimer_DefaultFalse_ThenAcceptPersists()
    {
        await SeedUserAsync(42);

        var before = Assert.IsType<ChessableDisclaimerDto>(Assert.IsType<OkObjectResult>(await _controller.GetDisclaimer()).Value);
        Assert.False(before.Accepted);

        await _controller.AcceptDisclaimer();

        var after = Assert.IsType<ChessableDisclaimerDto>(Assert.IsType<OkObjectResult>(await _controller.GetDisclaimer()).Value);
        Assert.True(after.Accepted);
        Assert.True(await _db.UserProfiles.AnyAsync(p => p.UserId == 42 && p.ChessableDisclaimerAcceptedAt != null));
    }

    private class StubHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Reply { get; set; }
            = (_, _) => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Reply(request, cancellationToken));
    }
}
