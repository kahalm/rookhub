using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Admin-Backend-Operationen für Benutzer- und Puzzle-Verwaltung (vormals inline im AdminController).
/// Self-Delete/Self-Toggle → <see cref="InvalidOperationException"/> (400), nicht gefunden →
/// <see cref="KeyNotFoundException"/> (404). Ein <see cref="DbUpdateException"/> aus
/// <see cref="DeleteUserAsync"/> propagiert bewusst zum Controller (→ 409).
/// </summary>
public class AdminService
{
    private readonly AppDbContext _db;

    /// <summary>Derselbe Cache, aus dem <see cref="AuthUserValidation"/> den Auth-Zustand liest — nach dem
    /// Admin-Entzug verworfen, sonst gilt das alte Token bis zu <see cref="AuthUserValidation.CacheTtl"/> weiter.</summary>
    private readonly IMemoryCache? _authCache;

    public AdminService(AppDbContext db, IMemoryCache? authCache = null)
    {
        _db = db;
        _authCache = authCache;
    }

    public async Task<(List<AdminUserDto> items, int totalCount, int page, int pageSize)> GetUsersAsync(string? search, int page, int pageSize)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);

        var query = _db.AppUsers.AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            if (search.Length > 100) search = search[..100];
            query = query.Where(u => u.Username.Contains(search) || (u.Email != null && u.Email.Contains(search)));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                IsAdmin = u.IsAdmin,
                CreatedAt = u.CreatedAt,
                Groups = u.Groups.Select(ug => ug.Group!.Name).OrderBy(n => n).ToList()
            })
            .ToListAsync();

        return (items, totalCount, page, pageSize);
    }

    /// <summary>Löscht einen User (samt Freundschaften wg. Restrict-FK). DbUpdateException propagiert (→ 409).</summary>
    public async Task DeleteUserAsync(int id, int currentUserId)
    {
        if (id == currentUserId)
            throw new InvalidOperationException("Cannot delete yourself.");

        var user = await _db.AppUsers.FindAsync(id)
            ?? throw new KeyNotFoundException();

        // Freundschaften zuerst entfernen (Restrict delete behavior).
        var friendships = await _db.Friendships
            .Where(f => f.RequesterId == id || f.AddresseeId == id)
            .ToListAsync();
        _db.Friendships.RemoveRange(friendships);

        _db.AppUsers.Remove(user);
        await _db.SaveChangesAsync();   // verbleibende Restrict-FKs → DbUpdateException → Controller mappt auf 409
    }

    /// <summary>Schaltet das Admin-Flag eines anderen Users um. <paramref name="actorIsAdmin"/> muss true
    /// sein: sonst könnte eine delegierte Rolle mit der Permission <c>users.manage</c> (selbst kein Admin)
    /// sich über ein zweites Konto Admin-Rechte verschaffen bzw. echte Admins degradieren.</summary>
    /// <remarks>Ein ENTZUG muss sofort wirken: die Admin-Rolle steht im Token (30, mit „eingeloggt bleiben" 90 Tage)
    /// und erfüllt jedes <c>[HasPermission]</c> und jedes <c>IsAdmin</c> — deshalb wird der Security-Stamp rotiert
    /// (alle Sitzungen des Kontos enden). Dazu folgt die System-Rolle „admin" dem Flag in beide Richtungen: der
    /// <see cref="RoleSeeder"/> legt sie beim Start nur AN, und bliebe sie nach dem Entzug stehen, gäbe der
    /// Live-Resolver dem Konto auch nach neuem Anmelden weiter alle Rechte.</remarks>
    public async Task<AdminUserDto> ToggleAdminAsync(int id, int currentUserId, bool actorIsAdmin = true)
    {
        if (id == currentUserId)
            throw new InvalidOperationException("Cannot toggle your own admin status.");
        if (!actorIsAdmin)
            throw new UnauthorizedAccessException("Only an admin may change the admin flag.");

        var user = await _db.AppUsers.FindAsync(id)
            ?? throw new KeyNotFoundException();

        user.IsAdmin = !user.IsAdmin;
        await MirrorAdminRoleAsync(user);
        if (!user.IsAdmin) user.SecurityStamp = AuthService.NewSecurityStamp();
        await _db.SaveChangesAsync();
        PermissionResolver.InvalidateAll();
        if (!user.IsAdmin && _authCache is not null) AuthUserValidation.Invalidate(_authCache, user.Id);

        var groups = await _db.UserGroups
            .Where(ug => ug.UserId == user.Id)
            .Select(ug => ug.Group!.Name)
            .OrderBy(n => n)
            .ToListAsync();

        return new AdminUserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            IsAdmin = user.IsAdmin,
            CreatedAt = user.CreatedAt,
            Groups = groups
        };
    }

    /// <summary>Hängt die System-Rolle „admin" an bzw. nimmt sie weg, passend zu <see cref="AppUser.IsAdmin"/>
    /// (andere Rollen des Kontos bleiben unberührt). Ohne geseedete Rolle nichts zu tun.</summary>
    private async Task MirrorAdminRoleAsync(AppUser user)
    {
        var adminRoleId = await _db.Roles.Where(r => r.Key == RoleSeeder.AdminKey).Select(r => (int?)r.Id).FirstOrDefaultAsync();
        if (adminRoleId is not int roleId) return;
        var links = await _db.UserRoles.Where(ur => ur.UserId == user.Id && ur.RoleId == roleId).ToListAsync();
        if (user.IsAdmin && links.Count == 0) _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        else if (!user.IsAdmin && links.Count > 0) _db.UserRoles.RemoveRange(links);
    }

    public Task<int> GetPuzzleCountAsync() => _db.Puzzles.CountAsync();

    public async Task ClearPuzzlesAsync()
    {
        // InMemory-Provider unterstützt keine Transaktionen → nur mit relationalem Provider umklammern.
        if (!_db.Database.IsRelational())
        {
            // Relational raeumt der Fremdschluessel (Cascade) die Kinder-Leiter mit ab; InMemory nicht.
            await _db.KidsPuzzles.ExecuteDeleteAsync();
            await _db.PuzzleAttempts.ExecuteDeleteAsync();
            await _db.Puzzles.ExecuteDeleteAsync();
            return;
        }

        // EnableRetryOnFailure aktiviert eine Execution-Strategy, die user-initiierte
        // Transaktionen nur innerhalb von ExecuteAsync erlaubt (sonst wird der Retry-Umfang
        // mehrdeutig) — daher die komplette Transaktion in die Strategy einschließen.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                await _db.PuzzleAttempts.ExecuteDeleteAsync();
                await _db.Puzzles.ExecuteDeleteAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });
    }
}
