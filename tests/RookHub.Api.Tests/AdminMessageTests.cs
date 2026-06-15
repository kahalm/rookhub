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

/// <summary>Admin↔User-Direktnachrichten: Service, beide Controller und die Glocken-Trigger.</summary>
public class AdminMessageTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly NotificationService _notifications;
    private readonly AdminMessageService _service;

    public AdminMessageTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _notifications = new NotificationService(_db);
        _service = new AdminMessageService(_db, _notifications);
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> UserAsync(int id, string name, bool admin = false)
    {
        var u = new AppUser { Id = id, Username = name, PasswordHash = "x", IsAdmin = admin };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    // ---- Service: Admin sendet ----

    [Fact]
    public async Task SendFromAdmin_CreatesMessage_AndNotifiesUser()
    {
        await UserAsync(1, "admin", admin: true);
        await UserAsync(2, "bob");

        var dto = await _service.SendFromAdminAsync(adminId: 1, targetUserId: 2, "Hallo Bob");

        Assert.True(dto.FromAdmin);
        Assert.Equal("Hallo Bob", dto.Body);
        var msg = await _db.AdminMessages.SingleAsync();
        Assert.Equal(2, msg.UserId);
        Assert.Equal(1, msg.SenderId);
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == 2 && n.Type == NotificationType.AdminMessageReceived && n.Link == "/messages"));
    }

    [Fact]
    public async Task SendFromAdmin_UnknownUser_Throws()
    {
        await UserAsync(1, "admin", admin: true);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.SendFromAdminAsync(1, 999, "hi"));
    }

    [Fact]
    public async Task SendFromAdmin_EmptyBody_Throws()
    {
        await UserAsync(1, "admin", admin: true);
        await UserAsync(2, "bob");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SendFromAdminAsync(1, 2, "   "));
    }

    [Fact]
    public async Task SendFromAdmin_OverlongBody_IsTruncated()
    {
        await UserAsync(1, "admin", admin: true);
        await UserAsync(2, "bob");
        var dto = await _service.SendFromAdminAsync(1, 2, new string('x', AdminMessageService.MaxBodyLength + 500));
        Assert.Equal(AdminMessageService.MaxBodyLength, dto.Body.Length);
    }

    // ---- Service: User antwortet ----

    [Fact]
    public async Task ReplyFromUser_WithoutThread_Throws()
    {
        await UserAsync(2, "bob");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ReplyFromUserAsync(2, "darf ich nicht"));
    }

    [Fact]
    public async Task ReplyFromUser_AfterAdminStarted_NotifiesAllAdmins()
    {
        await UserAsync(1, "admin1", admin: true);
        await UserAsync(2, "bob");
        await UserAsync(3, "admin2", admin: true);
        await _service.SendFromAdminAsync(1, 2, "Hallo");

        var dto = await _service.ReplyFromUserAsync(2, "Hi zurück");

        Assert.False(dto.FromAdmin);
        // beide Admins werden informiert, der User selbst nicht
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == 1 && n.Type == NotificationType.UserMessageReceived));
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == 3 && n.Type == NotificationType.UserMessageReceived));
        Assert.False(await _db.Notifications.AnyAsync(n => n.UserId == 2 && n.Type == NotificationType.UserMessageReceived));
    }

    // ---- Service: Thread + Read-Receipts ----

    [Fact]
    public async Task GetThread_IsChronological()
    {
        await UserAsync(1, "admin", admin: true);
        await UserAsync(2, "bob");
        await _service.SendFromAdminAsync(1, 2, "erste");
        await _service.ReplyFromUserAsync(2, "zweite");
        await _service.SendFromAdminAsync(1, 2, "dritte");

        var thread = await _service.GetThreadAsync(2);
        Assert.Equal(new[] { "erste", "zweite", "dritte" }, thread.Select(m => m.Body).ToArray());
        Assert.Equal(new[] { true, false, true }, thread.Select(m => m.FromAdmin).ToArray());
    }

    [Fact]
    public async Task UserUnread_And_MarkSeenByUser()
    {
        await UserAsync(1, "admin", admin: true);
        await UserAsync(2, "bob");
        await _service.SendFromAdminAsync(1, 2, "a");
        await _service.SendFromAdminAsync(1, 2, "b");

        Assert.Equal(2, await _service.CountUnreadForUserAsync(2));
        await _service.MarkSeenByUserAsync(2);
        Assert.Equal(0, await _service.CountUnreadForUserAsync(2));
    }

    [Fact]
    public async Task AdminUnread_CountsOnlyUserReplies_And_MarkSeenByAdmin()
    {
        await UserAsync(1, "admin", admin: true);
        await UserAsync(2, "bob");
        await _service.SendFromAdminAsync(1, 2, "a");          // zählt NICHT (vom Admin)
        await _service.ReplyFromUserAsync(2, "r1");
        await _service.ReplyFromUserAsync(2, "r2");

        Assert.Equal(2, await _service.CountUnreadForAdminAsync());
        await _service.MarkSeenByAdminAsync(2);
        Assert.Equal(0, await _service.CountUnreadForAdminAsync());
    }

    [Fact]
    public async Task HasThreadForUser_FalseUntilAdminWrites()
    {
        await UserAsync(1, "admin", admin: true);
        await UserAsync(2, "bob");

        Assert.False(await _service.HasThreadForUserAsync(2));   // noch keine Nachricht → Icon ausgeblendet
        await _service.SendFromAdminAsync(1, 2, "hallo");
        Assert.True(await _service.HasThreadForUserAsync(2));    // jetzt vorhanden → Icon sichtbar
    }

    [Fact]
    public async Task ReadByRecipient_ReflectsSeenState()
    {
        await UserAsync(1, "admin", admin: true);
        await UserAsync(2, "bob");
        await _service.SendFromAdminAsync(1, 2, "a");

        Assert.False((await _service.GetThreadAsync(2)).Single().ReadByRecipient);
        await _service.MarkSeenByUserAsync(2);
        Assert.True((await _service.GetThreadAsync(2)).Single().ReadByRecipient);
    }

    [Fact]
    public async Task GetThreads_SummarizesPerUser_WithLastMessageAndUnread()
    {
        await UserAsync(1, "admin", admin: true);
        await UserAsync(2, "bob");
        await UserAsync(3, "carol");
        await _service.SendFromAdminAsync(1, 2, "an bob");
        await _service.ReplyFromUserAsync(2, "bob antwortet");   // 1 ungelesen (Admin-Sicht)
        await _service.SendFromAdminAsync(1, 3, "an carol");

        var threads = await _service.GetThreadsAsync();

        Assert.Equal(2, threads.Count);
        var bob = threads.Single(t => t.UserId == 2);
        Assert.Equal("bob", bob.Username);
        Assert.Equal("bob antwortet", bob.LastMessagePreview);
        Assert.False(bob.LastFromAdmin);
        Assert.Equal(1, bob.UnreadFromUser);
        var carol = threads.Single(t => t.UserId == 3);
        Assert.True(carol.LastFromAdmin);
        Assert.Equal(0, carol.UnreadFromUser);
    }

    // ---- Controller: User-Seite ----

    private MessageController UserController(int userId)
    {
        var ctrl = new MessageController(_service);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return ctrl;
    }

    private AdminMessageController AdminController(int adminId)
    {
        var ctrl = new AdminMessageController(_service);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, adminId.ToString()) };
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return ctrl;
    }

    [Fact]
    public async Task UserReply_WithoutThread_Returns400()
    {
        await UserAsync(2, "bob");
        var result = await UserController(2).Reply(new SendMessageDto("hallo"));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task FullRoundTrip_AdminSends_UserReplies_AdminReads()
    {
        await UserAsync(1, "admin", admin: true);
        await UserAsync(2, "bob");

        // Admin schreibt
        Assert.IsType<OkObjectResult>(await AdminController(1).Send(2, new SendMessageDto("Hallo Bob")));

        // User sieht 1 ungelesen + hat eine Konversation, lädt Thread, markiert gelesen
        var status = Assert.IsType<UserMessageStatusDto>(Assert.IsType<OkObjectResult>(await UserController(2).GetStatus()).Value);
        Assert.Equal(1, status.Unread);
        Assert.True(status.HasMessages);
        var thread = Assert.IsAssignableFrom<IEnumerable<AdminMessageDto>>(Assert.IsType<OkObjectResult>(await UserController(2).GetThread()).Value).ToList();
        Assert.Single(thread);
        Assert.IsType<NoContentResult>(await UserController(2).MarkSeen());
        Assert.Equal(0, ((UserMessageStatusDto)Assert.IsType<OkObjectResult>(await UserController(2).GetStatus()).Value!).Unread);

        // User antwortet → Admin sieht ungelesen, liest, markiert
        Assert.IsType<OkObjectResult>(await UserController(2).Reply(new SendMessageDto("Hi!")));
        Assert.Equal(1, ((MessageUnreadCountDto)Assert.IsType<OkObjectResult>(await AdminController(1).GetUnreadCount()).Value!).Count);
        var threads = Assert.IsAssignableFrom<IEnumerable<AdminThreadSummaryDto>>(Assert.IsType<OkObjectResult>(await AdminController(1).GetThreads()).Value).ToList();
        Assert.Single(threads);
        Assert.IsType<NoContentResult>(await AdminController(1).MarkSeen(2));
        Assert.Equal(0, ((MessageUnreadCountDto)Assert.IsType<OkObjectResult>(await AdminController(1).GetUnreadCount()).Value!).Count);
    }

    [Fact]
    public async Task AdminSend_UnknownUser_Returns404()
    {
        await UserAsync(1, "admin", admin: true);
        Assert.IsType<NotFoundResult>(await AdminController(1).Send(999, new SendMessageDto("hi")));
    }
}
