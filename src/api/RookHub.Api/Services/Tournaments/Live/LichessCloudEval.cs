using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>
/// Bewertung einer Stellung aus dem Lichess Cloud-Eval (<c>/api/cloud-eval?fen=</c>) — nur für die Engine-Balken der
/// Live-Bretter, wenn der Übertragungs-Strom kein <c>[%eval]</c> mitbringt (gemessen 25.11.2026: die österreichische
/// Bundesliga schickt nur <c>[%clk]</c>). Keine eigene Engine, kein Engine-Broker. Gecacht je Stellung (Treffer 6 h,
/// Fehltreffer 15 min — die meisten Mittelspiel-Stellungen stehen NICHT im Cloud-Speicher, der Balken bleibt dann leer),
/// gedrosselt auf einen Abruf je <see cref="MinSpacing"/>; nach einem 429 ruht er <see cref="HttpLichessLiveSource.RateLimitPause"/>.
/// Gefragt wird nur, solange jemand den Balken eingeschaltet hat (<see cref="BroadcastLiveHub"/>).
/// </summary>
public sealed class LichessCloudEval
{
    // 1 s war zu dicht: zusammen mit Strom und Rundenliste drosselte Lichess uns am 26.11.2026 dauerhaft (429). Die eigenen
    // Hintergrund-Engines bewerten ohnehin bis Tiefe 30; die Cloud ist nur noch der schnelle erste Wert.
    public static readonly TimeSpan MinSpacing = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HitTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan MissTtl = TimeSpan.FromMinutes(15);

    private readonly ILichessLiveSource _source;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LichessCloudEval> _logger;
    private readonly SemaphoreSlim _one = new(1, 1);
    private DateTime _last = DateTime.MinValue;
    private DateTime _pausedUntil = DateTime.MinValue;

    public LichessCloudEval(ILichessLiveSource source, IMemoryCache cache, ILogger<LichessCloudEval> logger)
    {
        _source = source;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Wie oft wirklich bei Lichess gefragt wurde (Tests).</summary>
    public int Requests { get; private set; }

    public TimeSpan Spacing { get; init; } = MinSpacing;

    /// <summary>Bewertung aus Weiß-Sicht oder <c>null</c> (nicht im Cloud-Speicher, pausiert, Fehler).</summary>
    public async Task<LiveEval?> GetAsync(string fen, CancellationToken ct)
    {
        var key = "live-cloud-eval:" + fen;
        if (_cache.TryGetValue(key, out LiveEval? cached)) return cached;
        if (DateTime.UtcNow < _pausedUntil) return null;
        await _one.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(key, out cached)) return cached;
            var wait = _last + Spacing - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _last = DateTime.UtcNow;
            Requests++;
            var json = await _source.CloudEvalAsync(fen, ct);
            var ev = json is null ? null : Parse(json);
            _cache.Set(key, ev, ev is null ? MissTtl : HitTtl);
            return ev;
        }
        catch (LichessRateLimitedException)
        {
            _pausedUntil = DateTime.UtcNow + HttpLichessLiveSource.RateLimitPause;
            _logger.LogInformation("Live-Bretter: Lichess Cloud-Eval 429 — Pause");
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogDebug(e, "Live-Bretter: Cloud-Eval fehlgeschlagen");
            return null;
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary><c>{"depth":30,"pvs":[{"moves":"…","cp":18}]}</c> bzw. <c>"mate":-3</c> → Bewertung (Weiß-Sicht).</summary>
    public static LiveEval? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        int? depth = r.TryGetProperty("depth", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : null;
        if (!r.TryGetProperty("pvs", out var pvs) || pvs.ValueKind != JsonValueKind.Array || pvs.GetArrayLength() == 0) return null;
        var pv = pvs[0];
        if (pv.TryGetProperty("mate", out var m) && m.ValueKind == JsonValueKind.Number) return new LiveEval(null, m.GetInt32(), "cloud", depth);
        if (pv.TryGetProperty("cp", out var cp) && cp.ValueKind == JsonValueKind.Number) return new LiveEval(cp.GetInt32(), null, "cloud", depth);
        return null;
    }
}
