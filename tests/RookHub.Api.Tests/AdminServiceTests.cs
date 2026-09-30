using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Admin-Entzug über <see cref="AdminService.ToggleAdminAsync"/>: er muss SOFORT wirken. Die Admin-Rolle steht
/// im Token (bis 30/90 Tage) und erfüllt jedes <c>[HasPermission]</c>; die vom <see cref="RoleSeeder"/> gespiegelte
/// System-Rolle „admin" gibt über den Live-Resolver alle Rechte — beides überlebte den Entzug vorher.</summary>
public class AdminServiceTests : IDisposable
{
    private const int ActorId = 990001;

    private readonly AppDbContext _db;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly CapturingLogger<AdminServiceTests> _log = new();

    public AdminServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    private async Task<AppUser> AddUserAsync(string name, bool isAdmin, string stamp)
    {
        var user = new AppUser { Username = name, Email = $"{name}@t.com", PasswordHash = "h", IsAdmin = isAdmin, SecurityStamp = stamp };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private Task<string?> RejectionAsync(int userId, string tokenStamp)
        => JwtTokenGate.RejectionReasonAsync(_db, _cache, userId, tokenStamp, _log, CancellationToken.None);

    [Fact]
    public async Task ToggleAdmin_Demote_RotatesStamp_AndTheOldTokenIsRejectedAtOnce()
    {
        var admin = await AddUserAsync("admin-b", isAdmin: true, stamp: "stamp-before");
        Assert.Null(await RejectionAsync(admin.Id, "stamp-before"));      // Token gilt — und der Zustand liegt jetzt im Cache

        var dto = await TestServices.Admin(_db, _cache).ToggleAdminAsync(admin.Id, ActorId);

        Assert.False(dto.IsAdmin);
        var reloaded = await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == admin.Id);
        Assert.NotEqual("stamp-before", reloaded.SecurityStamp);
        // Sofort, nicht erst nach Ablauf des 60-s-Caches: sonst ruft der Herabgestufte in dieser Minute toggle-admin
        // mit seinem alten Token auf und stuft ein Zweitkonto hoch.
        Assert.Equal("stamp-mismatch", await RejectionAsync(admin.Id, "stamp-before"));
    }

    [Fact]
    public async Task ToggleAdmin_Demote_RemovesTheMirroredAdminRole_SoTheLiveResolverGrantsNothing()
    {
        var admin = await AddUserAsync("admin-b", isAdmin: true, stamp: "s1");
        await RoleSeeder.SeedAsync(_db);                                   // Start: IsAdmin → UserRole „admin"
        var club = await new RoleAdminService(_db).CreateAsync(new CreateRoleDto
        {
            Key = "verein", Name = "Verein", Permissions = [Permissions.LeagueView],
        });
        _db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = club.Id });
        await _db.SaveChangesAsync();
        Assert.True((await PermissionResolver.LoadAsync(_db, admin.Id)).Permissions.Contains(Permissions.UsersManage));

        await TestServices.Admin(_db, _cache).ToggleAdminAsync(admin.Id, ActorId);

        var live = await PermissionResolver.LoadAsync(_db, admin.Id);
        Assert.False(live.IsAdmin);
        Assert.False(live.Has(Permissions.UsersManage));                  // auch mit neuem Token (ohne Admin-Rolle) nichts mehr
        Assert.True(live.Has(Permissions.LeagueView));                     // andere Rollen bleiben
        var adminRoleId = await _db.Roles.Where(r => r.Key == RoleSeeder.AdminKey).Select(r => r.Id).SingleAsync();
        Assert.False(await _db.UserRoles.AnyAsync(ur => ur.UserId == admin.Id && ur.RoleId == adminRoleId));
    }

    [Fact]
    public async Task ToggleAdmin_Promote_LinksTheAdminRole_AndKeepsTheSessions()
    {
        var user = await AddUserAsync("user-c", isAdmin: false, stamp: "s1");
        await RoleSeeder.SeedAsync(_db);

        var dto = await TestServices.Admin(_db, _cache).ToggleAdminAsync(user.Id, ActorId);

        Assert.True(dto.IsAdmin);
        var adminRoleId = await _db.Roles.Where(r => r.Key == RoleSeeder.AdminKey).Select(r => r.Id).SingleAsync();
        Assert.True(await _db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == adminRoleId));
        Assert.Null(await RejectionAsync(user.Id, "s1"));                  // Hochstufen meldet niemanden ab
    }

    // ---- Admin-Löschung über den Kern der Selbstlöschung (Codereview 2026-09-29, A9-004) ----

    [Fact]
    public async Task DeleteUser_UsesTheSelfDeletionCore_LeavesNoOrphansInColumnsWithoutFk()
    {
        var target = await AddUserAsync("schueler", isAdmin: false, stamp: "s1");
        var owner = await AddUserAsync("trainer", isAdmin: false, stamp: "s2");
        Assert.Null(await RejectionAsync(target.Id, "s1"));                 // Token gilt — Zustand liegt jetzt im Cache
        var ownBook = new Book { FileName = "privat.pgn", DisplayName = "Privat", OwnerUserId = target.Id, Source = new BookSource() };
        var seriesBook = new Book { FileName = "serie.pgn", DisplayName = "Serie", IsCalculation = true, OwnerUserId = owner.Id, Source = new BookSource() };
        _db.Books.AddRange(ownBook, seriesBook);
        await _db.SaveChangesAsync();
        // Spalten OHNE FK auf AppUsers — die harte Löschung ließ sie als Waisen stehen:
        _db.CalcSeriesMembers.Add(new CalcSeriesMember { BookId = seriesBook.Id, UserId = target.Id });
        _db.LeagueClubDrafts.Add(new LeagueClubDraft { UserId = target.Id, Pgn = "[White \"Max Mustermann\"]" });
        _db.CatalogGrants.Add(new CatalogGrant { OwnerUserId = owner.Id, SubjectUserId = target.Id });
        // Restrict-FK (Freigabe an ihn) — hart gelöscht endete das in 409:
        _db.CourseShares.Add(new CourseShare { BookId = seriesBook.Id, OwnerId = owner.Id, RecipientId = target.Id });
        await _db.SaveChangesAsync();

        await TestServices.Admin(_db, _cache).DeleteUserAsync(target.Id, ActorId);

        var user = await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == target.Id);
        Assert.NotNull(user.DeletedAt);                                      // anonymisiert wie die Selbstlöschung
        Assert.Equal($"deleted_{target.Id}", user.Username);
        Assert.False(await _db.CalcSeriesMembers.AnyAsync(m => m.UserId == target.Id));
        Assert.False(await _db.LeagueClubDrafts.AnyAsync(d => d.UserId == target.Id));
        Assert.False(await _db.CatalogGrants.AnyAsync(g => g.SubjectUserId == target.Id));
        Assert.False(await _db.Books.AnyAsync(b => b.OwnerUserId == target.Id));
        Assert.True(await _db.Books.AnyAsync(b => b.Id == seriesBook.Id));   // fremdes Buch bleibt
        // Sofort abgemeldet, nicht erst nach Ablauf des 60-s-Caches.
        Assert.Equal("inactive-user", await RejectionAsync(target.Id, "s1"));
    }

    [Fact]
    public async Task DeleteUser_Twice_IsIdempotent_AndDeletedAccountsLeaveTheUserList()
    {
        var target = await AddUserAsync("einmal", isAdmin: false, stamp: "s1");
        var admin = TestServices.Admin(_db, _cache);

        await admin.DeleteUserAsync(target.Id, ActorId);
        await admin.DeleteUserAsync(target.Id, ActorId);                     // kein Fehler, nichts doppelt

        var (items, total, _, _) = await admin.GetUsersAsync(null, 1, 20);
        Assert.DoesNotContain(items, u => u.Id == target.Id);                // Zeile bleibt (Statistik), nicht in der Verwaltung
        Assert.Equal(0, total);
    }

    [Fact]
    public async Task DeleteUser_Unknown_ThrowsKeyNotFound_AndSelf_IsRejected()
    {
        var admin = TestServices.Admin(_db, _cache);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => admin.DeleteUserAsync(424242, ActorId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.DeleteUserAsync(ActorId, ActorId));
    }
}
