using System.Net;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using WebPush;

namespace RookHub.Api.Tests;

/// <summary>Web-Push: Bereichs-Präferenzen (Admin-Gate/Validierung), Subscriptions und der gated Versand.</summary>
public class PushNotificationServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FakeSender _sender = new();

    public PushNotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private PushNotificationService Svc(bool configured = true)
    {
        var opts = Options.Create(new WebPushOptions
        {
            Subject = configured ? "mailto:test@example.com" : null,
            PublicKey = configured ? "pub" : null,
            PrivateKey = configured ? "priv" : null,
        });
        return new PushNotificationService(_db, _sender, opts, NullLogger<PushNotificationService>.Instance);
    }

    private sealed class FakeSender : IWebPushSender
    {
        public readonly List<string> SentEndpoints = new();
        public HashSet<string> GoneEndpoints = new();
        /// <summary>Wird nach dem ersten erfolgreichen Send gecancelt (simuliert Shutdown mitten im Fan-out).</summary>
        public CancellationTokenSource? CancelAfterFirstSend;
        public Task SendAsync(UserPushSubscription sub, string payloadJson, WebPushOptions opts, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (GoneEndpoints.Contains(sub.Endpoint))
                throw new WebPushException("gone", new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth),
                    new HttpResponseMessage(HttpStatusCode.Gone));
            SentEndpoints.Add(sub.Endpoint);
            CancelAfterFirstSend?.Cancel();
            return Task.CompletedTask;
        }
    }

    private async Task AddSubAsync(int userId, string endpoint)
    {
        _db.UserPushSubscriptions.Add(new UserPushSubscription
        { UserId = userId, Endpoint = endpoint, P256dh = "p", Auth = "a", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task SetCategories_ValidatesAndStores_EmptyMeansNull()
    {
        var svc = Svc();
        var eff = await svc.SetEnabledCategoriesAsync(1, new[] { "courses", "puzzles" }, isAdmin: false);
        Assert.Equal(new[] { "courses", "puzzles" }, eff);
        Assert.Equal("courses,puzzles", (await _db.NotificationPushSettings.FindAsync(1))!.EnabledCategories);

        var empty = await svc.SetEnabledCategoriesAsync(1, Array.Empty<string>(), isAdmin: false);
        Assert.Empty(empty);
        Assert.Null((await _db.NotificationPushSettings.FindAsync(1))!.EnabledCategories);
    }

    [Fact]
    public async Task SetCategories_InvalidKey_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Svc().SetEnabledCategoriesAsync(1, new[] { "bogus" }, isAdmin: false));
    }

    [Fact]
    public async Task SetCategories_AdminArea_OnlyForAdmins()
    {
        var svc = Svc();
        var asUser = await svc.SetEnabledCategoriesAsync(1, new[] { "admin", "courses" }, isAdmin: false);
        Assert.Equal(new[] { "courses" }, asUser);   // „admin" verworfen
        var asAdmin = await svc.SetEnabledCategoriesAsync(2, new[] { "admin", "courses" }, isAdmin: true);
        Assert.Equal(new[] { "admin", "courses" }, asAdmin);
    }

    [Fact]
    public async Task Subscribe_Upsert_Unsubscribe_Idempotent()
    {
        var svc = Svc();
        await svc.SubscribeAsync(1, "https://push/ep1", "p1", "a1");
        await svc.SubscribeAsync(1, "https://push/ep1", "p2", "a2");   // Upsert (kein Duplikat)
        Assert.Equal(1, await _db.UserPushSubscriptions.CountAsync());
        Assert.Equal("p2", (await _db.UserPushSubscriptions.FirstAsync()).P256dh);

        await svc.UnsubscribeAsync(1, "https://push/ep1");
        await svc.UnsubscribeAsync(1, "https://push/ep1");            // idempotent
        Assert.Equal(0, await _db.UserPushSubscriptions.CountAsync());
    }

    [Fact]
    public async Task Send_OnlyWhenConfigured_CategoryEnabled_AndSubscribed()
    {
        var svc = Svc();
        await AddSubAsync(1, "https://push/ep1");
        await svc.SetEnabledCategoriesAsync(1, new[] { "courses" }, isAdmin: false);

        // Bereich aktiviert (course_shared → courses) → wird gesendet.
        await svc.SendToUserAsync(1, "course_shared", null, "/courses");
        Assert.Equal(new[] { "https://push/ep1" }, _sender.SentEndpoints);

        // Anderer Bereich (friend_request_received → friends) NICHT aktiviert → kein Versand.
        _sender.SentEndpoints.Clear();
        await svc.SendToUserAsync(1, "friend_request_received", null, null);
        Assert.Empty(_sender.SentEndpoints);
    }

    [Fact]
    public async Task Send_StopsRemainingSubscriptions_OnCancellation()
    {
        // Regression: SendToUserAsync reichte das CancellationToken nicht an den Sender durch —
        // beim Shutdown (bzw. Timeout) hing der sequenzielle Worker am laufenden HTTP-Send fest
        // und die restlichen Subscriptions wurden trotzdem noch angestoßen.
        var svc = Svc();
        await AddSubAsync(1, "https://push/ep1");
        await AddSubAsync(1, "https://push/ep2");
        await svc.SetEnabledCategoriesAsync(1, new[] { "courses" }, isAdmin: false);

        using var cts = new CancellationTokenSource();
        _sender.CancelAfterFirstSend = cts;   // Shutdown mitten im Fan-out

        await svc.SendToUserAsync(1, "course_shared", null, "/courses", cts.Token);

        Assert.Single(_sender.SentEndpoints); // zweiter Send wird nicht mehr versucht, kein Fehler
    }

    [Fact]
    public async Task Send_NoOp_WhenNotConfigured()
    {
        var svc = Svc(configured: false);
        await AddSubAsync(1, "https://push/ep1");
        await svc.SetEnabledCategoriesAsync(1, new[] { "courses" }, isAdmin: false);
        await svc.SendToUserAsync(1, "course_shared", null, "/courses");
        Assert.Empty(_sender.SentEndpoints);
        Assert.False(svc.IsConfigured);
        Assert.Null(svc.PublicKey);
    }

    [Fact]
    public async Task Send_GoneSubscription_IsRemoved()
    {
        var svc = Svc();
        await AddSubAsync(1, "https://push/gone");
        _sender.GoneEndpoints.Add("https://push/gone");
        await svc.SetEnabledCategoriesAsync(1, new[] { "courses" }, isAdmin: false);

        await svc.SendToUserAsync(1, "course_shared", null, "/courses");
        Assert.Equal(0, await _db.UserPushSubscriptions.CountAsync());   // tote Subscription aufgeräumt
    }

    [Fact]
    public void CategoryOf_MatchesFrontendBuckets()
    {
        Assert.Equal("courses", PushNotificationService.CategoryOf("repertoire_shared"));
        Assert.Equal("friends", PushNotificationService.CategoryOf("friend_request_accepted"));
        Assert.Equal("puzzles", PushNotificationService.CategoryOf("challenge_received"));
        Assert.Equal("messages", PushNotificationService.CategoryOf("admin_message_received"));
        Assert.Equal("tournaments", PushNotificationService.CategoryOf("tournament_new_round"));
        Assert.Equal("admin", PushNotificationService.CategoryOf("new_user_registered"));
        Assert.Equal("other", PushNotificationService.CategoryOf("something_else"));
    }
}
