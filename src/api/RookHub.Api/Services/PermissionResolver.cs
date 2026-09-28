using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Die EFFEKTIVEN Rechte eines Kontos, live aus der Datenbank: seine eigenen Rollen (<c>UserRoles</c>) plus die Rollen
/// seiner Gruppen (<c>GroupRoles</c> über <c>UserGroups</c>), dazu das Admin-Flag (0.589.0). Vorher standen die Rechte
/// nur als <c>perm</c>-Claims im Token — eine neue Rolle wirkte erst nach dem nächsten Anmelden (Token bis 30 Tage
/// gültig). Wunsch des Nutzers: „das ist doch scheiße — sollte immer wieder neue Infos holen".
///
/// <para>Kurz gespeichert (<see cref="CacheTtl"/>), damit nicht jede Anfrage vier Abfragen kostet; JEDE Änderung an Rollen,
/// Rollen-Rechten, Gruppenrollen, Mitgliedschaften oder dem Admin-Flag ruft <see cref="InvalidateAll"/> — danach gilt
/// der neue Stand beim nächsten Aufruf. Bewusst ALLES verwerfen statt gezielt: eine Rolle an einer Gruppe betrifft viele
/// Konten, und der Speicher ist in einer Minute ohnehin neu gefüllt.</para>
/// </summary>
public sealed class PermissionResolver(AppDbContext db, IMemoryCache cache)
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>Stand des Speichers — jede Änderung zählt hoch, alte Einträge werden damit nie wieder gelesen.</summary>
    private static long _generation;

    public sealed record Effective(bool IsAdmin, IReadOnlySet<string> Permissions)
    {
        public bool Has(string permission) => IsAdmin || Permissions.Contains(permission);
    }

    /// <summary>Nach jeder Änderung an Rollen, Gruppenrollen, Mitgliedschaften oder dem Admin-Flag aufrufen.</summary>
    public static void InvalidateAll() => Interlocked.Increment(ref _generation);

    public async Task<Effective> GetAsync(int userId, CancellationToken ct = default)
    {
        var key = $"perms:{Interlocked.Read(ref _generation)}:{userId}";
        if (cache.TryGetValue(key, out Effective? hit) && hit != null) return hit;
        var result = await LoadAsync(db, userId, ct);
        cache.Set(key, result, CacheTtl);
        return result;
    }

    /// <summary>Ohne Speicher — auch für die Claims beim Anmelden (<see cref="AuthService"/>). Vier kleine Abfragen statt
    /// einer verschachtelten: <c>Contains</c> über eine Liste wird in jeder Datenbank ein schlichtes <c>IN</c>.</summary>
    public static async Task<Effective> LoadAsync(AppDbContext db, int userId, CancellationToken ct = default)
    {
        var isAdmin = await db.AppUsers.Where(u => u.Id == userId && u.DeletedAt == null).Select(u => u.IsAdmin).FirstOrDefaultAsync(ct);
        var groupIds = await db.UserGroups.Where(ug => ug.UserId == userId).Select(ug => ug.GroupId).ToListAsync(ct);
        var roleIds = await db.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync(ct);
        if (groupIds.Count > 0)
            roleIds.AddRange(await db.GroupRoles.Where(gr => groupIds.Contains(gr.GroupId)).Select(gr => gr.RoleId).ToListAsync(ct));
        var distinct = roleIds.Distinct().ToList();
        var perms = distinct.Count == 0 ? []
            : await db.RolePermissions.Where(rp => distinct.Contains(rp.RoleId)).Select(rp => rp.Permission).Distinct().ToListAsync(ct);
        return new Effective(isAdmin, perms.ToHashSet());
    }
}
