using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Accounts;

/// <summary>
/// Die EFFEKTIVE Gruppen-Mitgliedschaft (2026-10-14): direkte Zeilen in <c>UserGroups</c> plus alle Gruppen, die eine
/// dieser Gruppen enthalten (<see cref="GroupInclusion"/>, über beliebig viele Stufen via <see cref="GroupClosure"/>).
/// JEDE Frage „ist das Konto in Gruppe X?" bzw. „wer ist in Gruppe X?" geht hierüber — Rechte, Freigaben, Vereine,
/// Menüpunkte und Trainingsziele sehen damit dieselbe Mitgliedschaft. „Everyone" bleibt Sache der Aufrufer (keine Zeilen).
/// </summary>
public static class GroupMembership
{
    /// <summary>Die Gruppen-Ids des Kontos (direkt + geerbt) als Abfrage. In einer anderen Abfrage als VARIABLE einsetzen
    /// (<c>var mine = GroupIdsOf(db, uid); … mine.Contains(a.GroupId)</c>) — EF setzt sie dann als Unterabfrage ein.
    /// Kann Doppelte enthalten.</summary>
    public static IQueryable<int> GroupIdsOf(AppDbContext db, int userId) =>
        db.UserGroups.Where(ug => ug.UserId == userId).Select(ug => ug.GroupId)
            .Concat(from ug in db.UserGroups
                    join c in db.GroupClosures on ug.GroupId equals c.DescendantId
                    where ug.UserId == userId
                    select c.AncestorId);

    /// <summary>Die Konto-Ids der Gruppe (direkt + über enthaltene Gruppen) als Abfrage. Kann Doppelte enthalten.</summary>
    public static IQueryable<int> UserIdsOf(AppDbContext db, int groupId) =>
        db.UserGroups.Where(ug => ug.GroupId == groupId).Select(ug => ug.UserId)
            .Concat(from c in db.GroupClosures
                    join ug in db.UserGroups on c.DescendantId equals ug.GroupId
                    where c.AncestorId == groupId
                    select ug.UserId);

    /// <summary>Die Gruppen-Ids des Kontos als Menge.</summary>
    public static async Task<HashSet<int>> GroupIdsOfAsync(AppDbContext db, int userId, CancellationToken ct = default) =>
        (await GroupIdsOf(db, userId).ToListAsync(ct)).ToHashSet();

    /// <summary>Wie viele verschiedene Konten in der Gruppe sind (direkt + geerbt).</summary>
    public static async Task<int> CountAsync(AppDbContext db, int groupId, CancellationToken ct = default) =>
        (await UserIdsOf(db, groupId).ToListAsync(ct)).Distinct().Count();

    /// <summary>Würde „<paramref name="groupId"/> enthält <paramref name="includedId"/>" einen Kreis schließen?
    /// (gleich, oder <paramref name="groupId"/> steckt schon in <paramref name="includedId"/>.)</summary>
    public static async Task<bool> WouldCycleAsync(AppDbContext db, int groupId, int includedId, CancellationToken ct = default) =>
        groupId == includedId
        || await db.GroupClosures.AnyAsync(c => c.AncestorId == includedId && c.DescendantId == groupId, ct);

    /// <summary>Baut <see cref="GroupClosure"/> aus <see cref="GroupInclusion"/> neu auf (nach jeder Änderung an
    /// Einschlüssen oder dem Löschen einer Gruppe; speichert selbst). Die Tabellen sind klein — ganz neu rechnen ist
    /// einfacher und sicherer als gezielt nachführen. Ruft <see cref="PermissionResolver.InvalidateAll"/>.</summary>
    public static async Task RebuildClosureAsync(AppDbContext db, CancellationToken ct = default)
    {
        var links = await db.GroupInclusions.AsNoTracking().Select(l => new { l.GroupId, l.IncludedGroupId }).ToListAsync(ct);
        var parentsOf = links.GroupBy(l => l.IncludedGroupId).ToDictionary(g => g.Key, g => g.Select(l => l.GroupId).ToList());
        var wanted = new HashSet<(int A, int D)>();
        foreach (var start in parentsOf.Keys)
        {
            var queue = new Queue<int>(parentsOf[start]);
            var seen = new HashSet<int>();
            while (queue.Count > 0)
            {
                var a = queue.Dequeue();
                if (a == start || !seen.Add(a)) continue;
                wanted.Add((a, start));
                if (parentsOf.TryGetValue(a, out var up)) foreach (var p in up) queue.Enqueue(p);
            }
        }
        var existing = await db.GroupClosures.ToListAsync(ct);
        db.GroupClosures.RemoveRange(existing.Where(c => !wanted.Contains((c.AncestorId, c.DescendantId))));
        var have = existing.Select(c => (c.AncestorId, c.DescendantId)).ToHashSet();
        db.GroupClosures.AddRange(wanted.Where(w => !have.Contains(w)).Select(w => new GroupClosure { AncestorId = w.A, DescendantId = w.D }));
        await db.SaveChangesAsync(ct);
        PermissionResolver.InvalidateAll();
    }
}
