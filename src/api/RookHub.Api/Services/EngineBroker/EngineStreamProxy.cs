using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using RookHub.Api.DTOs.EngineBroker;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Die Live-Analyse über eine External Engine ohne Controller-Bindung (Zerlegung Z12, vorher im <c>EngineController</c>):
/// Work-Parameter prüfen und auf die Engine-Maxima klemmen, und den ndjson-Stream des Brokers an den Browser
/// durchreichen (Lebenszeichen, kein Puffern, reguläre Abbruchwege).
/// </summary>
internal static class EngineStreamProxy
{
    /// <summary>Obergrenzen der durchgereichten Work-Parameter — schützt Provider (und unser
    /// Proxy-Streaming) vor absurden Anfragen; die Engine-Maxima klemmen zusätzlich.</summary>
    private const int MaxDepth = EngineProtocol.MaxDepth;
    private const int MaxMovetimeMs = 300_000;
    private const long MaxNodes = 5_000_000_000;
    private const int MaxMoves = EngineProtocol.MaxMoves;

    /// <summary>Absolute Obergrenze für EINEN Analyse-Stream. Keines der Work-Limits begrenzt die
    /// Laufzeit (Tiefe 60 rechnet auf echter Hardware Stunden), und der einzige andere Abbruchgrund
    /// wäre der Browser selbst — ohne diese Schranke hielte ein Client Verbindungen beliebig lange.</summary>
    internal static readonly TimeSpan MaxStreamDuration = TimeSpan.FromMinutes(10);

    /// <summary>Gleichzeitige Analyse-Streams je User (mehrere Tabs sind legitim, hundert nicht).
    /// Der globale Limiter begrenzt nur die RATE neuer Anfragen, nicht die Zahl offener Ströme.
    /// Gezählt wird im <see cref="EngineActivityTracker"/> — der auch dem Hintergrund-Worker sagt,
    /// wann Live rechnet (Hintergrund pausieren) und seit wann Ruhe ist.</summary>
    internal const int MaxConcurrentStreamsPerUser = 4;

    /// <summary>Abstand der Leerzeilen-Lebenszeichen im Analyse-Stream (siehe <see cref="NdjsonHeartbeatPump"/>).</summary>
    private static readonly TimeSpan HeartbeatInterval = NdjsonHeartbeatPump.DefaultInterval;

    /// <summary>Prüft die Anfrage; <c>null</c> = in Ordnung, sonst die Meldung für 400.</summary>
    internal static string? ValidateWork(EngineAnalyseRequest? r)
    {
        if (r is null) return "Request body is required";
        if (string.IsNullOrWhiteSpace(r.SessionId) || r.SessionId.Length > 64) return "Invalid sessionId";
        if (string.IsNullOrWhiteSpace(r.InitialFen) || r.InitialFen.Length > 120) return "Invalid initialFen";
        if (r.Moves is { Count: > MaxMoves }) return "Too many moves";
        if (r.Moves is not null && r.Moves.Any(m => string.IsNullOrWhiteSpace(m) || m.Length > 5)) return "Invalid move";

        var limits = new[] { r.Depth.HasValue, r.Movetime.HasValue, r.Nodes.HasValue }.Count(x => x);
        if (limits != 1) return "Exactly one of depth/movetime/nodes is required";
        if (r.Depth is < 1 or > MaxDepth) return "Invalid depth";
        if (r.Movetime is < 1 or > MaxMovetimeMs) return "Invalid movetime";
        if (r.Nodes is < 1 or > MaxNodes) return "Invalid nodes";
        return null;
    }

    /// <summary>Das Work-Objekt für die Engine: Threads/Hash IMMER auf die gemeldeten Engine-Maxima geklemmt (Maxima
    /// defensiv auf ≥1, sonst wirft Clamp bei kaputten Upstream-Daten).</summary>
    internal static EngineWork WorkFor(EngineAnalyseRequest request, EngineRef engine)
    {
        var maxThreads = Math.Max(1, engine.MaxThreads);
        var maxHash = Math.Max(1, engine.MaxHash);
        var threads = Math.Clamp(request.Threads ?? maxThreads, 1, maxThreads);
        var hash = Math.Clamp(request.Hash ?? maxHash, 1, maxHash);
        return BuildWork(request, threads, hash);
    }

    /// <summary>Baut das Work-Objekt (oneOf depth/movetime/nodes + gemeinsame Felder; <c>variant</c> fest
    /// <c>chess</c>) — dasselbe für beide Broker.</summary>
    private static EngineWork BuildWork(EngineAnalyseRequest r, int threads, int hash) =>
        new(r.SessionId!, threads, hash, Math.Clamp(r.MultiPv, 1, EngineProtocol.MaxMultiPv), r.InitialFen!, r.Moves ?? [],
            r.Depth, r.Depth is null ? r.Movetime : null, r.Depth is null && r.Movetime is null ? r.Nodes : null);

    /// <summary>
    /// Den (erfolgreichen) Broker-Stream als ndjson an den Browser reichen, bis er endet, der Browser abbricht oder
    /// <paramref name="streamCt"/> (Laufzeitschranke) greift. Der Status ist danach gesendet; ein Abriss wird nur
    /// protokolliert.
    /// </summary>
    internal static async Task PumpToClientAsync(HttpContext http, EngineAnalysisSession upstream, string engineId,
        ILogger logger, CancellationToken streamCt)
    {
        var response = http.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "application/x-ndjson";
        response.Headers.CacheControl = "no-cache";
        // nginx: diese Antwort NICHT puffern — die info-Zeilen müssen live beim Browser ankommen.
        response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            var stream = upstream.Ndjson!;
            // Kein nacktes CopyToAsync: bei Funkstille des Brokers (MultiPV 5 ab Tiefe ~27
            // vergehen Minuten zwischen zwei Zeilen) schreibt der Pump Leerzeilen, damit kein
            // Proxy davor (NPM 60 s!) die Verbindung als tot kappt — siehe NdjsonHeartbeatPump.
            await NdjsonHeartbeatPump.PumpAsync(stream, response.Body, HeartbeatInterval, streamCt);
        }
        catch (OperationCanceledException)
        {
            // Browser hat abgebrochen (Stellungswechsel/Seite zu) ODER die Laufzeitschranke
            // hat gegriffen — beides sind reguläre Stopp-Wege, der Client hat seine Zeilen.
        }
        catch (IOException ex)
        {
            // Upstream mitten im Stream weg (Provider offline): Stream endet einfach; der
            // Client wertet aus, was er hat. Status ist längst gesendet.
            logger.LogWarning(ex, "External-Engine-Stream abgerissen (Engine {EngineId})", engineId);
        }
    }
}
