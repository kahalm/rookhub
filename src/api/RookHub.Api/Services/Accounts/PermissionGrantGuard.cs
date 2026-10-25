using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;

namespace RookHub.Api.Services.Accounts;

/// <summary>
/// Regel „nur vergeben, was man selbst hat“ (Codereview N9-004, entschieden 25.10.2026): wer Rollen verwaltet
/// (<c>roles.manage</c>) oder Gruppen (<c>groups.manage</c>), darf keine Rechte weitergeben, die er selbst nicht hat —
/// weder über eine Rolle (anlegen, Rechte ergänzen, an Nutzer oder Gruppe hängen) noch über eine Gruppen-Mitgliedschaft
/// (Konto oder enthaltene Gruppe in eine Gruppe mit Rollen aufnehmen). Admins sind ausgenommen. Vorher konnte ein
/// Trainer mit <c>groups.manage</c> sich selbst in eine Gruppe mit <c>users.manage</c> eintragen, und ein
/// <c>roles.manage</c>-Inhaber eine Rolle mit allen Rechten bauen und sich geben.
/// <para>Nur das VERGEBEN wird geprüft — Wegnehmen (Rolle entfernen, Mitglied austragen, Recht streichen) bleibt frei.
/// Die eigenen Rechte kommen frisch aus der Datenbank (<see cref="PermissionResolver.LoadAsync"/>), nicht aus dem
/// Token oder dem 60-s-Speicher.</para>
/// </summary>
public static class PermissionGrantGuard
{
    /// <summary>Wirft 403 (<see cref="ApiErrorCodes.PermissionNotHeld"/>), wenn <paramref name="actorId"/> nicht Admin ist und
    /// eines der <paramref name="permissions"/> selbst nicht hat. <paramref name="actorId"/> <c>null</c> = interner Aufruf ohne
    /// angemeldeten Nutzer (Seeder, direkt instanziierte Dienste in Tests) — über die HTTP-Endpunkte kommt immer ein Nutzer
    /// (<c>[HasPermission]</c> lässt niemanden anonym durch).</summary>
    public static async Task EnsureCanGrantAsync(AppDbContext db, int? actorId, IEnumerable<string> permissions, CancellationToken ct = default)
    {
        if (actorId is not int actor) return;
        var wanted = permissions.Distinct().ToList();
        if (wanted.Count == 0) return;
        var own = await PermissionResolver.LoadAsync(db, actor, ct);
        if (own.IsAdmin) return;
        var missing = wanted.Where(p => !own.Permissions.Contains(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            throw new ForbiddenException($"Du kannst nur Rechte vergeben, die du selbst hast. Es fehlt: {string.Join(", ", missing)}.")
            {
                Code = ApiErrorCodes.PermissionNotHeld,
            };
    }

    /// <summary>Alle Rechte der Rollen <paramref name="roleIds"/>.</summary>
    public static async Task<List<string>> PermissionsOfRolesAsync(AppDbContext db, IReadOnlyCollection<int> roleIds, CancellationToken ct = default)
        => roleIds.Count == 0 ? []
            : await db.RolePermissions.Where(rp => roleIds.Contains(rp.RoleId)).Select(rp => rp.Permission).Distinct().ToListAsync(ct);

    /// <summary>Die Rechte, die ein Mitglied von <paramref name="groupId"/> dadurch bekommt: Rollen der Gruppe selbst und aller
    /// Gruppen, die sie (auch mittelbar) enthalten — wer in einer enthaltenen Gruppe steht, zählt dort mit.</summary>
    public static async Task<List<string>> PermissionsGrantedByGroupAsync(AppDbContext db, int groupId, CancellationToken ct = default)
    {
        var groupIds = await db.GroupClosures.Where(c => c.DescendantId == groupId).Select(c => c.AncestorId).ToListAsync(ct);
        groupIds.Add(groupId);
        var roleIds = await db.GroupRoles.Where(gr => groupIds.Contains(gr.GroupId)).Select(gr => gr.RoleId).Distinct().ToListAsync(ct);
        return await PermissionsOfRolesAsync(db, roleIds, ct);
    }
}
