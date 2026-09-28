using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Gemerkte Namens-Zuordnungen der Vereins-Datenbank (<see cref="LeagueNameAlias"/>): wer beim Import einen Spieler
/// korrigiert, legt damit fest, zu wem dieser Name gehört — die nächste Übersicht ordnet ihn von selbst so zu, bei allen.
/// Geschlüsselt über den Namen im PGN, klein, ohne Akzente und Titel (<see cref="KeyOf"/>).
/// </summary>
public sealed class LeagueNameAliases
{
    private readonly AppDbContext _db;
    public LeagueNameAliases(AppDbContext db) => _db = db;

    public sealed record Entry(string? Fide, string Name);

    public static string KeyOf(string? raw)
    {
        var k = LeagueRosterIndex.Fold(LeagueNames.NameKey(LeagueNames.StripTitles(raw)), false);
        return k.Length > 120 ? k[..120] : k;
    }

    /// <summary>Die Zuordnungen für diese Namen (eine Abfrage je 500).</summary>
    public async Task<IReadOnlyDictionary<string, Entry>> LoadAsync(IEnumerable<string?> names, CancellationToken ct)
    {
        var keys = names.Select(KeyOf).Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var result = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var chunk in keys.Chunk(500))
            foreach (var a in await _db.LeagueNameAliases.AsNoTracking().Where(a => chunk.Contains(a.NameKey)).ToListAsync(ct))
                result[a.NameKey] = new Entry(a.Fide, a.Name);
        return result;
    }

    /// <summary>Zuordnungen anlegen oder überschreiben (die jüngste Korrektur gewinnt). Speichert selbst; liefert, wie
    /// viele sich geändert haben.</summary>
    public async Task<int> SaveAsync(IEnumerable<(string Raw, Entry Target)> items, DateTime now, CancellationToken ct)
    {
        var byKey = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var (raw, target) in items)
            if (KeyOf(raw) is { Length: > 0 } k && target.Name.Length > 0) byKey[k] = target;
        if (byKey.Count == 0) return 0;
        var keys = byKey.Keys.ToList();
        var existing = new Dictionary<string, LeagueNameAlias>(StringComparer.Ordinal);
        foreach (var chunk in keys.Chunk(500))
            foreach (var a in await _db.LeagueNameAliases.Where(a => chunk.Contains(a.NameKey)).ToListAsync(ct))
                existing[a.NameKey] = a;
        var changed = 0;
        foreach (var (key, t) in byKey)
        {
            var name = t.Name.Length > 120 ? t.Name[..120] : t.Name;
            var fide = t.Fide is { Length: > 0 and <= 16 } f ? f : null;
            if (existing.TryGetValue(key, out var a))
            {
                if (a.Fide == fide && a.Name == name) continue;
                (a.Fide, a.Name, a.UpdatedAt) = (fide, name, now);
            }
            else _db.LeagueNameAliases.Add(new LeagueNameAlias { NameKey = key, Fide = fide, Name = name, UpdatedAt = now });
            changed++;
        }
        if (changed > 0) await _db.SaveChangesAsync(ct);
        return changed;
    }
}
