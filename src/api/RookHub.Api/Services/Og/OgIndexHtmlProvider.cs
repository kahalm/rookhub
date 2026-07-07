namespace RookHub.Api.Services.Og;

/// <summary>
/// Holt die LIVE-<c>index.html</c> der Angular-SPA vom Frontend-Container (nginx) und cacht sie kurz.
/// Grund: der OG-Renderer reichert genau diese echte index.html mit Meta-Tags an — die gehashten
/// Angular-Bootstrap-Scripts müssen unverändert erhalten bleiben, damit die SPA für Menschen bootet.
/// Bei Fehlern wird die letzte bekannte (auch abgelaufene) Version geliefert, sonst null.
/// </summary>
public class OgIndexHtmlProvider
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _indexUrl;
    private readonly ILogger<OgIndexHtmlProvider> _logger;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cached;
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    public OgIndexHtmlProvider(IHttpClientFactory httpFactory, IConfiguration config,
        ILogger<OgIndexHtmlProvider> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        var baseUrl = (config["Frontend:InternalUrl"] ?? "http://frontend:8080").TrimEnd('/');
        _indexUrl = $"{baseUrl}/index.html";
    }

    public async Task<string?> GetIndexHtmlAsync(CancellationToken ct = default)
    {
        if (_cached is not null && DateTimeOffset.UtcNow - _fetchedAt < Ttl) return _cached;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow - _fetchedAt < Ttl) return _cached;
            var client = _httpFactory.CreateClient("og-frontend");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var html = await client.GetStringAsync(_indexUrl, cts.Token);
            _cached = html;
            _fetchedAt = DateTimeOffset.UtcNow;
            return _cached;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OG: index.html von {Url} nicht abrufbar — nutze Cache/Fallback.", _indexUrl);
            return _cached; // ggf. abgelaufen, aber besser als nichts
        }
        finally
        {
            _lock.Release();
        }
    }
}
