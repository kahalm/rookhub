using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Seedet das RBAC-Grundgerüst idempotent bei jedem Start:
/// - System-Rollen „admin" (Superuser, trägt ALLE <see cref="Permissions"/>) und „member".
/// - Hält die Permission-Menge der „admin"-Rolle mit dem Code synchron (neue Permission-Konstante →
///   wird beim nächsten Start automatisch der admin-Rolle hinzugefügt).
/// - Spiegelt das bisherige <see cref="AppUser.IsAdmin"/>-Flag in die „admin"-Rollenmitgliedschaft
///   (Phase 1: IsAdmin bleibt die Quelle der Wahrheit; die Rolle bildet es nur ab). So ändert sich
///   das Verhalten nicht, während die Rollen-Infrastruktur bereits vollständig befüllt ist.
/// </summary>
public static class RoleSeeder
{
    public const string AdminKey = "admin";
    public const string MemberKey = "member";

    public static async Task SeedAsync(AppDbContext db)
    {
        var admin = await EnsureRoleAsync(db, AdminKey, "Administrator", isSystem: true);
        await EnsureRoleAsync(db, MemberKey, "Mitglied", isSystem: true);

        // admin-Rolle trägt alle bekannten Permissions (fehlende ergänzen — additiv, nie entfernen,
        // damit manuell vergebene Extra-Permissions nicht verloren gehen).
        var existing = await db.RolePermissions
            .Where(rp => rp.RoleId == admin.Id)
            .Select(rp => rp.Permission)
            .ToListAsync();
        var missing = Permissions.All.Except(existing).ToList();
        if (missing.Count > 0)
        {
            foreach (var perm in missing)
                db.RolePermissions.Add(new RolePermission { RoleId = admin.Id, Permission = perm });
            await db.SaveChangesAsync();
        }

        // Jeder IsAdmin-User bekommt (falls noch nicht vorhanden) die admin-Rollenmitgliedschaft.
        var adminUserIds = await db.AppUsers.Where(u => u.IsAdmin).Select(u => u.Id).ToListAsync();
        if (adminUserIds.Count > 0)
        {
            var alreadyLinked = await db.UserRoles
                .Where(ur => ur.RoleId == admin.Id && adminUserIds.Contains(ur.UserId))
                .Select(ur => ur.UserId)
                .ToListAsync();
            var toLink = adminUserIds.Except(alreadyLinked).ToList();
            if (toLink.Count > 0)
            {
                foreach (var uid in toLink)
                    db.UserRoles.Add(new UserRole { UserId = uid, RoleId = admin.Id });
                await db.SaveChangesAsync();
            }
        }
    }

    private static async Task<Role> EnsureRoleAsync(AppDbContext db, string key, string name, bool isSystem)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Key == key);
        if (role != null) return role;
        role = new Role { Key = key, Name = name, IsSystem = isSystem };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role;
    }
}
