using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services.League;

/// <summary>
/// Ligaspieler OHNE FIDE-ID über ihren Schlüssel <see cref="LeagueNames.AccountKey"/> („n-" + 14 Hex-Zeichen aus dem
/// Namensschlüssel) finden (0.730.0). Online-Konten, Karte und Partien hängen an diesem Schlüssel wie sonst an der FIDE-ID;
/// zurückrechnen lässt er sich nicht — gesucht wird unter den Namensschlüsseln der Meldelisten ohne FIDE-ID.
/// </summary>
public static class LeagueNoFidePlayers
{
    /// <summary>Der Namensschlüssel hinter <paramref name="key"/>, <c>null</c> = kein solcher Ligaspieler (oder kein n-Schlüssel).</summary>
    public static async Task<string?> NameKeyAsync(AppDbContext db, string? key, CancellationToken ct)
    {
        if (!LeagueNames.IsNoFideKey(key)) return null;
        var keys = await db.LeaguePlayers.AsNoTracking().Where(p => p.FideId == null || p.FideId == "")
            .Select(p => p.NameKey).Distinct().ToListAsync(ct);
        return keys.FirstOrDefault(k => LeagueNames.AccountKey(null, k) == key);
    }

    /// <summary>Der Ligaspieler hinter <paramref name="key"/> aus der jüngsten Meldeliste, <c>null</c> = keiner.</summary>
    public static async Task<LeagueAccountFinder.Player?> PlayerAsync(AppDbContext db, string key, CancellationToken ct)
    {
        if (await NameKeyAsync(db, key, ct) is not { } nameKey) return null;
        var row = await (from p in db.LeaguePlayers.AsNoTracking()
                         join t in db.LeagueTournaments.AsNoTracking() on p.Tnr equals t.Tnr
                         where p.NameKey == nameKey && (p.FideId == null || p.FideId == "")
                         orderby t.Season descending, p.Id descending
                         select new { p.Name, p.Fed, p.EloI, p.EloN, p.Team, t.Source }).FirstOrDefaultAsync(ct);
        return row is null ? null
            : new LeagueAccountFinder.Player(key, row.Name, row.Fed, row.EloI is > 0 ? row.EloI : row.EloN, row.Team,
                Region: LeagueRegions.Of(row.Source));
    }
}
