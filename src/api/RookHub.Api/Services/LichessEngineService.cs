using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace RookHub.Api.Services;

/// <summary>Von Lichess gelistete External Engine inkl. serverseitig gehaltenem clientSecret.</summary>
public record LichessExternalEngine(string Id, string Name, int MaxThreads, int MaxHash, string ClientSecret);

/// <summary>Ergebnis der Engine-Liste: <c>Unauthorized</c> = Lichess hat den Token abgewiesen
/// (ungültig/abgelaufen/falscher Scope) — kein Wurf, damit die UI gezielt reagieren kann.</summary>
public record LichessEngineListResult(bool Unauthorized, List<LichessExternalEngine> Engines);

/// <summary>
/// Client für die Lichess-External-Engine-API (lichess.org/api#tag/External-engine): listet die auf
/// dem Lichess-Konto des Users registrierten Engines (OAuth-Token, Scope <c>engine:read</c>) und
/// fordert Analysen beim Broker (engine.lichess.ovh) an, dessen ndjson-Stream 1:1 an den Browser
/// durchgereicht wird. RookHub ist reiner CLIENT des offenen Protokolls — Engine/Provider laufen
/// beim User (eigene Maschine via offiziellem Provider, Miet-Anbieter wie stockfishcloud).
/// clientSecrets werden nur hier (im MemoryCache) gehalten, der Browser sieht sie nie.
/// URLs konfigurierbar (<c>Lichess:ApiUrl</c>/<c>Lichess:BrokerUrl</c>) — auch die Vorbereitung
/// für einen späteren RookHub-eigenen Broker (Phase 2), der dieselben Endpoints spricht.
/// </summary>
public class LichessEngineService
{
    private static readonly TimeSpan SecretCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LichessEngineService> _logger;
    private readonly string _apiUrl;
    private readonly string _brokerUrl;

    public LichessEngineService(HttpClient http, IMemoryCache cache, IConfiguration config,
        ILogger<LichessEngineService> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
        _apiUrl = (config["Lichess:ApiUrl"] ?? "https://lichess.org").TrimEnd('/');
        _brokerUrl = (config["Lichess:BrokerUrl"] ?? "https://engine.lichess.ovh").TrimEnd('/');
    }

    /// <summary>Registrierte External Engines des Tokens listen; jede gefundene Engine wandert in
    /// den Secret-Cache, damit ein folgendes Analyse nicht erneut listen muss.</summary>
    public async Task<LichessEngineListResult> ListEnginesAsync(int userId, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/api/external-engine");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Eigener kurzer Timeout: der HttpClient selbst ist timeout-los (Analyse-Streams laufen lange).
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ListTimeout);
        using var res = await _http.SendAsync(req, timeout.Token);

        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new LichessEngineListResult(true, []);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(timeout.Token);
        var engines = new List<LichessExternalEngine>();
        // Defensiv gegen eine unerwartete Antwortform: Lichess deklariert die API selbst als
        // „alpha, subject to change", und eine Zwischeninstanz kann eine Fehlerseite liefern.
        // Ein Wurf hier ginge an den Fehlern der Aufrufer vorbei (die fangen nur HTTP-Fehler)
        // und würde als 500 sichtbar — die Engine-Auswahl soll stattdessen einfach leer bleiben.
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("Lichess-Engine-Liste: unerwartete Antwortform {Kind}", doc.RootElement.ValueKind);
                return new LichessEngineListResult(false, engines);
            }
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var id = TryString(e, "id");
                if (string.IsNullOrEmpty(id)) continue;
                var engine = new LichessExternalEngine(
                    id,
                    TryString(e, "name") ?? id,
                    TryInt(e, "maxThreads") ?? 1,
                    TryInt(e, "maxHash") ?? 16,
                    TryString(e, "clientSecret") ?? string.Empty);
                engines.Add(engine);
                _cache.Set(CacheKey(userId, engine.Id), engine, SecretCacheTtl);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Lichess-Engine-Liste nicht lesbar");
            return new LichessEngineListResult(false, []);
        }
        return new LichessEngineListResult(false, engines);
    }

    /// <summary>Engine (inkl. clientSecret) auflösen — cache-first, sonst frisch listen.
    /// null = unbekannte Engine-ID oder Token abgewiesen.</summary>
    public async Task<LichessExternalEngine?> ResolveEngineAsync(int userId, string token, string engineId, CancellationToken ct)
    {
        if (_cache.TryGetValue<LichessExternalEngine>(CacheKey(userId, engineId), out var cached) && cached is not null)
            return cached;
        var list = await ListEnginesAsync(userId, token, ct);
        return list.Engines.FirstOrDefault(e => e.Id == engineId);
    }

    /// <summary>Analyse beim Broker anfordern. Liefert die OFFENE Upstream-Antwort (nur Header
    /// gelesen, Body = laufender ndjson-Stream) — der Aufrufer streamt durch und disposed sie.
    /// Abbruch über <paramref name="ct"/> (Browser weg ⇒ Broker beendet die Suche beim Provider).</summary>
    public async Task<HttpResponseMessage> AnalyseAsync(LichessExternalEngine engine, object work, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { clientSecret = engine.ClientSecret, work });
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_brokerUrl}/api/external-engine/{Uri.EscapeDataString(engine.Id)}/analyse")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        _logger.LogDebug("External-Engine-Analyse: engine={EngineId}", engine.Id);
        return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static string? TryString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? TryInt(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static string CacheKey(int userId, string engineId) => $"lichess-engine:{userId}:{engineId}";
}
