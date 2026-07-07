namespace RookHub.Api.Services.Og;

/// <summary>
/// Holt die LIVE-<c>index.html</c> der Angular-SPA vom Frontend-Container (nginx) und cacht sie kurz.
/// Grund: der OG-Renderer reichert genau diese echte index.html mit Meta-Tags an — die gehashten
/// Angular-Bootstrap-Scripts müssen unverändert erhalten bleiben, damit die SPA für Menschen bootet.
///
/// Der Frontend-Container ist je nach Deployment unter unterschiedlichem DNS-Namen erreichbar
/// (Compose-Servicename <c>frontend</c> ODER container_name <c>rookhub-frontend</c>). Deshalb werden
/// mehrere Kandidaten-URLs durchprobiert; die erste funktionierende wird gemerkt. Bei Totalausfall
/// liefert die Methode <c>null</c> — der Controller fällt dann auf die unveränderte SPA zurück.
/// </summary>
public class OgIndexHtmlProvider
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IReadOnlyList<string> _candidateUrls;
    private readonly ILogger<OgIndexHtmlProvider> _logger;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cached;
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;
    private string? _workingUrl; // zuletzt erfolgreiche Kandidaten-URL (bevorzugt beim nächsten Refresh)

    public OgIndexHtmlProvider(IHttpClientFactory httpFactory, IConfiguration config,
        ILogger<OgIndexHtmlProvider> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;

        // Reihenfolge: explizit konfiguriert (falls gesetzt) → Compose-Servicename → container_name.
        var configured = config["Frontend:InternalUrl"];
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured.TrimEnd('/'));
        candidates.Add("http://frontend:8080");
        candidates.Add("http://rookhub-frontend:8080");
        _candidateUrls = candidates.Distinct().Select(u => $"{u}/index.html").ToList();
    }

    public async Task<string?> GetIndexHtmlAsync(CancellationToken ct = default)
    {
        if (_cached is not null && DateTimeOffset.UtcNow - _fetchedAt < Ttl) return _cached;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow - _fetchedAt < Ttl) return _cached;

            var client = _httpFactory.CreateClient("og-frontend");

            // Bekannte funktionierende URL zuerst, dann die restlichen Kandidaten.
            var ordered = _workingUrl is null
                ? _candidateUrls
                : _candidateUrls.OrderByDescending(u => u == _workingUrl).ToList();

            foreach (var url in ordered)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(5));
                    var html = await client.GetStringAsync(url, cts.Token);
                    if (string.IsNullOrWhiteSpace(html)) continue;
                    _cached = html;
                    _fetchedAt = DateTimeOffset.UtcNow;
                    _workingUrl = url;
                    return _cached;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "OG: index.html von {Url} nicht abrufbar.", url);
                }
            }

            _logger.LogWarning("OG: index.html von KEINEM Frontend-Kandidaten abrufbar ({Urls}).",
                string.Join(", ", _candidateUrls));
            return _cached; // ggf. abgelaufen, aber besser als nichts
        }
        finally
        {
            _lock.Release();
        }
    }
}
