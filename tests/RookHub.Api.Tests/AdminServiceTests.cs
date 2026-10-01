using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
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
        var self = await Assert.ThrowsAsync<DomainValidationException>(() => admin.DeleteUserAsync(ActorId, ActorId));
        Assert.Equal(ApiErrorCodes.AdminSelfDelete, self.Code);
    }

    [Fact]
    public async Task DeleteUser_AdminTarget_OnlyByAnAdmin()
    {
        var admin = TestServices.Admin(_db, _cache);
        var victim = await AddUserAsync("adminkonto", isAdmin: true, stamp: "s1");

        // Delegierte Rolle (users.manage, selbst kein Admin) löscht keine Admins (N9-002).
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admin.DeleteUserAsync(victim.Id, ActorId, actorIsAdmin: false));
        Assert.Null((await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == victim.Id)).DeletedAt);
        // Unbekannt bleibt 404, nicht 403.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => admin.DeleteUserAsync(424242, ActorId, actorIsAdmin: false));

        await admin.DeleteUserAsync(victim.Id, ActorId, actorIsAdmin: true);
        Assert.NotNull((await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == victim.Id)).DeletedAt);
    }

    // ---- Sperren statt Löschen (Codereview 2026-09-29, F5-011) ----

    [Fact]
    public async Task LockUser_RejectsTheRunningTokenAtOnce_KeepsTheAccount_AndShowsTheLockInTheList()
    {
        var target = await AddUserAsync("spammer", isAdmin: false, stamp: "s1");
        Assert.Null(await RejectionAsync(target.Id, "s1"));                 // Token gilt — Zustand liegt jetzt im Cache
        var until = DateTime.UtcNow.AddDays(7);
        var admin = TestServices.Admin(_db, _cache);

        var dto = await admin.LockUserAsync(target.Id, ActorId, until);

        Assert.Equal(until, dto.LockedUntil);
        Assert.Equal(DateTimeKind.Utc, dto.LockedUntil!.Value.Kind);
        var user = await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == target.Id);
        Assert.Null(user.DeletedAt);                                         // umkehrbar: Konto bleibt, wie es war
        Assert.Equal("spammer", user.Username);
        Assert.NotEqual("s1", user.SecurityStamp);                           // Stempel rotiert
        Assert.Equal("inactive-user", await RejectionAsync(target.Id, "s1")); // sofort, nicht erst nach 60 s
        // Auch ein frisches Token (neuer Stempel) gilt nicht, solange die Sperre läuft.
        Assert.Equal("inactive-user", await RejectionAsync(target.Id, user.SecurityStamp!));
        var (items, _, _, _) = await admin.GetUsersAsync(null, 1, 20);
        Assert.Equal(until, Assert.Single(items).LockedUntil);
    }

    [Fact]
    public async Task LockUser_WithoutEnd_IsIndefinite_AndUnlockLetsNewTokensThrough()
    {
        var target = await AddUserAsync("dauer", isAdmin: false, stamp: "s1");
        var admin = TestServices.Admin(_db, _cache);

        Assert.Equal(AppUser.LockedIndefinitely, (await admin.LockUserAsync(target.Id, ActorId, until: null)).LockedUntil);
        var stampAfterLock = (await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == target.Id)).SecurityStamp!;
        Assert.Equal("inactive-user", await RejectionAsync(target.Id, stampAfterLock));

        var dto = await admin.UnlockUserAsync(target.Id, ActorId);
        await admin.UnlockUserAsync(target.Id, ActorId);                     // idempotent

        Assert.Null(dto.LockedUntil);
        Assert.Null((await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == target.Id)).LockedUntil);
        Assert.Null(await RejectionAsync(target.Id, stampAfterLock));        // neu angemeldet: gilt wieder (Cache verworfen)
        Assert.Equal("stamp-mismatch", await RejectionAsync(target.Id, "s1")); // die vor der Sperre bleibt entwertet
    }

    [Fact]
    public async Task ExpiredLock_NoLongerCounts()
    {
        var target = await AddUserAsync("abgelaufen", isAdmin: false, stamp: "s1");
        target.LockedUntil = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        Assert.Null(await RejectionAsync(target.Id, "s1"));
        var (items, _, _, _) = await TestServices.Admin(_db, _cache).GetUsersAsync(null, 1, 20);
        Assert.Null(Assert.Single(items).LockedUntil);
    }

    [Fact]
    public async Task LockUser_Guards_Self_Past_Deleted_AndAdminsOnlyByAdmins()
    {
        var admin = TestServices.Admin(_db, _cache);
        var victim = await AddUserAsync("adminkonto", isAdmin: true, stamp: "s1");
        var gone = await AddUserAsync("weg", isAdmin: false, stamp: "s2");
        gone.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.LockUserAsync(ActorId, ActorId, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.LockUserAsync(victim.Id, ActorId, DateTime.UtcNow.AddMinutes(-5)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => admin.LockUserAsync(gone.Id, ActorId, null));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => admin.LockUserAsync(424242, ActorId, null));
        // Delegierte Rolle (users.manage, selbst kein Admin) sperrt keine Admins aus.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admin.LockUserAsync(victim.Id, ActorId, null, actorIsAdmin: false));
        Assert.Null((await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == victim.Id)).LockedUntil);
        Assert.Equal("s1", (await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == victim.Id)).SecurityStamp);
    }

    [Fact]
    public async Task LockUser_UntilWithOffset_IsStoredAsUtc()
    {
        var target = await AddUserAsync("offset", isAdmin: false, stamp: "s1");
        var local = new DateTimeOffset(2099, 1, 1, 12, 0, 0, TimeSpan.FromHours(2));

        var dto = await TestServices.Admin(_db, _cache).LockUserAsync(target.Id, ActorId, local.LocalDateTime);

        Assert.Equal(new DateTime(2099, 1, 1, 10, 0, 0, DateTimeKind.Utc), dto.LockedUntil);
    }
}
