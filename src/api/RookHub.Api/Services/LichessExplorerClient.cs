using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RookHub.Api.Services;

/// <summary>
/// Welche Partien der Explorer zählt: die Lichess-Datenbank (gefiltert nach Elo-Stufen und
/// Bedenkzeiten) oder die Meister-Datenbank (ohne Filter). Immer über <see cref="Create"/> bauen —
/// dort werden die Werte geprüft und sortiert, damit dieselbe Auswahl denselben Speicher-Schlüssel hat.
/// </summary>
public sealed record ExplorerQuery(string Database, IReadOnlyList<int> Ratings, IReadOnlyList<string> Speeds)
{
    public const string Lichess = "lichess";
    public const string Masters = "masters";

    /// <summary>Die Elo-Stufen, die der Lichess-Explorer kennt (je Stufe „ab diesem Wert").</summary>
    public static readonly IReadOnlyList<int> AllowedRatings = new[] { 0, 1000, 1200, 1400, 1600, 1800, 2000, 2200, 2500 };

    /// <summary>Die Bedenkzeiten des Lichess-Explorers, in seiner Schreibweise.</summary>
    public static readonly IReadOnlyList<string> AllowedSpeeds =
        new[] { "ultraBullet", "bullet", "blitz", "rapid", "classical", "correspondence" };

    /// <summary>Speicher-Schlüssel ohne Stellung. Die Meister-Datenbank hat keine Filter.</summary>
    public string CachePrefix => Database == Masters
        ? "masters|"
        : $"lichess|{string.Join(',', Ratings)}|{string.Join(',', Speeds)}|";

    /// <summary>Prüft und ordnet die Auswahl. Wirft <see cref="ArgumentException"/> bei unbekannten
    /// Werten; für Lichess braucht es mindestens eine Elo-Stufe und eine Bedenkzeit.</summary>
    public static ExplorerQuery Create(string? database, IEnumerable<int>? ratings, IEnumerable<string>? speeds)
    {
        var db = string.IsNullOrWhiteSpace(database) ? Lichess : database.Trim().ToLowerInvariant();
        if (db == Masters) return new ExplorerQuery(Masters, Array.Empty<int>(), Array.Empty<string>());
        if (db != Lichess) throw new ArgumentException($"Unbekannte Datenbank: {database}");

        var r = (ratings ?? Array.Empty<int>()).Distinct().OrderBy(x => x).ToList();
        if (r.Count == 0) throw new ArgumentException("Mindestens eine Elo-Stufe wählen.");
        if (r.Any(x => !AllowedRatings.Contains(x))) throw new ArgumentException("Unbekannte Elo-Stufe.");

        // Reihenfolge = die des Explorers, nicht alphabetisch — so bleibt der Schlüssel lesbar.
        var wanted = (speeds ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) throw new ArgumentException("Mindestens eine Bedenkzeit wählen.");
        var s = AllowedSpeeds.Where(wanted.Contains).ToList();
        if (s.Count != wanted.Count) throw new ArgumentException("Unbekannte Bedenkzeit.");

        return new ExplorerQuery(Lichess, r, s);
    }
}

/// <summary>Ein Zug in einer Explorer-Stellung: wie oft er dort gespielt wurde.</summary>
public sealed record ExplorerMoveStat(
    [property: JsonPropertyName("u")] string Uci,
    [property: JsonPropertyName("s")] string San,
    [property: JsonPropertyName("g")] long Games,
    [property: JsonPropertyName("o")] string? Opening,
    [property: JsonPropertyName("e")] string? Eco);

/// <summary>Explorer-Daten einer Stellung: alle Partien dort (<see cref="Total"/>) und die gespielten
/// Züge. <see cref="Total"/> ist der Nenner für Anteile — nicht die Summe der gelieferten Züge.</summary>
public sealed record ExplorerPositionStats(
    [property: JsonPropertyName("t")] long Total,
    [property: JsonPropertyName("m")] List<ExplorerMoveStat> Moves)
{
    private static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static ExplorerPositionStats? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<ExplorerPositionStats>(json, Options); }
        catch (JsonException) { return null; }
    }
}

public enum ExplorerFetchStatus { Ok, RateLimited, Unauthorized, Failed }

/// <summary>
/// Eine Leitung zum Explorer für den ganzen Prozess: Lichess bittet darum, Anfragen nacheinander
/// zu stellen und nach einem 429 eine Minute zu warten. Singleton — jeder Lauf jedes Nutzers geht
/// durch dieselbe Tür.
///
/// <para><b>Nacheinander allein genügt nicht — der Explorer rechnet mit einem Kontingent.</b> Gegen
/// den echten Explorer gemessen (2026-09-22, mit Token): 21 Anfragen dicht hintereinander → 429;
/// mit 0,5 s Abstand 429 nach 27 Anfragen (17 s), mit 1 s Abstand nach 33 (36 s). Das passt zu einem
/// Eimer von gut 20 Anfragen, der mit rund 0,3 je Sekunde nachläuft. Die Leitung hält deshalb selbst
/// einen etwas kleineren Eimer: <see cref="Burst"/> sofort, danach eine Anfrage je
/// <see cref="RefillInterval"/> (Vorgabe 15 und 4 s = 15 je Minute auf Dauer). Ein kleines Repertoire
/// ist damit in Sekunden durch, ein großes läuft gleichmäßig statt in Minuten-Strafpausen.</para>
/// </summary>
public sealed class LichessExplorerGate
{
    /// <summary>So lange ruht die Leitung nach einem 429 (Empfehlung der Lichess-API-Doku).</summary>
    public static readonly TimeSpan RateLimitPause = TimeSpan.FromMinutes(1);

    public const int DefaultBurst = 15;
    public static readonly TimeSpan DefaultRefillInterval = TimeSpan.FromSeconds(4);

    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly TimeProvider _time;
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;
    private double _tokens;
    private DateTimeOffset _lastRefill;

    /// <param name="burst">So viele Anfragen gehen ohne Wartezeit raus.</param>
    /// <param name="refillInterval">Danach eine je Intervall; <see cref="TimeSpan.Zero"/> = ohne Grenze (Tests).</param>
    public LichessExplorerGate(TimeProvider? time = null, int? burst = null, TimeSpan? refillInterval = null)
    {
        _time = time ?? TimeProvider.System;
        Burst = Math.Max(1, burst ?? DefaultBurst);
        RefillInterval = refillInterval ?? DefaultRefillInterval;
        _tokens = Burst;
        _lastRefill = _time.GetUtcNow();
    }

    public int Burst { get; }
    public TimeSpan RefillInterval { get; }

    /// <summary>Wartet (innerhalb der Leitung) auf eine freie Anfrage und verbraucht sie.</summary>
    internal async Task PaceAsync(CancellationToken ct)
    {
        Refill();
        if (_tokens < 1)
        {
            var wait = TimeSpan.FromTicks((long)((1 - _tokens) * RefillInterval.Ticks));
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            Refill();
        }
        _tokens = Math.Max(0, _tokens - 1);
    }

    private void Refill()
    {
        var now = _time.GetUtcNow();
        _tokens = RefillInterval <= TimeSpan.Zero
            ? Burst
            : Math.Min(Burst, _tokens + Math.Max(0, (now - _lastRefill) / RefillInterval));
        _lastRefill = now;
    }

    /// <summary>Restliche Sperrzeit nach einem 429, sonst <c>null</c>.</summary>
    public TimeSpan? BlockedFor
    {
        get
        {
            var left = _blockedUntil - _time.GetUtcNow();
            return left > TimeSpan.Zero ? left : null;
        }
    }

    /// <summary>Nach einem 429: eine Minute Ruhe, und der Eimer ist danach leer statt voll.</summary>
    public void Block()
    {
        _blockedUntil = _time.GetUtcNow() + RateLimitPause;
        _tokens = 0;
        _lastRefill = _blockedUntil;
    }

    public Task WaitAsync(CancellationToken ct) => _one.WaitAsync(ct);

    public void Release() => _one.Release();
}

/// <summary>
/// HTTP-Client für den Lichess-Eröffnungs-Explorer (<c>explorer.lichess.ovh</c>). Kennt nur die
/// Leitung — Speicher, Token-Wahl und Auswertung liegen in <see cref="RepertoireExplorerService"/>.
/// </summary>
public class LichessExplorerClient
{
    public const string BaseUrl = "https://explorer.lichess.ovh/";

    /// <summary>Mehr Züge, als je in einer Stellung vorkommen, die über der Schwelle liegen können
    /// (die Grundstellung hat 20). Der Explorer liefert sie nach Häufigkeit sortiert.</summary>
    private const int MaxMoves = 40;

    private readonly HttpClient _http;
    private readonly LichessExplorerGate _gate;
    private readonly ILogger<LichessExplorerClient> _logger;

    public LichessExplorerClient(HttpClient http, LichessExplorerGate gate, ILogger<LichessExplorerClient> logger)
    {
        _http = http;
        _gate = gate;
        _logger = logger;
    }

    public async Task<(ExplorerFetchStatus Status, ExplorerPositionStats? Stats)> FetchAsync(
        string fen, ExplorerQuery query, string token, CancellationToken ct)
    {
        if (_gate.BlockedFor is not null) return (ExplorerFetchStatus.RateLimited, null);

        var url = query.Database == ExplorerQuery.Masters
            ? $"masters?fen={Uri.EscapeDataString(fen)}&moves={MaxMoves}&topGames=0"
            : $"lichess?variant=standard&fen={Uri.EscapeDataString(fen)}"
              + $"&ratings={string.Join(',', query.Ratings)}&speeds={string.Join(',', query.Speeds)}"
              + $"&moves={MaxMoves}&topGames=0&recentGames=0";

        await _gate.WaitAsync(ct);
        try
        {
            if (_gate.BlockedFor is not null) return (ExplorerFetchStatus.RateLimited, null);
            await _gate.PaceAsync(ct);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _gate.Block();
                _logger.LogWarning("Lichess-Explorer: 429 — Leitung ruht {Pause}", LichessExplorerGate.RateLimitPause);
                return (ExplorerFetchStatus.RateLimited, null);
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return (ExplorerFetchStatus.Unauthorized, null);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Lichess-Explorer: HTTP {Status} für {Fen}", (int)response.StatusCode, fen);
                return (ExplorerFetchStatus.Failed, null);
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            var stats = Parse(await JsonDocument.ParseAsync(body, cancellationToken: ct));
            return stats is null ? (ExplorerFetchStatus.Failed, null) : (ExplorerFetchStatus.Ok, stats);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "Lichess-Explorer nicht erreichbar für {Fen}", fen);
            return (ExplorerFetchStatus.Failed, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Explorer-Antwort → kompakte Form. Gesamtzahl = Weiß + Remis + Schwarz der Stellung.</summary>
    internal static ExplorerPositionStats? Parse(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        var total = Count(root);
        var moves = new List<ExplorerMoveStat>();
        if (root.TryGetProperty("moves", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in arr.EnumerateArray())
            {
                var uci = m.TryGetProperty("uci", out var u) ? u.GetString() : null;
                var san = m.TryGetProperty("san", out var s) ? s.GetString() : null;
                if (string.IsNullOrEmpty(uci) || string.IsNullOrEmpty(san)) continue;
                string? opening = null, eco = null;
                if (m.TryGetProperty("opening", out var o) && o.ValueKind == JsonValueKind.Object)
                {
                    opening = o.TryGetProperty("name", out var n) ? n.GetString() : null;
                    eco = o.TryGetProperty("eco", out var e) ? e.GetString() : null;
                }
                moves.Add(new ExplorerMoveStat(uci, san, Count(m), opening, eco));
            }
        }
        return new ExplorerPositionStats(total, moves);
    }

    private static long Count(JsonElement e)
    {
        long n = 0;
        foreach (var key in new[] { "white", "draws", "black" })
            if (e.TryGetProperty(key, out var v) && v.TryGetInt64(out var x)) n += x;
        return n;
    }
}
