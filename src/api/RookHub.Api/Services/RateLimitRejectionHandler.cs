using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Serilog.Context;

namespace RookHub.Api.Services;

/// <summary>
/// Antwort und Log einer Rate-Limit-Absage (<c>options.OnRejected</c> in <c>Program.cs</c>, Codereview A10-010).
///
/// <para><b>Warum:</b> Vorher setzte der Limiter nur den Status 429. <c>UseRateLimiter</c> steht vor
/// <c>UseSerilogRequestLogging</c>, und die Middleware selbst meldet eine Absage nur auf Debug (Microsoft.AspNetCore ist
/// auf Warning übersteuert) — eine abgewiesene Anfrage stand in KEINEM Log. Ein Scanner mit 10 000 Anfragen je Minute
/// hinterließ nur die ersten 100, und ob ein Nutzer hinter einer NAT-Adresse gedrosselt wurde, war nicht
/// nachzuvollziehen. Dazu kam die Antwort ohne Wartezeit und ohne Rumpf (die Offline-Warteschlange fiel immer auf 30 s
/// zurück).</para>
///
/// <para><b>Jetzt:</b> <c>Retry-After</c> (wo der Limiter eine Wartezeit kennt — Fenster ja, reine Gleichzeitigkeit nein),
/// Rumpf <c>{ message, retryAfterSeconds }</c>, und eine Warnung je (Adresse, Policy) und Minute; die Absagen dazwischen
/// werden gezählt und mit der nächsten Warnung gemeldet (<c>SuppressedRejections</c>). Jede einzeln zu loggen hieße, dem
/// Scanner die Elasticsearch-Platte zu überlassen.</para>
///
/// <para><c>RateLimitPolicy</c> nennt die Policy des ENDPUNKTS, „global" einen Endpunkt ohne eigene. Ob bei einem
/// Endpunkt mit Policy der globale Limiter oder die Policy abgewiesen hat, verrät <c>OnRejected</c> nicht.</para>
/// </summary>
public sealed class RateLimitRejectionHandler
{
    /// <summary>Eine Warnung je (Adresse, Policy) und Fenster.</summary>
    internal static readonly TimeSpan WarnWindow = TimeSpan.FromMinutes(1);

    /// <summary>Deckel der Drossel-Tabelle; darüber fliegen abgelaufene, dann die ältesten Einträge.</summary>
    internal const int MaxEntries = 2000;

    public const string Message = "Too many requests. Please try again later.";

    private readonly record struct Slot(DateTimeOffset WindowStart, int Suppressed);

    private readonly ConcurrentDictionary<string, Slot> _slots = new();
    private readonly ILogger<RateLimitRejectionHandler> _logger;

    public RateLimitRejectionHandler(ILogger<RateLimitRejectionHandler> logger) => _logger = logger;

    /// <summary>Für <c>RateLimiterOptions.OnRejected</c> — der Dienst ist ein Singleton im DI.</summary>
    public static ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken) =>
        context.HttpContext.RequestServices.GetRequiredService<RateLimitRejectionHandler>()
            .HandleAsync(context.HttpContext, context.Lease, cancellationToken);

    /// <summary>Der Status (429) steht schon, wenn die Middleware hierher ruft.</summary>
    public async ValueTask HandleAsync(HttpContext context, RateLimitLease lease, CancellationToken cancellationToken)
    {
        int? retryAfterSeconds = null;
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            context.Response.Headers.RetryAfter = retryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture);
        }

        LogRejection(context, retryAfterSeconds, DateTimeOffset.UtcNow);

        await context.Response.WriteAsJsonAsync(new { message = Message, retryAfterSeconds }, cancellationToken);
    }

    private void LogRejection(HttpContext context, int? retryAfterSeconds, DateTimeOffset now)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var policy = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "global";
        var (warn, suppressed) = Register($"{ip}|{policy}", now);
        if (!warn) return;

        // StatusCode wie beim Request-Log (die Ingest-Pipeline macht daraus http.response.status_code) — die Absage lief
        // an UseSerilogRequestLogging vorbei, und IpAddress setzt der Enricher erst NACH dem Limiter.
        using (LogContext.PushProperty("StatusCode", StatusCodes.Status429TooManyRequests))
        {
            _logger.LogWarning(
                "Rate limit: rejected {RequestMethod} {RequestPath} from {IpAddress} with 429 (policy {RateLimitPolicy}, retry after {RetryAfterSeconds} s, {SuppressedRejections} further rejections since the previous warning)",
                context.Request.Method, context.Request.Path.Value, ip, policy, retryAfterSeconds, suppressed);
        }
    }

    /// <summary>
    /// Drossel: die erste Absage je <paramref name="key"/> und Fenster wird gemeldet, die folgenden nur gezählt; die Zahl
    /// geht mit der nächsten Meldung raus. Bei echter Gleichzeitigkeit kann eine Meldung zu viel durchrutschen — für ein
    /// Log unkritisch, ein Lock wäre teurer.
    /// </summary>
    internal (bool Warn, int Suppressed) Register(string key, DateTimeOffset now)
    {
        var warn = false;
        var suppressed = 0;
        _slots.AddOrUpdate(
            key,
            _ =>
            {
                warn = true;
                suppressed = 0;
                return new Slot(now, 0);
            },
            (_, slot) =>
            {
                if (now - slot.WindowStart < WarnWindow)
                {
                    warn = false;
                    suppressed = 0;
                    return slot with { Suppressed = slot.Suppressed + 1 };
                }
                warn = true;
                suppressed = slot.Suppressed;
                return new Slot(now, 0);
            });

        if (_slots.Count > MaxEntries) Prune(now);
        return (warn, suppressed);
    }

    /// <summary>Nur für Tests: aktuelle Größe der Drossel-Tabelle.</summary>
    internal int EntryCount => _slots.Count;

    private void Prune(DateTimeOffset now)
    {
        foreach (var (key, slot) in _slots)
            if (now - slot.WindowStart >= WarnWindow)
                _slots.TryRemove(key, out _);

        // Kommen viele Adressen innerhalb EINES Fensters, ist nichts abgelaufen — dann die ältesten weg (Kosten: für eine
        // verworfene Adresse höchstens eine Warnung mehr).
        if (_slots.Count <= MaxEntries) return;
        foreach (var entry in _slots.OrderBy(e => e.Value.WindowStart).Take(_slots.Count - MaxEntries))
            _slots.TryRemove(entry.Key, out _);
    }
}
