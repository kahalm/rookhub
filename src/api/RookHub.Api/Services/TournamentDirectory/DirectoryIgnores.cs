using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// „Ausblenden“ gilt je TURNIER, nicht je Gruppe (G1-001): die Liste fasst die Gruppen A/B/C eines Turniers über
/// <see cref="TournamentDirectoryEntry.GroupKey"/> zu einer Karte zusammen, und die Karte trägt nur die Nummer ihres
/// Hauptvertreters. Gespeichert wird weiter genau diese eine Nummer; ausgeblendet ist aber jeder Eintrag, dessen
/// Nummer selbst ODER die Nummer eines Eintrags mit demselben Gruppenschlüssel ausgeblendet ist. Sonst stand das
/// Turnier nach dem Neuladen mit Gruppe B als Hauptvertreter wieder da, und die Nachtmeldung meldete B und C weiter.
/// </summary>
public static class DirectoryIgnores
{
    /// <summary>Nur Einträge, die <paramref name="userId"/> weder selbst noch über eine andere Gruppe desselben Turniers
    /// ausgeblendet hat (EXISTS-Unterabfragen, nichts wird materialisiert). Zwei getrennte NOT EXISTS ohne OR (GA-06): die
    /// frühere Fassung mit einem OR in EINER Unterabfrage lief auf MariaDB je Kandidatenzeile alle Ausblendungen des
    /// Nutzers ab. So greift das erste NOT EXISTS den eindeutigen Index (UserId, PublicId), das zweite den GroupKey-Index
    /// und darin wieder (UserId, PublicId). <c>o.GroupKey != null</c> steht für die SQL-Semantik: ohne Gruppe gibt es keine
    /// Geschwister (in C# wäre <c>null == null</c> wahr).</summary>
    public static IQueryable<TournamentDirectoryEntry> WhereNotIgnored(
        this IQueryable<TournamentDirectoryEntry> source, AppDbContext db, int userId) =>
        source.Where(e => !db.TournamentDirectoryIgnores.Any(i => i.UserId == userId && i.PublicId == e.PublicId)
            && !db.TournamentDirectoryEntries.Any(o => o.GroupKey != null && o.GroupKey == e.GroupKey
                && db.TournamentDirectoryIgnores.Any(i => i.UserId == userId && i.PublicId == o.PublicId)));

    /// <summary>Welche der <paramref name="ids"/> hat der Nutzer ausgeblendet (direkt oder über eine Gruppe desselben
    /// Turniers)?</summary>
    public static async Task<HashSet<string>> IgnoredAmongAsync(
        AppDbContext db, int userId, IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var groupOf = (await db.TournamentDirectoryEntries.AsNoTracking()
                .Where(e => ids.Contains(e.PublicId) && e.GroupKey != null)
                .Select(e => new { e.PublicId, e.GroupKey })
                .ToListAsync(ct))
            .ToDictionary(e => e.PublicId, e => e.GroupKey!, StringComparer.Ordinal);
        var hidden = await HiddenAsync(db, [userId], ids, groupOf.Values.Distinct().ToList(), ct);
        if (!hidden.TryGetValue(userId, out var h)) return [];
        return ids.Where(id => h.PublicIds.Contains(id) || (groupOf.TryGetValue(id, out var g) && h.GroupKeys.Contains(g)))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Ausgeblendetes je Nutzer, eingeschränkt auf die Kandidaten (Nummern bzw. Gruppenschlüssel): die
    /// ausgeblendeten Nummern und die Gruppenschlüssel, zu denen eine ausgeblendete Nummer gehört.</summary>
    public static async Task<Dictionary<int, (HashSet<string> PublicIds, HashSet<string> GroupKeys)>> HiddenAsync(
        AppDbContext db, IReadOnlyCollection<int> userIds, IReadOnlyCollection<string> publicIds,
        IReadOnlyCollection<string> groupKeys, CancellationToken ct)
    {
        var rows = await db.TournamentDirectoryIgnores.AsNoTracking()
            .Where(i => userIds.Contains(i.UserId)
                && (publicIds.Contains(i.PublicId)
                    || db.TournamentDirectoryEntries.Any(o => o.PublicId == i.PublicId && o.GroupKey != null && groupKeys.Contains(o.GroupKey))))
            .Select(i => new
            {
                i.UserId,
                i.PublicId,
                GroupKey = db.TournamentDirectoryEntries.Where(o => o.PublicId == i.PublicId).Select(o => o.GroupKey).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => (
            g.Select(r => r.PublicId).ToHashSet(StringComparer.Ordinal),
            g.Where(r => r.GroupKey != null).Select(r => r.GroupKey!).ToHashSet(StringComparer.Ordinal)));
    }

    /// <summary>Alle Nummern des Turniers zu <paramref name="publicId"/> (sie selbst plus alle Gruppen mit demselben
    /// Gruppenschlüssel) — „wieder zeigen“ hebt die Sperre für das ganze Turnier auf.</summary>
    public static async Task<List<string>> TournamentPublicIdsAsync(AppDbContext db, string publicId, CancellationToken ct)
    {
        var key = await db.TournamentDirectoryEntries.AsNoTracking()
            .Where(e => e.PublicId == publicId).Select(e => e.GroupKey).FirstOrDefaultAsync(ct);
        if (key == null) return [publicId];
        var ids = await db.TournamentDirectoryEntries.AsNoTracking()
            .Where(e => e.GroupKey == key).Select(e => e.PublicId).ToListAsync(ct);
        return ids.Append(publicId).Distinct(StringComparer.Ordinal).ToList();
    }
}
