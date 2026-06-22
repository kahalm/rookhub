using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Prüft, ob die hinter einem (stateless) JWT stehende Identität noch aktiv ist: der User muss
/// existieren UND darf nicht gelöscht/anonymisiert sein (<see cref="Models.AppUser.DeletedAt"/>).
/// Wird im <c>OnTokenValidated</c>-Event des JWT-Handlers aufgerufen, damit ein gelöschtes Konto
/// sein bereits ausgegebenes (bis zu 30 Tage gültiges) Token nicht weiterverwenden kann.
/// Ergebnis wird kurz gecacht, um den Auth-Hot-Path (Polling) nicht je Request zu belasten.
/// </summary>
public static class AuthUserValidation
{
    /// <summary>Cache-Dauer des Aktiv-Status. Kurz genug, dass eine Löschung schnell greift.</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private static string CacheKey(int userId) => $"user-active:{userId}";

    /// <summary>True, wenn der User existiert und nicht gelöscht ist (mit kurzem Cache).</summary>
    public static async Task<bool> IsActiveUserAsync(
        AppDbContext db, IMemoryCache cache, int userId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey(userId), out bool cached))
            return cached;

        // FirstOrDefault liefert null, wenn die Zeile fehlt → nicht aktiv. Sonst zählt DeletedAt.
        var row = await db.AppUsers
            .Where(u => u.Id == userId)
            .Select(u => new { u.DeletedAt })
            .FirstOrDefaultAsync(ct);
        var active = row != null && row.DeletedAt == null;

        cache.Set(CacheKey(userId), active, CacheTtl);
        return active;
    }
}
