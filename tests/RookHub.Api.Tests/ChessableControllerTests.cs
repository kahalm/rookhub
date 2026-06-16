using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        var sp = new ServiceCollection().BuildServiceProvider();
        _controller = new ChessableController(_db, _encryption, _proxy, new BackgroundTaskQueue(),
            sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<ChessableController>.Instance);
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
    public async Task Courses_MarksCachedCourses()
    {
        await SeedUserAsync(42);
        await _controller.SaveCredentials(new SaveChessableBearerRequest("real-bearer-1234567890"));

        _handler.Reply = (req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path == "/api/chessable/direct/courses")
                return JsonResponse(HttpStatusCode.OK, new[] { new ChessableCourseDto("100", "A"), new ChessableCourseDto("200", "B") });
            if (path == "/api/chessable/direct/courses/cached")
                return JsonResponse(HttpStatusCode.OK, new { bids = new[] { "200" } });   // nur B gecacht
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        var result = await _controller.Courses(refresh: true, CancellationToken.None);

        var body = Assert.IsType<ChessableCoursesDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.False(body.Courses.Single(c => c.Bid == "100").Cached);
        Assert.True(body.Courses.Single(c => c.Bid == "200").Cached);
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

    [Fact]
    public async Task StartImport_WhenCached_RunsImmediately_QueuedAheadZero()
    {
        await SeedUserAsync(42);
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = 42, EncryptedBearer = _encryption.Encrypt("b"),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        // piratechess meldet: Kurs ist gecacht → sofort verarbeiten, keine Queue-Position.
        _handler.Reply = (req, _) => req.RequestUri!.AbsolutePath.EndsWith("/cached")
            ? JsonResponse(HttpStatusCode.OK, new { cached = true })
            : new HttpResponseMessage(HttpStatusCode.OK);

        var result = await _controller.StartImport("bid-x", new StartChessableImportRequest("repertoire", "X"));

        var dto = Assert.IsType<ChessableImportDto>(Assert.IsType<AcceptedResult>(result).Value);
        Assert.Equal(0, dto.QueuedAhead);
    }

    [Fact]
    public async Task CancelImport_Running_SetsCancelled()
    {
        await SeedUserAsync(42);
        var imp = new ChessableImport { UserId = 42, Bid = "b", Target = "book", Status = "running", Phase = "queued", CreatedAt = DateTime.UtcNow };
        _db.ChessableImports.Add(imp);
        await _db.SaveChangesAsync();

        Assert.IsType<OkObjectResult>(await _controller.CancelImport(imp.Id));
        Assert.Equal("cancelled", (await _db.ChessableImports.FindAsync(imp.Id))!.Status);
    }

    [Fact]
    public async Task GetImports_ReportsGlobalQueuePosition()
    {
        await SeedUserAsync(42);
        _db.AppUsers.Add(new AppUser { Id = 7, Username = "u7", PasswordHash = "x" });
        _db.ChessableImports.Add(new ChessableImport { UserId = 7, Bid = "a", Target = "book", Status = "running", CreatedAt = DateTime.UtcNow });
        var mine = new ChessableImport { UserId = 42, Bid = "b", Target = "book", Status = "running", CreatedAt = DateTime.UtcNow };
        _db.ChessableImports.Add(mine);
        await _db.SaveChangesAsync();

        var ok = Assert.IsType<OkObjectResult>(await _controller.GetImports());
        var list = Assert.IsAssignableFrom<IEnumerable<ChessableImportDto>>(ok.Value).ToList();
        Assert.Equal(1, list.Single(d => d.Id == mine.Id).QueuedAhead); // 1 Kurs (anderer User) davor
    }

    [Fact]
    public async Task GetImports_AlwaysIncludesActiveImport_EvenWhenOutsideRecentWindow()
    {
        await SeedUserAsync(42);
        var baseTime = DateTime.UtcNow.AddHours(-1);
        // Der aktive (gerade verarbeitete) Job ist der ÄLTESTE der offenen Charge.
        var active = new ChessableImport
        {
            UserId = 42, Bid = "active", Target = "book", Status = "running", Phase = "fetching",
            CreatedAt = baseTime
        };
        _db.ChessableImports.Add(active);
        // 25 neuere, abgeschlossene Importe → würden das 20er-Verlaufsfenster komplett füllen.
        for (int n = 0; n < 25; n++)
            _db.ChessableImports.Add(new ChessableImport
            {
                UserId = 42, Bid = $"done-{n}", Target = "book", Status = "completed",
                CreatedAt = baseTime.AddMinutes(n + 1)
            });
        await _db.SaveChangesAsync();

        var ok = Assert.IsType<OkObjectResult>(await _controller.GetImports());
        var list = Assert.IsAssignableFrom<IEnumerable<ChessableImportDto>>(ok.Value).ToList();

        var activeDto = list.SingleOrDefault(d => d.Id == active.Id);
        Assert.NotNull(activeDto); // trotz 25 neuerer Importe sichtbar (vorher durch Take(20) abgeschnitten)
        Assert.Equal("running", activeDto!.Status);
        Assert.Equal(0, activeDto.QueuedAhead); // ältester laufender → Position 0 = "läuft gerade"
    }

    // ---- Admin-Sicht: alle Importe + aktive Queue ----

    [Fact]
    public async Task GetAllImportsAdmin_ReturnsImportsOfAllUsersWithUsername()
    {
        await SeedUserAsync(42);
        _db.AppUsers.Add(new AppUser { Id = 7, Username = "alice", PasswordHash = "x" });
        _db.ChessableImports.Add(new ChessableImport { UserId = 7, Bid = "a", CourseName = "Alice Kurs", Target = "book", Status = "completed", CreatedAt = DateTime.UtcNow.AddMinutes(-5) });
        _db.ChessableImports.Add(new ChessableImport { UserId = 42, Bid = "b", CourseName = "Mein Kurs", Target = "repertoire", Status = "running", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var ok = Assert.IsType<OkObjectResult>(await _controller.GetAllImportsAdmin());
        var list = Assert.IsAssignableFrom<IEnumerable<ChessableAdminImportDto>>(ok.Value).ToList();

        Assert.Equal(2, list.Count); // beide User
        Assert.Contains(list, d => d.Username == "alice" && d.CourseName == "Alice Kurs");
        Assert.Contains(list, d => d.Username == "u42" && d.CourseName == "Mein Kurs");
        Assert.Equal("running", list[0].Status); // neueste zuerst
    }

    [Fact]
    public async Task GetActiveImportsAdmin_ReturnsOnlyActiveWithQueuePosition()
    {
        await SeedUserAsync(42);
        _db.AppUsers.Add(new AppUser { Id = 7, Username = "alice", PasswordHash = "x" });
        _db.ChessableImports.Add(new ChessableImport { UserId = 7, Bid = "old", Target = "book", Status = "completed", CreatedAt = DateTime.UtcNow.AddHours(-2) });
        var first = new ChessableImport { UserId = 7, Bid = "r1", Target = "book", Status = "running", CreatedAt = DateTime.UtcNow.AddMinutes(-2) };
        var second = new ChessableImport { UserId = 42, Bid = "r2", Target = "book", Status = "running", CreatedAt = DateTime.UtcNow };
        _db.ChessableImports.AddRange(first, second);
        await _db.SaveChangesAsync();

        var ok = Assert.IsType<OkObjectResult>(await _controller.GetActiveImportsAdmin());
        var list = Assert.IsAssignableFrom<IEnumerable<ChessableAdminImportDto>>(ok.Value).ToList();

        Assert.Equal(2, list.Count); // nur die laufenden, nicht der abgeschlossene
        Assert.DoesNotContain(list, d => d.Bid == "old");
        Assert.Equal(0, list.Single(d => d.Id == first.Id).QueuedAhead);  // ältester läuft
        Assert.Equal(1, list.Single(d => d.Id == second.Id).QueuedAhead); // einer davor
    }

    // Regression: Die angezeigte Queue-Position MUSS die faire Round-Robin-Reihenfolge
    // (QueueRound) widerspiegeln — nicht die Einreih-/Id-Reihenfolge. Szenario: User A reiht 3 Kurse
    // ein (Runde 0,1,2), danach User B 1 Kurs (Runde 0). Fair wird B's Runde-0-Kurs an Position 1
    // gezogen (gleich nach A's erstem), VOR A's Folge-Kursen. Die alte Id-Zählung hätte B ans Ende
    // (Position 3) gesetzt.
    [Fact]
    public async Task GetActiveImportsAdmin_OrdersByFairRoundRobin_NotByInsertionId()
    {
        await SeedUserAsync(42);
        _db.AppUsers.Add(new AppUser { Id = 7, Username = "userB", PasswordHash = "x" });
        var t = DateTime.UtcNow.AddMinutes(-10);
        var a1 = new ChessableImport { UserId = 42, Bid = "a1", Target = "book", Status = "running", Phase = "queued", QueueRound = 0, CreatedAt = t.AddSeconds(1) };
        var a2 = new ChessableImport { UserId = 42, Bid = "a2", Target = "book", Status = "running", Phase = "queued", QueueRound = 1, CreatedAt = t.AddSeconds(2) };
        var a3 = new ChessableImport { UserId = 42, Bid = "a3", Target = "book", Status = "running", Phase = "queued", QueueRound = 2, CreatedAt = t.AddSeconds(3) };
        var b1 = new ChessableImport { UserId = 7, Bid = "b1", Target = "book", Status = "running", Phase = "queued", QueueRound = 0, CreatedAt = t.AddSeconds(4) };
        _db.ChessableImports.AddRange(a1, a2, a3, b1);
        await _db.SaveChangesAsync();

        var ok = Assert.IsType<OkObjectResult>(await _controller.GetActiveImportsAdmin());
        var list = Assert.IsAssignableFrom<IEnumerable<ChessableAdminImportDto>>(ok.Value).ToList();

        Assert.Equal(0, list.Single(d => d.Id == a1.Id).QueuedAhead); // A Runde 0
        Assert.Equal(1, list.Single(d => d.Id == b1.Id).QueuedAhead); // B Runde 0 — fair an 2. Stelle (alt: 3)
        Assert.Equal(2, list.Single(d => d.Id == a2.Id).QueuedAhead); // A Runde 1
        Assert.Equal(3, list.Single(d => d.Id == a3.Id).QueuedAhead); // A Runde 2
        // Die zurückgegebene Liste ist bereits in fairer Verarbeitungsreihenfolge sortiert.
        Assert.Equal(new[] { "a1", "b1", "a2", "a3" }, list.Select(d => d.Bid).ToArray());
    }

    // Regression: laufender/holender Import steht oben, wartende folgen in fairer Reihenfolge.
    [Fact]
    public async Task GetActiveImportsAdmin_FetchingFirstThenFairQueue()
    {
        await SeedUserAsync(42);
        _db.AppUsers.Add(new AppUser { Id = 7, Username = "userB", PasswordHash = "x" });
        var t = DateTime.UtcNow.AddMinutes(-10);
        var fetching = new ChessableImport { UserId = 7, Bid = "fetch", Target = "book", Status = "running", Phase = "fetching", QueueRound = 0, CreatedAt = t.AddSeconds(1) };
        var qA = new ChessableImport { UserId = 42, Bid = "qA", Target = "book", Status = "running", Phase = "queued", QueueRound = 0, CreatedAt = t.AddSeconds(2) };
        var qB = new ChessableImport { UserId = 7, Bid = "qB", Target = "book", Status = "running", Phase = "queued", QueueRound = 1, CreatedAt = t.AddSeconds(3) };
        _db.ChessableImports.AddRange(fetching, qA, qB);
        await _db.SaveChangesAsync();

        var ok = Assert.IsType<OkObjectResult>(await _controller.GetActiveImportsAdmin());
        var list = Assert.IsAssignableFrom<IEnumerable<ChessableAdminImportDto>>(ok.Value).ToList();

        Assert.Equal(new[] { "fetch", "qA", "qB" }, list.Select(d => d.Bid).ToArray()); // holender oben
        Assert.Equal(1, list.Single(d => d.Id == qA.Id).QueuedAhead); // 1 in Arbeit davor
        Assert.Equal(2, list.Single(d => d.Id == qB.Id).QueuedAhead);
    }

    // ---- Admin: Kurse im Namen eines Users holen ----

    [Fact]
    public async Task AdminStartImport_UnknownUser_Returns404()
    {
        await SeedUserAsync(42); // Aufrufer = Admin
        var result = await _controller.StartImportForUserAdmin(999, "bid-1", new AdminChessableImportRequest(null));
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task AdminStartImport_TargetUserHasNoBearer_Returns400()
    {
        await SeedUserAsync(42);
        await SeedUserAsync(7); // Ziel-User ohne Bearer
        var result = await _controller.StartImportForUserAdmin(7, "bid-1", new AdminChessableImportRequest(null));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AdminStartImport_Valid_CreatesImportOwnedByAdmin_FetchedWithTargetUserBearer()
    {
        await SeedUserAsync(42);          // Admin (Aufrufer)
        await SeedUserAsync(7);           // Ziel-User
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = 7, EncryptedBearer = _encryption.Encrypt("user7-bearer"),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.StartImportForUserAdmin(7, "bid-9", new AdminChessableImportRequest("Holzkurs"));

        var dto = Assert.IsType<ChessableImportDto>(Assert.IsType<AcceptedResult>(result).Value);
        Assert.Equal("repertoire", dto.Target);           // landet als Repertoire
        Assert.Equal("Holzkurs", dto.CourseName);
        var imp = await _db.ChessableImports.SingleAsync(i => i.Bid == "bid-9");
        Assert.Equal(42, imp.UserId);                     // Besitzer = Admin
        Assert.Equal(7, imp.BearerUserId);                // Bearer vom Ziel-User
        Assert.Equal("running", imp.Status);
    }

    [Fact]
    public async Task AdminCredentialedUsers_ListsOnlyUsersWithBearer()
    {
        await SeedUserAsync(42);
        await SeedUserAsync(7);
        await SeedUserAsync(8); // ohne Bearer → nicht gelistet
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = 7, EncryptedBearer = _encryption.Encrypt("b"),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var ok = Assert.IsType<OkObjectResult>(await _controller.GetCredentialedUsersAdmin());
        var list = Assert.IsAssignableFrom<IEnumerable<ChessableCredentialedUserDto>>(ok.Value).ToList();
        Assert.Single(list);
        Assert.Equal(7, list[0].UserId);
        Assert.Equal("u7", list[0].Username);
    }

    private class StubHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Reply { get; set; }
            = (_, _) => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Reply(request, cancellationToken));
    }
}
