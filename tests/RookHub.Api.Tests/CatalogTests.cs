using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Katalog: Freigabe der Kurs-/Repertoire-Liste an User/Gruppen + Item-Anforderung mit
/// Besitzer-Genehmigung (→ bestehendes Teilen) bzw. Ablehnung.
/// </summary>
public class CatalogTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CatalogService _svc;
    private const int Owner = 5;     // Admin/Besitzer
    private const int Viewer = 42;

    public CatalogTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        var notifications = new NotificationService(_db);
        var reps = new RepertoireService(_db, new RepertoireAnalyzeService(_db, new MemoryCache(new MemoryCacheOptions())),
            new FriendService(_db, notifications), notifications);
        var courses = new CourseService(_db, NullLogger<CourseService>.Instance, new PgnImportService(_db),
            new BookAdminService(_db), reps, new FriendService(_db, notifications), notifications);
        _svc = new CatalogService(_db, courses, reps, notifications);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedUsersAsync()
    {
        _db.AppUsers.Add(new AppUser { Id = Owner, Username = "owner", PasswordHash = "x" });
        _db.AppUsers.Add(new AppUser { Id = Viewer, Username = "viewer", PasswordHash = "x" });
        await _db.SaveChangesAsync();
    }

    private async Task<int> SeedCourseAsync(string name = "Course A")
    {
        var b = new Book { FileName = $"b-{Guid.NewGuid():N}.pgn", DisplayName = name, OwnerUserId = Owner,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(b); await _db.SaveChangesAsync(); return b.Id;
    }

    private async Task<int> SeedRepertoireAsync(string name = "Rep A")
    {
        var r = new Repertoire { UserId = Owner, Name = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Repertoires.Add(r); await _db.SaveChangesAsync(); return r.Id;
    }

    [Fact]
    public async Task SetAndGetGrants_FiltersInvalidAndSelf()
    {
        await SeedUsersAsync();
        _db.Groups.Add(new Group { Id = 7, Name = "G7" }); await _db.SaveChangesAsync();

        var res = await _svc.SetGrantsAsync(Owner, new List<int> { Viewer, Owner, 9999 }, new List<int> { 7, 8888 });
        Assert.Equal(new List<int> { Viewer }, res.UserIds);   // self + unbekannt gefiltert
        Assert.Equal(new List<int> { 7 }, res.GroupIds);       // unbekannte Gruppe gefiltert

        var got = await _svc.GetGrantsAsync(Owner);
        Assert.Equal(new List<int> { Viewer }, got.UserIds);
        Assert.Equal(new List<int> { 7 }, got.GroupIds);
    }

    [Fact]
    public async Task HasAccess_DirectUser_Group_None_Admin()
    {
        await SeedUsersAsync();
        Assert.False(await _svc.HasAccessAsync(Viewer, isAdmin: false));   // keine Freigabe
        Assert.True(await _svc.HasAccessAsync(Viewer, isAdmin: true));     // Admin immer

        await _svc.SetGrantsAsync(Owner, new List<int> { Viewer }, new List<int>());
        Assert.True(await _svc.HasAccessAsync(Viewer, isAdmin: false));    // direkt freigegeben

        // Über Gruppe: neuer User in Gruppe, Gruppe freigegeben.
        _db.Groups.Add(new Group { Id = 3, Name = "Klasse" });
        _db.AppUsers.Add(new AppUser { Id = 77, Username = "u77", PasswordHash = "x" });
        _db.UserGroups.Add(new UserGroup { UserId = 77, GroupId = 3 });
        await _db.SaveChangesAsync();
        await _svc.SetGrantsAsync(Owner, new List<int> { Viewer }, new List<int> { 3 });
        Assert.True(await _svc.HasAccessAsync(77, isAdmin: false));
    }

    [Fact]
    public async Task GetCatalog_ListsOwnerItems_WithStatuses()
    {
        await SeedUsersAsync();
        var courseId = await SeedCourseAsync();
        var repId = await SeedRepertoireAsync();
        await _svc.SetGrantsAsync(Owner, new List<int> { Viewer }, new List<int>());

        var cat = await _svc.GetCatalogAsync(Viewer);
        Assert.Equal(2, cat.Count);
        Assert.All(cat, i => Assert.Equal("none", i.Status));
        Assert.Contains(cat, i => i.ItemType == "course" && i.ItemId == courseId);
        Assert.Contains(cat, i => i.ItemType == "repertoire" && i.ItemId == repId);

        // Ohne Freigabe → leer.
        Assert.Empty(await _svc.GetCatalogAsync(999));
    }

    [Fact]
    public async Task Request_CreatesPending_Notifies_AndIsIdempotent()
    {
        await SeedUsersAsync();
        var courseId = await SeedCourseAsync();
        await _svc.SetGrantsAsync(Owner, new List<int> { Viewer }, new List<int>());

        Assert.Equal("pending", await _svc.RequestAsync(Viewer, "course", courseId));
        Assert.Equal("pending", await _svc.RequestAsync(Viewer, "course", courseId));   // idempotent
        Assert.Single(_db.CatalogRequests.Where(r => r.Status == "pending"));

        // Besitzer wurde benachrichtigt.
        Assert.Contains(_db.Notifications, n => n.UserId == Owner && n.Type == NotificationType.CatalogRequestReceived);

        // Katalog zeigt jetzt "pending".
        var cat = await _svc.GetCatalogAsync(Viewer);
        Assert.Equal("pending", cat.Single(i => i.ItemId == courseId).Status);
    }

    [Fact]
    public async Task Request_WithoutAccess_Throws()
    {
        await SeedUsersAsync();
        var courseId = await SeedCourseAsync();   // KEINE Freigabe an Viewer
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.RequestAsync(Viewer, "course", courseId));
    }

    [Fact]
    public async Task Approve_SharesCourse_AndMarksApproved()
    {
        await SeedUsersAsync();
        var courseId = await SeedCourseAsync();
        await _svc.SetGrantsAsync(Owner, new List<int> { Viewer }, new List<int>());
        await _svc.RequestAsync(Viewer, "course", courseId);
        var reqId = _db.CatalogRequests.Single().Id;

        await _svc.ApproveAsync(Owner, reqId, isAdmin: true);

        Assert.Equal("approved", _db.CatalogRequests.Single().Status);
        Assert.Contains(_db.CourseShares, s => s.BookId == courseId && s.RecipientId == Viewer && s.OwnerId == Owner);
        // Danach zeigt der Katalog "shared".
        var cat = await _svc.GetCatalogAsync(Viewer);
        Assert.Equal("shared", cat.Single(i => i.ItemId == courseId).Status);
    }

    [Fact]
    public async Task Approve_SharesRepertoire()
    {
        await SeedUsersAsync();
        var repId = await SeedRepertoireAsync();
        await _svc.SetGrantsAsync(Owner, new List<int> { Viewer }, new List<int>());
        await _svc.RequestAsync(Viewer, "repertoire", repId);
        var reqId = _db.CatalogRequests.Single().Id;

        await _svc.ApproveAsync(Owner, reqId, isAdmin: true);
        Assert.Contains(_db.RepertoireShares, s => s.RepertoireId == repId && s.RecipientId == Viewer);
    }

    [Fact]
    public async Task Decline_MarksDeclined_AndNotifiesRequester()
    {
        await SeedUsersAsync();
        var courseId = await SeedCourseAsync();
        await _svc.SetGrantsAsync(Owner, new List<int> { Viewer }, new List<int>());
        await _svc.RequestAsync(Viewer, "course", courseId);
        var reqId = _db.CatalogRequests.Single().Id;

        await _svc.DeclineAsync(Owner, reqId);
        Assert.Equal("declined", _db.CatalogRequests.Single().Status);
        Assert.Contains(_db.Notifications, n => n.UserId == Viewer && n.Type == NotificationType.CatalogRequestDeclined);
        Assert.DoesNotContain(_db.CourseShares, s => s.BookId == courseId && s.RecipientId == Viewer);
    }

    [Fact]
    public async Task Approve_WrongOwner_Throws()
    {
        await SeedUsersAsync();
        var courseId = await SeedCourseAsync();
        await _svc.SetGrantsAsync(Owner, new List<int> { Viewer }, new List<int>());
        await _svc.RequestAsync(Viewer, "course", courseId);
        var reqId = _db.CatalogRequests.Single().Id;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.ApproveAsync(999, reqId, isAdmin: true));
    }
}
