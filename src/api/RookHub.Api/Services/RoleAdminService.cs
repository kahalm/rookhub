using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Verwaltung von Rollen + deren Permissions und der Rollen-Zuweisung an Nutzer (Admin-UI).
/// Wirft <see cref="KeyNotFoundException"/> (→404) / <see cref="InvalidOperationException"/> (→400).
///
/// Leitplanken: Permissions müssen aus <see cref="Permissions.All"/> stammen (Code = Quelle der
/// Wahrheit). Die System-Rolle <c>admin</c> ist der Superuser — ihre Permission-Menge ist NICHT
/// editierbar (bleibt „alle") und sie ist nicht löschbar; <c>member</c> ist ebenfalls nicht löschbar.
/// Die admin-Rollenmitgliedschaft folgt weiterhin dem <see cref="AppUser.IsAdmin"/>-Flag (Sync-Quelle,
/// via <c>toggle-admin</c> + <see cref="RoleSeeder"/>) → die Rollen-Zuweisung hier fasst sie NICHT an.
/// </summary>
public class RoleAdminService
{
    private readonly AppDbContext _db;
    public RoleAdminService(AppDbContext db) => _db = db;

    public async Task<List<RoleDto>> ListAsync()
    {
        var roles = await _db.Roles
            .Include(r => r.Permissions)
            .OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)
            .ToListAsync();
        var counts = (await _db.UserRoles
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToListAsync())
            .ToDictionary(x => x.RoleId, x => x.Count);
        return roles.Select(r => new RoleDto
        {
            Id = r.Id,
            Key = r.Key,
            Name = r.Name,
            IsSystem = r.IsSystem,
            Permissions = r.Permissions.Select(p => p.Permission).OrderBy(p => p).ToList(),
            MemberCount = counts.GetValueOrDefault(r.Id),
        }).ToList();
    }

    /// <summary>Alle im Code definierten Permission-Schlüssel (für die Checkbox-Auswahl in der UI).</summary>
    public IReadOnlyList<string> AllPermissions() => Permissions.All;

    public async Task<RoleDto> CreateAsync(CreateRoleDto dto)
    {
        var key = dto.Key.Trim().ToLowerInvariant();
        if (await _db.Roles.AnyAsync(r => r.Key == key))
            throw new InvalidOperationException("Eine Rolle mit diesem Key existiert bereits.");

        var perms = ValidatePermissions(dto.Permissions);
        var role = new Role { Key = key, Name = dto.Name.Trim(), IsSystem = false };
        role.Permissions = perms.Select(p => new RolePermission { Permission = p }).ToList();
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();
        return (await ListAsync()).First(r => r.Id == role.Id);
    }

    public async Task<RoleDto> UpdateAsync(int id, UpdateRoleDto dto)
    {
        var role = await _db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException("Rolle nicht gefunden.");

        role.Name = dto.Name.Trim();

        // admin-Rolle bleibt Superuser (alle Permissions) — ihre Menge nicht über die UI verändern.
        if (role.Key != RoleSeeder.AdminKey)
        {
            var perms = ValidatePermissions(dto.Permissions);
            _db.RolePermissions.RemoveRange(role.Permissions);
            role.Permissions = perms.Select(p => new RolePermission { RoleId = role.Id, Permission = p }).ToList();
        }
        await _db.SaveChangesAsync();
        return (await ListAsync()).First(r => r.Id == role.Id);
    }

    public async Task DeleteAsync(int id)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException("Rolle nicht gefunden.");
        if (role.IsSystem)
            throw new InvalidOperationException("System-Rollen können nicht gelöscht werden.");
        _db.Roles.Remove(role);   // UserRoles + RolePermissions cascaden
        await _db.SaveChangesAsync();
    }

    public async Task<UserRolesDto> GetUserRolesAsync(int userId)
    {
        if (!await _db.AppUsers.AnyAsync(u => u.Id == userId))
            throw new KeyNotFoundException("User nicht gefunden.");
        var roleIds = await _db.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync();
        return new UserRolesDto { UserId = userId, RoleIds = roleIds };
    }

    /// <summary>Setzt die Rollen des Users auf genau <paramref name="dto"/> — die admin-Rolle bleibt
    /// dabei UNANGETASTET (sie folgt dem IsAdmin-Flag), auch wenn sie in der Liste fehlt/steht.</summary>
    public async Task SetUserRolesAsync(int userId, SetUserRolesDto dto)
    {
        if (!await _db.AppUsers.AnyAsync(u => u.Id == userId))
            throw new KeyNotFoundException("User nicht gefunden.");

        var adminRoleId = await _db.Roles.Where(r => r.Key == RoleSeeder.AdminKey).Select(r => r.Id).FirstOrDefaultAsync();
        var target = new HashSet<int>(dto.RoleIds);
        target.Remove(adminRoleId);   // admin wird nicht über diese UI verwaltet

        // Nur existierende, NICHT-admin-Rollen zulassen.
        var valid = await _db.Roles.Where(r => target.Contains(r.Id) && r.Key != RoleSeeder.AdminKey)
            .Select(r => r.Id).ToListAsync();
        var validSet = new HashSet<int>(valid);

        var current = await _db.UserRoles.Where(ur => ur.UserId == userId).ToListAsync();
        var currentNonAdmin = current.Where(ur => ur.RoleId != adminRoleId).ToList();

        // Entfernen, was nicht mehr gewünscht ist; hinzufügen, was fehlt.
        var toRemove = currentNonAdmin.Where(ur => !validSet.Contains(ur.RoleId)).ToList();
        if (toRemove.Count > 0) _db.UserRoles.RemoveRange(toRemove);
        var haveIds = currentNonAdmin.Select(ur => ur.RoleId).ToHashSet();
        foreach (var rid in validSet.Where(rid => !haveIds.Contains(rid)))
            _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = rid });
        await _db.SaveChangesAsync();
    }

    private static List<string> ValidatePermissions(IEnumerable<string> requested)
    {
        var known = new HashSet<string>(Permissions.All);
        var result = new List<string>();
        foreach (var p in requested.Select(x => x?.Trim() ?? "").Where(x => x.Length > 0).Distinct())
        {
            if (!known.Contains(p))
                throw new InvalidOperationException($"Unbekannte Permission: {p}");
            result.Add(p);
        }
        return result;
    }
}
