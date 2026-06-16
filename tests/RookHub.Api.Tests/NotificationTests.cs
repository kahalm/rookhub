using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Generische In-App-Benachrichtigungen: Service, Controller und die Domänen-Trigger.</summary>
public class NotificationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly NotificationService _service;

    public NotificationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new NotificationService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> UserAsync(int id, string name)
    {
        var u = new AppUser { Id = id, Username = name, PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    // ---- Service ----

    [Fact]
    public async Task GetHistoryAsync_PaginatesAndScopesToUser()
    {
        await UserAsync(1, "u1");
        await UserAsync(2, "u2");
        for (var i = 0; i < 5; i++) await _service.CreateAsync(1, NotificationType.FriendRequestReceived);
        await _service.CreateAsync(2, NotificationType.FriendRequestReceived); // anderer User

        var p1 = await _service.GetHistoryAsync(1, page: 1, pageSize: 2);
        var p2 = await _service.GetHistoryAsync(1, page: 2, pageSize: 2);
        var p3 = await _service.GetHistoryAsync(1, page: 3, pageSize: 2);

        Assert.Equal(5, p1.Total); // nur die eigenen, nicht User 2
        Assert.Equal(2, p1.Items.Count);
        Assert.Equal(2, p2.Items.Count);
        Assert.Single(p3.Items);
        var ids = p1.Items.Concat(p2.Items).Concat(p3.Items).Select(n => n.Id).ToList();
        Assert.Equal(5, ids.Distinct().Count()); // keine Überschneidung, alle abgedeckt
    }

    [Fact]
    public async Task GetHistoryAsync_NewestFirst()
    {
        await UserAsync(1, "u1");
        for (var i = 0; i < 3; i++) await _service.CreateAsync(1, NotificationType.FriendRequestReceived);
        // CreatedAt eindeutig staffeln (id-aufsteigend = zeit-aufsteigend).
        var rows = await _db.Notifications.OrderBy(n => n.Id).ToListAsync();
        var t0 = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < rows.Count; i++) rows[i].CreatedAt = t0.AddMinutes(i);
        await _db.SaveChangesAsync();

        var page = await _service.GetHistoryAsync(1, page: 1, pageSize: 10);
        Assert.Equal(rows[^1].Id, page.Items[0].Id);  // neuester zuerst
        Assert.Equal(rows[0].Id, page.Items[^1].Id);
    }

    [Fact]
    public async Task CreateAsync_StoresUnseenWithData()
    {
        await UserAsync(1, "u1");
        await _service.CreateAsync(1, NotificationType.FriendRequestReceived,
            new Dictionary<string, string> { ["username"] = "alice" }, "/friends");

        var n = await _db.Notifications.SingleAsync();
        Assert.Equal(1, n.UserId);
        Assert.Equal(NotificationType.FriendRequestReceived, n.Type);
        Assert.Null(n.SeenAt);
        Assert.Equal("/friends", n.Link);
        Assert.Contains("alice", n.DataJson);
    }

    [Fact]
    public async Task CountUnseen_AndMarkAllSeen()
    {
        await UserAsync(1, "u1");
        await _service.CreateAsync(1, NotificationType.RevengePerformed);
        await _service.CreateAsync(1, NotificationType.ChallengeReceived);

        Assert.Equal(2, await _service.CountUnseenAsync(1));

        await _service.MarkAllSeenAsync(1);

        Assert.Equal(0, await _service.CountUnseenAsync(1));
        Assert.All(await _db.Notifications.ToListAsync(), n => Assert.NotNull(n.SeenAt));
    }

    [Fact]
    public async Task MarkSeen_Single_OnlyThatOne_AndScopedToUser()
    {
        await UserAsync(1, "u1");
        await UserAsync(2, "u2");
        await _service.CreateAsync(1, NotificationType.AdminMessageReceived);
        await _service.CreateAsync(1, NotificationType.FriendRequestReceived);
        var first = await _db.Notifications.OrderBy(n => n.Id).FirstAsync(n => n.UserId == 1);
        await _service.CreateAsync(2, NotificationType.AdminMessageReceived);
        var foreign = await _db.Notifications.FirstAsync(n => n.UserId == 2);

        await _service.MarkSeenAsync(1, first.Id);
        Assert.Equal(1, await _service.CountUnseenAsync(1));   // nur eine der zwei eigenen weg

        await _service.MarkSeenAsync(1, foreign.Id);            // fremde Notification → no-op
        Assert.Equal(1, await _service.CountUnseenAsync(2));
    }

    [Fact]
    public async Task GetForUser_NewestFirst_ScopedToUser_ParsesData()
    {
        await UserAsync(1, "u1");
        await UserAsync(2, "u2");
        await _service.CreateAsync(1, NotificationType.FriendRequestReceived, new Dictionary<string, string> { ["username"] = "a" });
        await _service.CreateAsync(1, NotificationType.FriendRequestAccepted, new Dictionary<string, string> { ["username"] = "b" });
        await _service.CreateAsync(2, NotificationType.RevengePerformed); // anderer User

        var list = await _service.GetForUserAsync(1);

        Assert.Equal(2, list.Count); // nur User 1
        Assert.Equal(NotificationType.FriendRequestAccepted, list[0].Type); // neueste zuerst
        Assert.Equal("b", list[0].Data!["username"]);
        Assert.False(list[0].Seen);
    }

    // ---- Controller ----

    private NotificationController ControllerFor(int userId)
    {
        var ctrl = new NotificationController(_service);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return ctrl;
    }

    [Fact]
    public async Task Controller_Count_List_Seen_RoundTrip()
    {
        await UserAsync(5, "u5");
        await _service.CreateAsync(5, NotificationType.ChessableImportCompleted, new Dictionary<string, string> { ["courseName"] = "C" }, "/courses");
        var ctrl = ControllerFor(5);

        var count = Assert.IsType<NotificationCountDto>(Assert.IsType<OkObjectResult>(await ctrl.GetCount()).Value);
        Assert.Equal(1, count.Count);

        var list = Assert.IsAssignableFrom<IEnumerable<NotificationDto>>(Assert.IsType<OkObjectResult>(await ctrl.GetAll(20)).Value).ToList();
        Assert.Single(list);

        Assert.IsType<NoContentResult>(await ctrl.MarkSeen());
        var after = Assert.IsType<NotificationCountDto>(Assert.IsType<OkObjectResult>(await ctrl.GetCount()).Value);
        Assert.Equal(0, after.Count);
    }

    // ---- Domänen-Trigger ----

    [Fact]
    public async Task FriendRequest_Send_And_Accept_NotifyBothSides()
    {
        await UserAsync(1, "alice");
        await UserAsync(2, "bob");
        var friends = new FriendService(_db, _service);

        var fr = await friends.SendRequestAsync(1, 2); // alice → bob
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == 2 && n.Type == NotificationType.FriendRequestReceived));

        await friends.AcceptRequestAsync(fr.Id, 2); // bob nimmt an → alice wird informiert
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == 1 && n.Type == NotificationType.FriendRequestAccepted));
    }

    [Fact]
    public async Task Challenge_Create_And_Resolve_NotifyCounterparty()
    {
        await UserAsync(1, "alice");
        await UserAsync(2, "bob");
        _db.Friendships.Add(new Friendship { RequesterId = 1, AddresseeId = 2, Status = FriendshipStatus.Accepted });
        _db.Puzzles.Add(new Puzzle { Id = 100, Rating = 1500 });
        await _db.SaveChangesAsync();
        var challenges = new ChallengeService(_db, new FriendService(_db, _service), _service);

        var ch = await challenges.CreateAsync(1, 2, 100); // alice fordert bob
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == 2 && n.Type == NotificationType.ChallengeReceived));

        await challenges.ResolveAsync(ch.Id, 2, solved: true, timeSpentSeconds: 12); // bob löst → alice erfährt es
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == 1 && n.Type == NotificationType.ChallengeResolved));
    }

    [Fact]
    public async Task Revenge_Record_NotifiesTarget()
    {
        await UserAsync(1, "avenger");
        await UserAsync(2, "target");
        _db.Friendships.Add(new Friendship { RequesterId = 1, AddresseeId = 2, Status = FriendshipStatus.Accepted });
        _db.Puzzles.Add(new Puzzle { Id = 100, Rating = 1400 });
        _db.PuzzleAttempts.Add(new PuzzleAttempt { UserId = 2, PuzzleId = 100, Solved = false }); // Target ist gescheitert
        await _db.SaveChangesAsync();
        var revenge = new RevengeNotificationService(_db, new FriendService(_db, _service), _service);

        var created = await revenge.RecordAsync(avengerId: 1, targetId: 2, puzzleId: 100, solved: true);

        Assert.True(created);
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == 2 && n.Type == NotificationType.RevengePerformed));
    }
}
