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
    private int _userId;

    public ExternalEngineControllerTests()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
        _sp = services.BuildServiceProvider();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);
        var directory = new EngineSelectorDirectory(_sp.GetRequiredService<IServiceScopeFactory>());
        var registrations = new ExternalEngineRegistrationService(_db, directory, NullLogger<ExternalEngineRegistrationService>.Instance);
        _controller = new ExternalEngineController(registrations, new LocalBrokerOptions(),
            NullLogger<ExternalEngineController>.Instance);
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
            new LocalBrokerOptions { Enabled = false }, NullLogger<ExternalEngineController>.Instance)
        {
            ControllerContext = _controller.ControllerContext,
        };
        As("engine");
        off.ControllerContext = _controller.ControllerContext;
        Assert.IsType<NotFoundResult>(await off.List(CancellationToken.None));
        Assert.IsType<NotFoundResult>(await off.Create(Req(), CancellationToken.None));
    }
}
