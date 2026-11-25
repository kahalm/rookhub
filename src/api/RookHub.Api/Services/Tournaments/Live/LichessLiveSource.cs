using System.Net;
using System.Runtime.CompilerServices;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>Lichess hat 429 geantwortet — der Aufrufer pausiert (Lichess verlangt dann mindestens eine Minute Ruhe).</summary>
public sealed class LichessRateLimitedException() : Exception("Lichess: 429 Too Many Requests");

/// <summary>
/// Was die Live-Bretter von Lichess lesen. Eigene Schnittstelle, damit Hub und Finder ohne Netz testbar sind
/// (<c>FakeLichessLiveSource</c> in den Tests).
/// </summary>
public interface ILichessLiveSource
{
    /// <summary><c>GET /api/broadcast/round/{id}.pgn</c> — alle Partien der Runde (einmal). <c>null</c> = 404.</summary>
    Task<string?> GetRoundPgnAsync(string roundId, CancellationToken ct);
    /// <summary><c>GET /api/stream/broadcast/round/{id}.pgn</c> — offene Verbindung, Stücke wie sie kommen; endet, wenn
    /// Lichess schließt. Wirft <see cref="LichessRateLimitedException"/> bei 429.</summary>
    IAsyncEnumerable<string> StreamRoundAsync(string roundId, CancellationToken ct);
    /// <summary><c>GET /api/broadcast/{tourId}</c>; <c>null</c> = 404.</summary>
    Task<string?> GetTourAsync(string tourId, CancellationToken ct);
    /// <summary><c>GET /api/broadcast/-/-/{roundId}</c> (Turnier zu einem Runden-Link); <c>null</c> = 404.</summary>
    Task<string?> GetTourOfRoundAsync(string roundId, CancellationToken ct);
    /// <summary><c>GET /api/broadcast/top</c>.</summary>
    Task<string?> GetTopAsync(CancellationToken ct);
    /// <summary><c>GET /api/broadcast/search?q=</c> (erste Seite).</summary>
    Task<string?> SearchAsync(string query, CancellationToken ct);
    /// <summary><c>GET /api/cloud-eval?fen=</c>; <c>null</c> = keine Bewertung im Cloud-Speicher (404).</summary>
    Task<string?> CloudEvalAsync(string fen, CancellationToken ct);
}

/// <summary>
/// Abruf über HTTP. Höflich: eigener User-Agent (wie der übrige Lichess-Code, ohne persönliche Daten), nach einem 429
/// eine Pause für ALLE Abrufe dieser Quelle, höchstens zwei gleichzeitige Einzelabrufe (Ströme zählen nicht mit —
/// je Runde gibt es ohnehin nur einen, siehe <see cref="BroadcastLiveHub"/>).
/// </summary>
public sealed class HttpLichessLiveSource : ILichessLiveSource
{
    public const string ClientName = "lichess-live";
    /// <summary>So lange ruht die Quelle nach einem 429.</summary>
    public static readonly TimeSpan RateLimitPause = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _factory;
    private readonly string _site;
    private readonly SemaphoreSlim _gate = new(2, 2);
    private DateTime _pausedUntil = DateTime.MinValue;

    public HttpLichessLiveSource(IHttpClientFactory factory, IConfiguration? config = null)
    {
        _factory = factory;
        _site = (config?["Lichess:SiteUrl"] ?? "https://lichess.org").TrimEnd('/');
    }

    private void ThrowIfPaused()
    {
        if (DateTime.UtcNow < _pausedUntil) throw new LichessRateLimitedException();
    }

    private async Task<string?> GetAsync(string path, CancellationToken ct)
    {
        ThrowIfPaused();
        await _gate.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);
            using var r = await _factory.CreateClient(ClientName).GetAsync(_site + path, timeout.Token);
            if (r.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _pausedUntil = DateTime.UtcNow + RateLimitPause;
                throw new LichessRateLimitedException();
            }
            if (r.StatusCode == HttpStatusCode.NotFound) return null;
            r.EnsureSuccessStatusCode();
            return await r.Content.ReadAsStringAsync(timeout.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<string?> GetRoundPgnAsync(string roundId, CancellationToken ct) =>
        GetAsync($"/api/broadcast/round/{Uri.EscapeDataString(roundId)}.pgn", ct);

    public Task<string?> GetTourAsync(string tourId, CancellationToken ct) =>
        GetAsync($"/api/broadcast/{Uri.EscapeDataString(tourId)}", ct);

    public Task<string?> GetTourOfRoundAsync(string roundId, CancellationToken ct) =>
        GetAsync($"/api/broadcast/-/-/{Uri.EscapeDataString(roundId)}", ct);

    public Task<string?> GetTopAsync(CancellationToken ct) => GetAsync("/api/broadcast/top?page=1", ct);

    public Task<string?> SearchAsync(string query, CancellationToken ct) =>
        GetAsync($"/api/broadcast/search?q={Uri.EscapeDataString(query)}&page=1", ct);

    public Task<string?> CloudEvalAsync(string fen, CancellationToken ct) =>
        GetAsync($"/api/cloud-eval?fen={Uri.EscapeDataString(fen)}", ct);

    public async IAsyncEnumerable<string> StreamRoundAsync(string roundId, [EnumeratorCancellation] CancellationToken ct)
    {
        ThrowIfPaused();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_site}/api/stream/broadcast/round/{Uri.EscapeDataString(roundId)}.pgn");
        using var r = await _factory.CreateClient(ClientName).SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (r.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _pausedUntil = DateTime.UtcNow + RateLimitPause;
            throw new LichessRateLimitedException();
        }
        r.EnsureSuccessStatusCode();
        await using var stream = await r.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var buf = new char[8192];
        while (true)
        {
            var n = await reader.ReadAsync(buf.AsMemory(), ct);
            if (n <= 0) yield break;
            yield return new string(buf, 0, n);
        }
    }
}
