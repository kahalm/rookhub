using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services.Accounts;

/// <summary>
/// Mitgliederzahl je Gruppe MIT den geerbten aus enthaltenen Gruppen (jedes Konto einmal); „Everyone" = alle Konten.
/// Eine Stelle für die volle Gruppenliste (<c>GET /api/admin/groups</c>) und die Auswahl (<c>…/options</c>, F5-005-Nachlauf)
/// — die Tabellen sind klein, gerechnet wird im Speicher.
/// </summary>
public static class GroupMemberCounts
{
    public static async Task<Dictionary<int, int>> ComputeAsync(AppDbContext db, CancellationToken ct = default)
    {
        var groups = await db.Groups.AsNoTracking().Select(g => new { g.Id, g.IsEveryone }).ToListAsync(ct);
        var rows = await db.UserGroups.AsNoTracking().Select(ug => new { ug.GroupId, ug.UserId }).ToListAsync(ct);
        var closure = await db.GroupClosures.AsNoTracking().ToListAsync(ct);
        var usersOf = rows.GroupBy(r => r.GroupId).ToDictionary(g => g.Key, g => g.Select(r => r.UserId).ToList());
        int? totalUsers = groups.Any(g => g.IsEveryone) ? await db.AppUsers.CountAsync(ct) : null;

        var counts = new Dictionary<int, int>();
        foreach (var g in groups)
        {
            if (g.IsEveryone) { counts[g.Id] = totalUsers!.Value; continue; }
            var ids = new HashSet<int>(usersOf.GetValueOrDefault(g.Id) ?? []);
            foreach (var c in closure.Where(c => c.AncestorId == g.Id))
                ids.UnionWith(usersOf.GetValueOrDefault(c.DescendantId) ?? []);
            counts[g.Id] = ids.Count;
        }
        return counts;
    }
}
