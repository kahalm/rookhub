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

    /// <summary>Den gecachten Auth-Zustand EINES Users verwerfen. Nötig, sobald der Security-Stamp
    /// rotiert (Passwortänderung/-Reset): ohne das blieben fremde Sitzungen bis zu <see cref="CacheTtl"/>
    /// weiter gültig, obwohl der Widerruf schon in der Datenbank steht.</summary>
    public static void Invalidate(IMemoryCache cache, int userId) => cache.Remove(CacheKey(userId));

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
        => await CheckTokenAsync(db, cache, userId, tokenStamp, ct) == TokenRejection.None;

    /// <summary>Wie <see cref="IsTokenValidAsync"/>, sagt aber WARUM — der JWT-Handler loggt den
    /// Grund (<see cref="JwtTokenGate"/>), sonst ist ein 401 in den Logs nicht von einem falschen
    /// Passwort zu unterscheiden.</summary>
    public static async Task<TokenRejection> CheckTokenAsync(
        AppDbContext db, IMemoryCache cache, int userId, string? tokenStamp, CancellationToken ct = default)
    {
        var state = await GetStateAsync(db, cache, userId, ct);
        if (!state.Active) return TokenRejection.InactiveUser;
        if (tokenStamp == null || state.SecurityStamp == null) return TokenRejection.None;   // Grandfathering
        return tokenStamp == state.SecurityStamp ? TokenRejection.None : TokenRejection.StampMismatch;
    }
}

/// <summary>Ergebnis der Kontostand-Prüfung eines formal gültigen Tokens.</summary>
public enum TokenRejection
{
    /// <summary>Token bleibt gültig.</summary>
    None = 0,
    /// <summary>Konto fehlt oder ist gelöscht/anonymisiert.</summary>
    InactiveUser,
    /// <summary>Security-Stamp passt nicht mehr (Passwort geändert/zurückgesetzt).</summary>
    StampMismatch,
}
