using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Codereview F5-005: eine Rolle mit einem Admin-Bereichs-Recht wirkt überall, wo das Recht gemeint ist — nicht
/// nur an den <c>[HasPermission]</c>-Endpunkten. Vorher hingen Glocken-Empfänger, Katalog-Besitz, die Sichtbarkeit noch
/// nicht fälliger Wochenposts und der Push-Bereich „admin" am Admin-Flag; <c>catalog.manage</c> schaltete gar nichts frei.</summary>
public class RbacWiringTests : IDisposable
{
    private const int AdminId = 991501, SupportId = 991502, GroupSupportId = 991503, PlainId = 991504, GoneId = 991505;
    private readonly AppDbContext _db;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public RbacWiringTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    /// <summary>Admin, „Support" mit eigener Rolle, ein Gruppenmitglied mit derselben Rolle über die Gruppe, ein normales
    /// Konto und ein gelöschtes Konto, das die Rolle noch trägt.</summary>
    private async Task SeedAsync(params string[] supportPermissions)
    {
        await RoleSeeder.SeedAsync(_db);
        _db.AppUsers.AddRange(
            new AppUser { Id = AdminId, Username = "admin", PasswordHash = "x", IsAdmin = true },
            new AppUser { Id = SupportId, Username = "support", PasswordHash = "x" },
            new AppUser { Id = GroupSupportId, Username = "groupsupport", PasswordHash = "x" },
            new AppUser { Id = PlainId, Username = "plain", PasswordHash = "x" },
            new AppUser { Id = GoneId, Username = "gone", PasswordHash = "x", DeletedAt = DateTime.UtcNow });
        var role = new Role { Key = "support", Name = "Support" };
        var group = new Group { Name = "Support-Team" };
        _db.Roles.Add(role);
        _db.Groups.Add(group);
        await _db.SaveChangesAsync();
        foreach (var p in supportPermissions) _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = p });
        _db.UserRoles.Add(new UserRole { UserId = SupportId, RoleId = role.Id });
        _db.UserRoles.Add(new UserRole { UserId = GoneId, RoleId = role.Id });
        _db.GroupRoles.Add(new GroupRole { GroupId = group.Id, RoleId = role.Id });
        _db.UserGroups.Add(new UserGroup { UserId = GroupSupportId, GroupId = group.Id });
        await _db.SaveChangesAsync();
        PermissionResolver.InvalidateAll();
    }

    private static ControllerContext Ctx(int userId, bool admin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        return new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) } };
    }

    private PermissionResolver Resolver() => new(_db, _cache);

    [Fact]
    public async Task UserIdsWithPermission_AdminsOwnRoleAndGroupRole_NotOthersNotDeleted()
    {
        await SeedAsync(Permissions.MessagesAdmin);
        var ids = await PermissionResolver.UserIdsWithPermissionAsync(_db, Permissions.MessagesAdmin);
        Assert.Equal(new[] { AdminId, SupportId, GroupSupportId }, ids.OrderBy(i => i));

        // Ein Recht, das die Rolle nicht trägt: nur der Admin.
        Assert.Equal(new[] { AdminId }, await PermissionResolver.UserIdsWithPermissionAsync(_db, Permissions.UsersManage));
    }

    [Fact]
    public async Task UserMessage_RingsEveryoneWithMessagesAdmin_NotJustTheAdminFlag()
    {
        await SeedAsync(Permissions.MessagesAdmin);
        await new AdminMessageService(_db, new NotificationService(_db)).SendFromUserAsync(PlainId, "Hallo?");

        var rung = await _db.Notifications.Where(n => n.Type == NotificationType.UserMessageReceived)
            .Select(n => n.UserId).OrderBy(i => i).ToListAsync();
        Assert.Equal(new[] { AdminId, SupportId, GroupSupportId }, rung);
    }

    [Fact]
    public async Task Catalog_CatalogManageRole_IsAnOwner_PlainUserStillForbidden()
    {
        await SeedAsync(Permissions.CatalogManage);
        var notifications = new NotificationService(_db);
        var friends = new FriendService(_db, notifications);
        var catalog = new CatalogService(_db,
            new CourseService(_db, NullLogger<CourseService>.Instance, new PgnImportService(_db), new BookAdminService(_db), friends, notifications),
            new RepertoireService(_db, new RepertoireAnalyzeService(_db, _cache), friends, notifications),
            notifications);

        var support = new CatalogController(catalog, Resolver()) { ControllerContext = Ctx(SupportId) };
        Assert.IsType<OkObjectResult>((await support.GetGrants()).Result);
        Assert.IsType<OkObjectResult>((await support.GetRequests()).Result);
        var access = Assert.IsType<OkObjectResult>((await support.Access()).Result);
        Assert.True(Assert.IsType<CatalogAccessDto>(access.Value).HasAccess);

        var plain = new CatalogController(catalog, Resolver()) { ControllerContext = Ctx(PlainId) };
        Assert.IsType<ForbidResult>((await plain.GetGrants()).Result);
    }

    [Fact]
    public async Task WeeklyPosts_UnpublishedVisibleToWeeklyPostsManageRole_NotToPlainUser()
    {
        await SeedAsync(Permissions.WeeklyPostsManage);
        var post = new WeeklyPost { Title = "Nächste Woche", FileName = "w.pgn", PgnContent = "1. e4 *", ScheduledAt = DateTime.UtcNow.AddDays(3) };
        _db.WeeklyPosts.Add(post);
        await _db.SaveChangesAsync();
        WeeklyPostController Controller(int userId) => new(_db, new WeeklyPostService(_db, NullLogger<WeeklyPostService>.Instance),
            permissions: Resolver()) { ControllerContext = Ctx(userId) };

        var asSupport = Assert.IsType<OkObjectResult>(await Controller(SupportId).GetAll());
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<WeeklyPostDto>>(asSupport.Value));
        Assert.IsType<OkObjectResult>(await Controller(SupportId).GetById(post.Id));

        var asPlain = Assert.IsType<OkObjectResult>(await Controller(PlainId).GetAll());
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<WeeklyPostDto>>(asPlain.Value));
        Assert.IsType<NotFoundObjectResult>(await Controller(PlainId).GetById(post.Id));
    }

    [Fact]
    public async Task PushAdminArea_AllowedForUsersManageRole_DroppedForPlainUser()
    {
        await SeedAsync(Permissions.UsersManage);
        NotificationController Controller(int userId) => new(new NotificationService(_db),
            new PushNotificationService(_db, new NoopSender(), Options.Create(new WebPushOptions()), NullLogger<PushNotificationService>.Instance),
            Resolver()) { ControllerContext = Ctx(userId) };

        await Controller(SupportId).PushPreferences(new PushPreferencesInputDto { Categories = ["admin"] });
        await Controller(PlainId).PushPreferences(new PushPreferencesInputDto { Categories = ["admin", "courses"] });

        Assert.Equal("admin", (await _db.NotificationPushSettings.SingleAsync(s => s.UserId == SupportId)).EnabledCategories);
        Assert.Equal("courses", (await _db.NotificationPushSettings.SingleAsync(s => s.UserId == PlainId)).EnabledCategories);
    }

    private sealed class NoopSender : IWebPushSender
    {
        public Task SendAsync(UserPushSubscription sub, string payloadJson, WebPushOptions opts, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
