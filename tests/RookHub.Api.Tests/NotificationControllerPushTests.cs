using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>NotificationController Web-Push-Hälfte (Controller-Verdrahtung + Gates): unbekannter Bereich →
/// 400, „admin"-Bereich nur für Admins (IsAdmin durchgereicht), unvollständige Subscription → 400,
/// config liefert Ok. Die PushNotificationService-Logik selbst ist separat getestet.</summary>
public class NotificationControllerPushTests : IDisposable
{
    private readonly AppDbContext _db;
    public NotificationControllerPushTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(opts);
    }
    public void Dispose() => _db.Dispose();

    private sealed class NoopSender : IWebPushSender
    {
        public Task SendAsync(UserPushSubscription sub, string payloadJson, WebPushOptions opts, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private NotificationController Controller(int userId, bool admin)
    {
        var push = new PushNotificationService(_db, new NoopSender(),
            Options.Create(new WebPushOptions()), NullLogger<PushNotificationService>.Instance);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return new NotificationController(new NotificationService(_db), push)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    [Fact]
    public async Task PushConfig_ReturnsOk_WhenUnconfigured()
    {
        var res = await Controller(1, false).PushConfig();
        var ok = Assert.IsType<OkObjectResult>(res);
        Assert.IsType<PushConfigDto>(ok.Value);
    }

    [Fact]
    public async Task PushPreferences_RejectsUnknownCategory()
    {
        var res = await Controller(1, false).PushPreferences(new PushPreferencesInputDto { Categories = new() { "bogus" } });
        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task PushPreferences_DropsAdminCategoryForNonAdmins()
    {
        var res = await Controller(1, admin: false).PushPreferences(new PushPreferencesInputDto { Categories = new() { "admin", "courses" } });
        Assert.IsType<OkObjectResult>(res);
        var setting = await _db.NotificationPushSettings.FirstAsync(s => s.UserId == 1);
        Assert.Equal("courses", setting.EnabledCategories);   // admin herausgefiltert
    }

    [Fact]
    public async Task PushPreferences_KeepsAdminCategoryForAdmins()
    {
        var res = await Controller(2, admin: true).PushPreferences(new PushPreferencesInputDto { Categories = new() { "admin" } });
        Assert.IsType<OkObjectResult>(res);
        var setting = await _db.NotificationPushSettings.FirstAsync(s => s.UserId == 2);
        Assert.Contains("admin", setting.EnabledCategories!);
    }

    [Fact]
    public async Task PushSubscribe_RejectsIncompleteSubscription()
    {
        var res = await Controller(1, false).PushSubscribe(new PushSubscribeInputDto { Endpoint = "", P256dh = "", Auth = "" });
        Assert.IsType<BadRequestObjectResult>(res);
    }
}
