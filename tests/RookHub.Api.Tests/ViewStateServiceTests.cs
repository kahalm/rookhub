using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Anzeige-Zustand einer Seite je NUTZER — heute die Filterleiste des Turnierkalenders. Sie
/// lag nur im localStorage und war damit an ein Geraet gebunden: der Umkreis, den man am Rechner
/// eingestellt hat, war am Handy weg.
/// </summary>
public class ViewStateServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public ViewStateServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private const string Key = "turnier.directory";

    private async Task<int> CreateUserAsync(string username = "u")
    {
        var user = new AppUser { Username = username, Email = $"{username}@t.local", PasswordHash = "x" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private ViewStateController CreateController(int userId)
    {
        var controller = new ViewStateController(new ViewStateService(_db));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test")),
            },
        };
        return controller;
    }

    [Fact]
    public async Task Save_ThenGet_ReturnsTheState()
    {
        var userId = await CreateUserAsync();
        var svc = new ViewStateService(_db);

        Assert.True(await svc.SaveAsync(userId, Key, """{"tab":"map","radiusKm":50}"""));

        Assert.Equal("""{"tab":"map","radiusKm":50}""", await svc.GetAsync(userId, Key));
    }

    /// <summary>Ein Zustand je Nutzer und Ansicht — der zweite Schreibvorgang ERSETZT.</summary>
    [Fact]
    public async Task Save_Twice_Replaces()
    {
        var userId = await CreateUserAsync();
        var svc = new ViewStateService(_db);

        await svc.SaveAsync(userId, Key, """{"tab":"list"}""");
        await svc.SaveAsync(userId, Key, """{"tab":"calendar"}""");

        Assert.Equal("""{"tab":"calendar"}""", await svc.GetAsync(userId, Key));
        Assert.Single(_db.UserViewStates);
    }

    [Fact]
    public async Task Get_ForAnotherUser_IsNull()
    {
        var mine = await CreateUserAsync("ich");
        var other = await CreateUserAsync("jemand");
        var svc = new ViewStateService(_db);
        await svc.SaveAsync(mine, Key, """{"tab":"map"}""");

        Assert.Null(await svc.GetAsync(other, Key));
    }

    /// <summary>
    /// Kein stilles Zurechtbiegen: was hier ankommt, ist entweder der Zustand der Oberflaeche
    /// oder ein Fehler in ihr. Eine Zahl ist gueltiges JSON, aber kein Anzeige-Zustand.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("kein json")]
    [InlineData("42")]
    [InlineData("\"nur ein string\"")]
    [InlineData("[1,2,3]")]
    public async Task Save_WithoutJsonObject_IsRejected(string json)
    {
        var userId = await CreateUserAsync();

        Assert.False(await new ViewStateService(_db).SaveAsync(userId, Key, json));
        Assert.Empty(_db.UserViewStates);
    }

    [Fact]
    public async Task Save_TooLarge_IsRejected()
    {
        var userId = await CreateUserAsync();
        var big = "{\"x\":\"" + new string('a', ViewStateService.MaxJsonLength) + "\"}";

        Assert.False(await new ViewStateService(_db).SaveAsync(userId, Key, big));
        Assert.Empty(_db.UserViewStates);
    }

    /// <summary>
    /// Die Kennung ist eine ERLAUBTE, kein freier Name — ohne diese Liste waere der Endpunkt ein
    /// Schluessel-Wert-Speicher je Nutzer fuer beliebige Inhalte.
    /// </summary>
    [Theory]
    [InlineData("turnier.directory", true)]
    [InlineData("beliebig", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAllowedKey_OnlyKnownViews(string? key, bool allowed)
    {
        Assert.Equal(allowed, ViewStateService.IsAllowedKey(key));
    }

    [Fact]
    public async Task Controller_UnknownKey_IsNotFound()
    {
        var userId = await CreateUserAsync();

        Assert.IsType<NotFoundResult>(await CreateController(userId).Get("beliebig", default));
    }

    /// <summary>204, nicht 404: „nichts gespeichert" ist der Normalfall beim ersten Aufruf.</summary>
    [Fact]
    public async Task Controller_WithoutState_IsNoContent()
    {
        var userId = await CreateUserAsync();

        Assert.IsType<NoContentResult>(await CreateController(userId).Get(Key, default));
    }

    [Fact]
    public async Task Controller_Put_StoresTheBodyItself()
    {
        var userId = await CreateUserAsync();
        var body = System.Text.Json.JsonDocument.Parse("""{"tab":"map"}""").RootElement;

        Assert.IsType<NoContentResult>(await CreateController(userId).Put(Key, body, default));

        Assert.Equal("""{"tab":"map"}""", await new ViewStateService(_db).GetAsync(userId, Key));
    }

    [Fact]
    public async Task Controller_Delete_IsIdempotent()
    {
        var userId = await CreateUserAsync();
        var controller = CreateController(userId);

        Assert.IsType<NoContentResult>(await controller.Delete(Key, default));   // gab es nie
        await new ViewStateService(_db).SaveAsync(userId, Key, """{"tab":"list"}""");
        Assert.IsType<NoContentResult>(await controller.Delete(Key, default));

        Assert.Empty(_db.UserViewStates);
    }
}
