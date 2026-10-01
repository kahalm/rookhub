using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Admin-Backend-Operationen für Benutzer- und Puzzle-Verwaltung (vormals inline im AdminController).
/// Self-Delete/Self-Toggle → <see cref="DomainValidationException"/> mit Fehlercode (400), nicht gefunden →
/// <see cref="KeyNotFoundException"/> (404).
/// </summary>
public class AdminService
{
    private readonly AppDbContext _db;

    /// <summary>Träger des gemeinsamen Löschkerns (<see cref="ProfileService.EraseUserAsync"/>).</summary>
    private readonly ProfileService _profile;

    /// <summary>Derselbe Cache, aus dem <see cref="AuthUserValidation"/> den Auth-Zustand liest — nach dem
    /// Admin-Entzug verworfen, sonst gilt das alte Token bis zu <see cref="AuthUserValidation.CacheTtl"/> weiter.</summary>
    private readonly IMemoryCache? _authCache;

    private readonly ILogger<AdminService>? _logger;

    public AdminService(AppDbContext db, ProfileService profile, IMemoryCache? authCache = null,
        ILogger<AdminService>? logger = null)
    {
        _db = db;
        _profile = profile;
        _authCache = authCache;
        _logger = logger;
    }

    public async Task<(List<AdminUserDto> items, int totalCount, int page, int pageSize)> GetUsersAsync(string? search, int page, int pageSize)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);

        // Gelöschte Konten bleiben als anonymisierte Zeile stehen (deleted_<id>) — in der Verwaltung haben sie
        // nichts verloren; sonst stünde ein eben gelöschter Nutzer nach dem Neuladen unter neuem Namen wieder da.
        var query = _db.AppUsers.Where(u => u.DeletedAt == null);

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
                Groups = u.Groups.Select(ug => ug.Group!.Name).OrderBy(n => n).ToList(),
                LockedUntil = u.LockedUntil,
            })
            .ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var item in items) item.LockedUntil = ActiveLock(item.LockedUntil, now);

        return (items, totalCount, page, pageSize);
    }

    /// <summary>Löscht einen User über DENSELBEN Kern wie die Selbstlöschung (<see cref="ProfileService.EraseUserAsync"/>):
    /// Identität anonymisiert, persönliche Inhalte entfernt, anonyme Statistik bleibt. Vorher: hartes
    /// <c>AppUsers.Remove</c> — Spalten ohne FK blieben als Waisen stehen (eine Verteiler-Waise der Kalk-Serie
    /// ließ jede weitere Ankündigung am FK der Benachrichtigungen scheitern), Restrict-FKs endeten in 409.
    /// Ein Admin-Konto löscht nur ein Admin (<paramref name="actorIsAdmin"/>) — dieselbe Grenze wie bei Toggle, Sperre
    /// und Impersonation; sonst löschte eine delegierte Rolle mit <c>users.manage</c> die Admins weg.</summary>
    public async Task DeleteUserAsync(int id, int currentUserId, bool actorIsAdmin = true)
    {
        if (id == currentUserId)
            throw new DomainValidationException("Cannot delete yourself.") { Code = ApiErrorCodes.AdminSelfDelete };
        if (!actorIsAdmin && await _db.AppUsers.AnyAsync(u => u.Id == id && u.IsAdmin && u.DeletedAt == null))
            throw new UnauthorizedAccessException("Only an admin may delete an admin account.");

        await _profile.EraseUserAsync(id);   // KeyNotFoundException → 404
        // Das Token des Gelöschten fällt sofort, nicht erst nach Ablauf des Auth-Caches.
        if (_authCache is not null) AuthUserValidation.Invalidate(_authCache, id);
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
        var user = await LoadForAdminChangeAsync(id, currentUserId, actorIsAdmin);
        return await ApplyAdminAsync(user, !user.IsAdmin);
    }

    /// <summary>Setzt das Admin-Flag auf den SOLL-Wert (Codereview F5-013) — idempotent: steht es schon so, ändert sich
    /// nichts (kein Stamp-Wechsel, keine beendeten Sitzungen). Das Umschalten (<see cref="ToggleAdminAsync"/>) entzog bei
    /// einem Doppelklick oder einer veralteten Liste zweier Admins das eben vergebene Recht wieder. Dieselben Grenzen
    /// wie dort.</summary>
    public async Task<AdminUserDto> SetAdminAsync(int id, int currentUserId, bool isAdmin, bool actorIsAdmin = true)
    {
        var user = await LoadForAdminChangeAsync(id, currentUserId, actorIsAdmin);
        return user.IsAdmin == isAdmin ? await UserDtoAsync(user) : await ApplyAdminAsync(user, isAdmin);
    }

    private async Task<AppUser> LoadForAdminChangeAsync(int id, int currentUserId, bool actorIsAdmin)
    {
        if (id == currentUserId)
            throw new DomainValidationException("Cannot toggle your own admin status.") { Code = ApiErrorCodes.AdminSelfChange };
        if (!actorIsAdmin)
            throw new UnauthorizedAccessException("Only an admin may change the admin flag.");

        return await _db.AppUsers.FindAsync(id)
            ?? throw new KeyNotFoundException();
    }

    private async Task<AdminUserDto> ApplyAdminAsync(AppUser user, bool isAdmin)
    {
        user.IsAdmin = isAdmin;
        await MirrorAdminRoleAsync(user);
        if (!user.IsAdmin) user.SecurityStamp = AuthService.NewSecurityStamp();
        await _db.SaveChangesAsync();
        PermissionResolver.InvalidateAll();
        if (!user.IsAdmin && _authCache is not null) AuthUserValidation.Invalidate(_authCache, user.Id);

        return await UserDtoAsync(user);
    }

    /// <summary>Sperrt einen anderen Nutzer bis <paramref name="until"/> (UTC; <c>null</c> = unbefristet) — das umkehrbare
    /// Mittel gegen Missbrauch statt der Löschung (F5-011). Login, JWT- und API-Token-Prüfung weisen das Konto ab, solange
    /// die Sperre läuft; der Security-Stamp rotiert, damit laufende Sitzungen SOFORT enden und auch nach Ablauf der Sperre
    /// nicht zurückkommen. Konto und Daten bleiben unberührt. Ein Admin-Konto sperrt nur ein Admin (sonst sperrte eine
    /// delegierte Rolle mit <c>users.manage</c> die Admins aus).</summary>
    public async Task<AdminUserDto> LockUserAsync(int id, int currentUserId, DateTime? until, bool actorIsAdmin = true)
    {
        if (id == currentUserId)
            throw new InvalidOperationException("Cannot lock yourself.");

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null)
            ?? throw new KeyNotFoundException();
        if (user.IsAdmin && !actorIsAdmin)
            throw new UnauthorizedAccessException("Only an admin may lock an admin account.");

        var end = until is { } t ? ToUtc(t) : AppUser.LockedIndefinitely;
        if (end <= DateTime.UtcNow)
            throw new InvalidOperationException("The lock must end in the future.");
        if (end > AppUser.LockedIndefinitely) end = AppUser.LockedIndefinitely;

        user.LockedUntil = end;
        user.SecurityStamp = AuthService.NewSecurityStamp();
        await _db.SaveChangesAsync();
        if (_authCache is not null) AuthUserValidation.Invalidate(_authCache, user.Id);
        _logger?.LogInformation("AdminLock: User {UserId} gesperrt bis {LockedUntil} von {ActorId}", user.Id, end, currentUserId);

        return await UserDtoAsync(user);
    }

    /// <summary>Hebt die Sperre eines Nutzers auf (idempotent). Die bei der Sperre entwerteten Sitzungen bleiben entwertet —
    /// der Nutzer meldet sich neu an.</summary>
    public async Task<AdminUserDto> UnlockUserAsync(int id, int currentUserId, bool actorIsAdmin = true)
    {
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null)
            ?? throw new KeyNotFoundException();
        if (user.IsAdmin && !actorIsAdmin)
            throw new UnauthorizedAccessException("Only an admin may unlock an admin account.");

        if (user.LockedUntil != null)
        {
            user.LockedUntil = null;
            await _db.SaveChangesAsync();
            if (_authCache is not null) AuthUserValidation.Invalidate(_authCache, user.Id);
            _logger?.LogInformation("AdminLock: User {UserId} entsperrt von {ActorId}", user.Id, currentUserId);
        }

        return await UserDtoAsync(user);
    }

    private async Task<AdminUserDto> UserDtoAsync(AppUser user)
    {
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
            Groups = groups,
            LockedUntil = ActiveLock(user.LockedUntil, DateTime.UtcNow),
        };
    }

    /// <summary>Sperrende für die Verwaltung: nur eine LAUFENDE Sperre, als UTC gekennzeichnet (die Spalte liest sich
    /// ohne Kind zurück).</summary>
    private static DateTime? ActiveLock(DateTime? lockedUntil, DateTime nowUtc)
        => lockedUntil is { } until && until > nowUtc ? DateTime.SpecifyKind(until, DateTimeKind.Utc) : null;

    /// <summary>Zeitpunkt aus dem Request als UTC: mit Offset gesendete Werte kommen als Local an, ohne Angabe gilt UTC.</summary>
    private static DateTime ToUtc(DateTime t) => t.Kind switch
    {
        DateTimeKind.Utc => t,
        DateTimeKind.Local => t.ToUniversalTime(),
        _ => DateTime.SpecifyKind(t, DateTimeKind.Utc),
    };

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
}
