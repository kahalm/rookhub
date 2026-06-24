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

    private static string CacheKey(int userId) => $"user-auth:{userId}";

    /// <summary>Gecachter Auth-Zustand eines Users: ob er existiert+aktiv ist und sein aktueller
    /// Security-Stamp (für die Token-Invalidierung nach Passwort-Reset/-Änderung).</summary>
    private sealed record UserAuthState(bool Active, string? SecurityStamp);

    private static async Task<UserAuthState> GetStateAsync(
        AppDbContext db, IMemoryCache cache, int userId, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey(userId), out UserAuthState? cached) && cached != null)
            return cached;

        // FirstOrDefault liefert null, wenn die Zeile fehlt → nicht aktiv. Sonst zählt DeletedAt.
        var row = await db.AppUsers
            .Where(u => u.Id == userId)
            .Select(u => new { u.DeletedAt, u.SecurityStamp })
            .FirstOrDefaultAsync(ct);
        var state = new UserAuthState(row != null && row.DeletedAt == null, row?.SecurityStamp);

        cache.Set(CacheKey(userId), state, CacheTtl);
        return state;
    }

    /// <summary>True, wenn der User existiert und nicht gelöscht ist (mit kurzem Cache).</summary>
    public static async Task<bool> IsActiveUserAsync(
        AppDbContext db, IMemoryCache cache, int userId, CancellationToken ct = default)
        => (await GetStateAsync(db, cache, userId, ct)).Active;

    /// <summary>
    /// True, wenn das Token noch gültig ist: User aktiv UND der mitgeführte <paramref name="tokenStamp"/>
    /// passt zum aktuellen Security-Stamp. Grandfathering: trägt das Token keinen Stempel
    /// (<c>null</c>, Alt-Token vor diesem Feature) ODER hat der User serverseitig (noch) keinen
    /// Stempel, wird der Stempel-Abgleich übersprungen — verhindert Massen-Logout beim Deploy.
    /// </summary>
    public static async Task<bool> IsTokenValidAsync(
        AppDbContext db, IMemoryCache cache, int userId, string? tokenStamp, CancellationToken ct = default)
    {
        var state = await GetStateAsync(db, cache, userId, ct);
        if (!state.Active) return false;
        if (tokenStamp == null || state.SecurityStamp == null) return true;   // Grandfathering
        return tokenStamp == state.SecurityStamp;
    }
}
