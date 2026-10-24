using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using RookHub.Api.Authorization;
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
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Puzzles;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Social;
using RookHub.Api.Controllers.Social;
using RookHub.Api.DTOs.Puzzles;
using RookHub.Api.Controllers.Puzzles;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.DTOs.Social;
using RookHub.Api.Controllers.Courses;
using RookHub.Api.Controllers.Accounts;
using RookHub.Api.DTOs.Accounts;

namespace RookHub.Api.Tests.Social;

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

    /// <summary>Wertet die <c>[HasPermission]</c>-Policy eines Controllers so aus wie die Pipeline (Provider + Handler mit
    /// Live-Resolver) — ein direkt instanziierter Controller prüft Attribute nie.</summary>
    private async Task<bool> PolicyAllowsAsync(Type controller, int userId, bool admin = false)
    {
        var attr = controller.GetCustomAttribute<HasPermissionAttribute>()!;
        var policy = await new PermissionPolicyProvider(Options.Create(new AuthorizationOptions())).GetPolicyAsync(attr.Policy!);
        var reqs = policy!.Requirements.OfType<PermissionRequirement>().ToList();
        Assert.NotEmpty(reqs);
        var ctx = new AuthorizationHandlerContext(reqs, Ctx(userId, admin).HttpContext.User, null);
        await new PermissionAuthorizationHandler(Resolver()).HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    /// <summary>F5-005-Nachlauf: die Gruppen-Auswahlliste bekommt, wer Gruppen nur ZUORDNET (Katalog, Menü, Rollen,
    /// Bücher), nicht nur groups.manage — auch über eine Gruppenrolle; und sie verrät keine Mitglieder (nur deren Zahl).</summary>
    [Theory]
    [InlineData("catalog.manage")]
    [InlineData("menu.manage")]
    [InlineData("roles.manage")]
    [InlineData("books.manage")]
    [InlineData("groups.manage")]
    public async Task GroupOptions_AnyGroupAssigningPermission_Allowed_NoMembers(string permission)
    {
        await SeedAsync(permission);
        Assert.True(await PolicyAllowsAsync(typeof(GroupOptionsController), SupportId));
        Assert.True(await PolicyAllowsAsync(typeof(GroupOptionsController), GroupSupportId));
        Assert.False(await PolicyAllowsAsync(typeof(GroupOptionsController), PlainId));
        // Der volle Gruppen-Endpunkt bleibt bei groups.manage.
        Assert.Equal(permission == "groups.manage", await PolicyAllowsAsync(typeof(GroupController), SupportId));

        var res = await new GroupOptionsController(_db) { ControllerContext = Ctx(SupportId) }.Get(default);
        var g = Assert.Single(Assert.IsType<List<GroupOptionDto>>(Assert.IsType<OkObjectResult>(res.Result).Value));
        Assert.Equal("Support-Team", g.Name);
        Assert.False(g.IsEveryone);
        Assert.Equal(1, g.MemberCount);   // groupsupport — gezählt, nicht genannt
        Assert.Equal(["Id", "IsEveryone", "MemberCount", "Name"], typeof(GroupOptionDto).GetProperties().Select(p => p.Name).OrderBy(n => n));
    }

    [Fact]
    public async Task GroupOptions_WithoutAnyAssigningPermission_Denied_AdminAllowed()
    {
        await SeedAsync(Permissions.MessagesAdmin, Permissions.UsersManage);
        Assert.False(await PolicyAllowsAsync(typeof(GroupOptionsController), SupportId));
        Assert.False(await PolicyAllowsAsync(typeof(GroupOptionsController), PlainId));
        Assert.True(await PolicyAllowsAsync(typeof(GroupOptionsController), AdminId, admin: true));
    }

    private sealed class NoopSender : IWebPushSender
    {
        public Task SendAsync(UserPushSubscription sub, string payloadJson, WebPushOptions opts, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
