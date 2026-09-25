using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Registrierungs-Endpunkte des eigenen Brokers: der PROVIDER (API-Token mit Scope <c>engine</c>)
/// registriert und liest mit clientSecret; der Browser (JWT) darf nur lesen (ohne Secret) und löschen;
/// ein Token eines anderen Scopes gar nichts.
/// </summary>
public class ExternalEngineControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ServiceProvider _sp;
    private readonly ExternalEngineController _controller;
    private readonly EngineHub _hub;
    private readonly EngineSelectorDirectory _directory;
    private readonly LocalBrokerOptions _options = new() { AcquireWait = TimeSpan.FromMilliseconds(150) };
    private int _userId;

    public ExternalEngineControllerTests()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
        _sp = services.BuildServiceProvider();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);
        _directory = new EngineSelectorDirectory(_sp.GetRequiredService<IServiceScopeFactory>());
        var registrations = new ExternalEngineRegistrationService(_db, _directory, NullLogger<ExternalEngineRegistrationService>.Instance);
        _hub = new EngineHub(_options, () => DateTime.UtcNow, startSweeper: false);
        _controller = new ExternalEngineController(registrations, _options,
            NullLogger<ExternalEngineController>.Instance, _hub, _directory);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sp.Dispose();
    }

    private async Task UserAsync()
    {
        var u = new AppUser { Username = "kahalm", PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        _userId = u.Id;
    }

    /// <summary><paramref name="scope"/> null = Browser-Login (JWT trägt keinen scope-Claim).</summary>
    private void As(string? scope)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, _userId.ToString()), new(ClaimTypes.Name, "kahalm") };
        if (scope is not null) claims.Add(new Claim("scope", scope));
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
        };
    }

    private static ExternalEngineRegistrationRequest Req(string name = "Server Live") => new()
    {
        Name = name, MaxThreads = 8, MaxHash = 1024, Variants = ["chess"], ProviderSecret = "provider-secret-0123456789",
    };

    private static int StatusOf(IActionResult r) => r switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        _ => throw new InvalidOperationException(r.GetType().Name),
    };

    [Fact]
    public async Task Provider_RegistersAndListsWithClientSecret()
    {
        await UserAsync();
        As("engine");
        var created = Assert.IsType<OkObjectResult>(await _controller.Create(Req(), CancellationToken.None));
        var dto = Assert.IsType<ExternalEngineRegistrationDto>(created.Value);
        Assert.StartsWith("rhe_", dto.Id);
        Assert.NotNull(dto.ClientSecret);
        Assert.Equal("kahalm", dto.UserId);

        var list = Assert.IsType<OkObjectResult>(await _controller.List(CancellationToken.None));
        var engine = Assert.Single(Assert.IsType<List<ExternalEngineRegistrationDto>>(list.Value));
        Assert.Equal("Server Live", engine.Name);           // der Provider sucht per Name
        Assert.Equal(dto.ClientSecret, engine.ClientSecret);

        var updated = await _controller.Update(dto.Id, Req(), CancellationToken.None);
        Assert.Equal(200, StatusOf(updated));
    }

    [Fact]
    public async Task Browser_MayReadWithoutSecret_AndDelete_ButNotRegister()
    {
        await UserAsync();
        As("engine");
        var id = ((ExternalEngineRegistrationDto)((OkObjectResult)await _controller.Create(Req(), CancellationToken.None)).Value!).Id;

        As(null);
        Assert.Equal(403, StatusOf(await _controller.Create(Req("zweite"), CancellationToken.None)));
        Assert.Equal(403, StatusOf(await _controller.Update(id, Req(), CancellationToken.None)));

        var list = Assert.IsType<OkObjectResult>(await _controller.List(CancellationToken.None));
        Assert.Null(Assert.Single(Assert.IsType<List<ExternalEngineRegistrationDto>>(list.Value)).ClientSecret);

        Assert.IsType<NoContentResult>(await _controller.Delete(id, CancellationToken.None));
        Assert.Empty(_db.ExternalEngineRegistrations);
        Assert.Equal(404, StatusOf(await _controller.Delete(id, CancellationToken.None)));
    }

    [Fact]
    public async Task ExtensionToken_IsForbiddenEverywhere()
    {
        await UserAsync();
        As("extension");
        Assert.Equal(403, StatusOf(await _controller.List(CancellationToken.None)));
        Assert.Equal(403, StatusOf(await _controller.Create(Req(), CancellationToken.None)));
        Assert.Equal(403, StatusOf(await _controller.Delete("rhe_x", CancellationToken.None)));
    }

    [Fact]
    public async Task InvalidBody_Is400_WithAMessage()
    {
        await UserAsync();
        As("engine");
        var req = Req();
        req.MaxThreads = 0;
        var r = Assert.IsType<ObjectResult>(await _controller.Create(req, CancellationToken.None));
        Assert.Equal(400, r.StatusCode);
    }

    [Fact]
    public async Task Disabled_AllEndpointsAre404()
    {
        await UserAsync();
        var directory = new EngineSelectorDirectory(_sp.GetRequiredService<IServiceScopeFactory>());
        var off = new ExternalEngineController(
            new ExternalEngineRegistrationService(_db, directory, NullLogger<ExternalEngineRegistrationService>.Instance),
            new LocalBrokerOptions { Enabled = false }, NullLogger<ExternalEngineController>.Instance, _hub, directory)
        {
            ControllerContext = _controller.ControllerContext,
        };
        As("engine");
        off.ControllerContext = _controller.ControllerContext;
        Assert.IsType<NotFoundResult>(await off.List(CancellationToken.None));
        Assert.IsType<NotFoundResult>(await off.Create(Req(), CancellationToken.None));
        Assert.IsType<NotFoundResult>(await off.Acquire(new EngineAcquireRequest { ProviderSecret = "x" }, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await off.Submit("abc"));
    }

    // ---------------------------------------------------------------- Arbeit holen / hochladen

    private void Anonymous() => _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

    [Fact]
    public async Task Acquire_UnknownSecret_Waits_ThenIs204_Not401()
    {
        Anonymous();
        var started = DateTime.UtcNow;
        var r = await _controller.Acquire(new EngineAcquireRequest { ProviderSecret = "never-registered-0123" }, CancellationToken.None);
        Assert.IsType<NoContentResult>(r);
        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(120));
    }

    [Fact]
    public async Task Acquire_KnownSecret_WithoutWork_Is204_AndMarksTheEngineSeen()
    {
        await UserAsync();
        As("engine");
        await _controller.Create(Req(), CancellationToken.None);
        Anonymous();
        var r = await _controller.Acquire(new EngineAcquireRequest { ProviderSecret = "provider-secret-0123456789" }, CancellationToken.None);
        Assert.IsType<NoContentResult>(r);
        Assert.NotNull(_directory.LastSeen(ProviderSecrets.Selector("provider-secret-0123456789")));
    }

    [Fact]
    public async Task Acquire_WithWork_ReturnsIdWorkEngine()
    {
        await UserAsync();
        As("engine");
        await _controller.Create(Req(), CancellationToken.None);
        var selector = ProviderSecrets.Selector("provider-secret-0123456789");
        var job = new PendingJob(selector, "rhe_x", new System.Text.Json.Nodes.JsonObject { ["id"] = "rhe_x" },
            new EngineWork("s", 1, 16, 1, "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", [], Depth: 5),
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        _hub.Submit(job);

        Anonymous();
        var ok = Assert.IsType<OkObjectResult>(
            await _controller.Acquire(new EngineAcquireRequest { ProviderSecret = "provider-secret-0123456789" }, CancellationToken.None));
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.StartsWith("{\"id\":\"" + job.Id + "\",\"work\":{\"sessionId\":\"s\"", json);
        Assert.Contains("\"engine\":{\"id\":\"rhe_x\"}", json);
    }

    [Fact]
    public async Task Acquire_WithoutSecret_Is400()
    {
        Anonymous();
        Assert.IsType<BadRequestObjectResult>(await _controller.Acquire(new EngineAcquireRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task Submit_UnknownId_Is404()
    {
        Anonymous();
        _controller.ControllerContext.HttpContext.Request.Body = new MemoryStream();
        Assert.IsType<NotFoundObjectResult>(await _controller.Submit("doesnotexist0000"));
    }

    [Fact]
    public async Task Submit_RequesterAlreadyGone_Is200Immediately()
    {
        var job = new PendingJob("sel", "rhe_x", new System.Text.Json.Nodes.JsonObject(),
            new EngineWork("s", 1, 16, 1, "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", [], Depth: 5),
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        _hub.Submit(job);
        var got = await _hub.AcquireAsync("sel", TimeSpan.FromSeconds(1), CancellationToken.None);
        job.CancelRequester();

        Anonymous();
        _controller.ControllerContext.HttpContext.Request.Body = new MemoryStream();
        Assert.IsType<OkResult>(await _controller.Submit(got!.Id!));
        Assert.Null(_hub.TakeOngoing(got.Id!));
    }

    [Fact]
    public async Task Submit_StreamsTheUpload_ToTheRequester()
    {
        var job = new PendingJob("sel", "rhe_x", new System.Text.Json.Nodes.JsonObject(),
            new EngineWork("s", 1, 16, 1, "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", [], Depth: 5),
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        _hub.Submit(job);
        var got = await _hub.AcquireAsync("sel", TimeSpan.FromSeconds(1), CancellationToken.None);

        Anonymous();
        _controller.ControllerContext.HttpContext.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            "info depth 1 multipv 1 score cp 3 nodes 1 time 1 pv e2e4\n{\"keepalive\":true}\nbestmove e2e4\n"));
        Assert.IsType<OkResult>(await _controller.Submit(got!.Id!));

        var lines = new List<string>();
        await foreach (var l in job.Lines.Reader.ReadAllAsync()) lines.Add(l.TrimEnd('\n'));
        Assert.Equal(3, lines.Count);
        Assert.Equal("{\"keepalive\":true}", lines[1]);
        Assert.EndsWith("\"bestmove\":\"e2e4\"}", lines[2]);
        Assert.Equal(1, _hub.Stats.For("rhe_x")!.Completed);
    }

    /// <summary>Die drei Grenzen, an denen ein Upload sonst still stirbt, stehen an den Aktionen — und nur
    /// dort: die Registrierung bleibt gedrosselt.</summary>
    [Fact]
    public void ProviderEndpoints_AreExemptFromRateLimitAndSizeLimit_RegistrationIsNot()
    {
        static bool Has<T>(string action) where T : Attribute =>
            typeof(ExternalEngineController).GetMethod(action)!.GetCustomAttributes(typeof(T), true).Length > 0;

        Assert.True(Has<Microsoft.AspNetCore.RateLimiting.DisableRateLimitingAttribute>(nameof(ExternalEngineController.Acquire)));
        Assert.True(Has<Microsoft.AspNetCore.RateLimiting.DisableRateLimitingAttribute>(nameof(ExternalEngineController.Submit)));
        Assert.True(Has<DisableRequestSizeLimitAttribute>(nameof(ExternalEngineController.Submit)));
        Assert.True(Has<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>(nameof(ExternalEngineController.Acquire)));
        Assert.True(Has<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>(nameof(ExternalEngineController.Submit)));
        foreach (var registration in new[] { nameof(ExternalEngineController.List), nameof(ExternalEngineController.Create),
                     nameof(ExternalEngineController.Update), nameof(ExternalEngineController.Delete) })
        {
            Assert.False(Has<Microsoft.AspNetCore.RateLimiting.DisableRateLimitingAttribute>(registration));
            Assert.False(Has<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>(registration));
        }
    }
}
