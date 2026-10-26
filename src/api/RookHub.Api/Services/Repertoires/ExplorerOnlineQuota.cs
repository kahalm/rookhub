using System.Threading.RateLimiting;

namespace RookHub.Api.Services.Repertoires;

/// <summary>
/// Deckel je Konto für die ONLINE-Quelle des Explorers (Codereview N7-003): <c>GET /api/explorer/position|games</c> und
/// <c>POST /api/repertoires/{id}/explorer-analysis</c> teilen sich die EINE Lichess-Leitung (<see cref="LichessExplorerGate"/>,
/// global); ohne Deckel konnte ein Konto sie dauerhaft belegen. <c>Explorer:OnlinePerAccountPerMinute</c> (Vorgabe 30)
/// Aufrufe je Konto und Minute über alle drei Endpunkte zusammen; die lokale Quelle (<c>source=local</c>) bleibt unberührt.
///
/// <para>Kein <c>[EnableRateLimiting]</c>: die Quelle der Analyse steht im Rumpf, den der Partitionierer der Middleware
/// nicht sieht. Die Absage geht trotzdem durch <see cref="RateLimitRejectionHandler"/> — gleiche Antwort
/// (<c>{ message, code: rate_limited, retryAfterSeconds }</c> + <c>Retry-After</c>) und gleiches Log wie jede Policy.</para>
/// </summary>
public sealed class ExplorerOnlineQuota : IDisposable
{
    public const int DefaultPerMinute = 30;
    public const string PolicyName = "explorer-online";

    private readonly PartitionedRateLimiter<int> _limiter;

    public ExplorerOnlineQuota(IConfiguration? configuration = null)
    {
        var permit = Math.Max(1, configuration?.GetValue("Explorer:OnlinePerAccountPerMinute", DefaultPerMinute) ?? DefaultPerMinute);
        _limiter = PartitionedRateLimiter.Create<int, int>(userId => RateLimitPartition.GetFixedWindowLimiter(userId,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    }

    /// <summary>Online ist alles außer <c>local</c> — die Vorgabe des Dienstes (leer) ist online.</summary>
    public static bool IsOnline(string? source) => source is null or "" or "online";

    /// <summary>Darf das Konto jetzt online fragen? Lokal immer.</summary>
    public bool TryAcquire(int userId, string? source, out TimeSpan? retryAfter)
    {
        retryAfter = null;
        if (!IsOnline(source)) return true;
        using var lease = _limiter.AttemptAcquire(userId);
        if (lease.IsAcquired) return true;
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)) retryAfter = wait;
        return false;
    }

    /// <summary>Für die Controller: <c>true</c> = abgewiesen, die 429-Antwort ist geschrieben (der Controller gibt dann
    /// <c>EmptyResult</c> zurück).</summary>
    public async Task<bool> RejectAsync(HttpContext context, int userId, string? source, CancellationToken ct)
    {
        if (!IsOnline(source)) return false;
        using var lease = _limiter.AttemptAcquire(userId);
        if (lease.IsAcquired) return false;
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.RequestServices.GetRequiredService<RateLimitRejectionHandler>().HandleAsync(context, lease, ct, PolicyName);
        return true;
    }

    public void Dispose() => _limiter.Dispose();
}
