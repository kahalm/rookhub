namespace RookHub.Api.Services.Og;

/// <summary>
/// Holt die LIVE-<c>index.html</c> der Angular-SPA vom Frontend-Container (nginx) und cacht sie kurz.
/// Grund: der OG-Renderer reichert genau diese echte index.html mit Meta-Tags an — die gehashten
/// Angular-Bootstrap-Scripts müssen unverändert erhalten bleiben, damit die SPA für Menschen bootet.
///
/// Es gibt ZWEI Seiten mit je eigener Shell: RookHub und die Turnierseite (turnier.oberschmid.homes,
/// eigenes Angular-Projekt, eigenes Image). Ein geteilter Turnier-Link <c>/t/{id}</c> liegt auf der
/// Turnierseite — bekäme der Browser dort die RookHub-Shell, würde die falsche App booten und den Link
/// auf das Dashboard umleiten. Welche Shell gemeint ist, sagt der nginx über <c>X-Og-Site</c>.
///
/// Der Frontend-Container ist je nach Deployment unter unterschiedlichem DNS-Namen erreichbar
/// (Compose-Servicename ODER container_name, dev mit <c>-dev</c>-Suffix). Deshalb werden je Seite
/// mehrere Kandidaten-URLs durchprobiert; die erste funktionierende wird gemerkt. Bei Totalausfall
/// liefert die Methode <c>null</c> — der Controller fällt dann auf die unveränderte SPA zurück.
/// </summary>
public class OgIndexHtmlProvider
{
    /// <summary>Wert von <c>X-Og-Site</c> für die Turnierseite; alles andere = RookHub.</summary>
    public const string TurnierSite = "turnier";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OgIndexHtmlProvider> _logger;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly Dictionary<string, Shell> _shells;

    /// <summary>Cache-Zustand einer Seite (Shell). Je Seite eine eigene index.html.</summary>
    private sealed class Shell
    {
        public required IReadOnlyList<string> CandidateUrls { get; init; }
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public string? Cached { get; set; }
        public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.MinValue;
        public string? WorkingUrl { get; set; } // zuletzt erfolgreiche Kandidaten-URL
    }

    public OgIndexHtmlProvider(IHttpClientFactory httpFactory, IConfiguration config,
        ILogger<OgIndexHtmlProvider> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;

        // Reihenfolge: explizit konfiguriert (falls gesetzt) → Compose-Servicename → container_name.
        _shells = new Dictionary<string, Shell>(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = new()
            {
                CandidateUrls = BuildCandidates(config["Frontend:InternalUrl"],
                    "http://frontend:8080", "http://rookhub-frontend:8080", "http://rookhub-frontend-dev:8080"),
            },
            [TurnierSite] = new()
            {
                CandidateUrls = BuildCandidates(config["Frontend:TurnierInternalUrl"],
                    "http://turnier:8080", "http://rookhub-turnier:8080", "http://rookhub-turnier-dev:8080"),
            },
        };
    }

    internal static IReadOnlyList<string> BuildCandidates(string? configured, params string[] defaults)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured.TrimEnd('/'));
        candidates.AddRange(defaults);
        return candidates.Distinct().Select(u => $"{u}/index.html").ToList();
    }

    /// <param name="site">Wert des <c>X-Og-Site</c>-Headers; unbekannt/leer = RookHub.</param>
    public async Task<string?> GetIndexHtmlAsync(string? site = null, CancellationToken ct = default)
    {
        var shell = _shells.TryGetValue(site ?? string.Empty, out var s) ? s : _shells[string.Empty];

        if (shell.Cached is not null && DateTimeOffset.UtcNow - shell.FetchedAt < Ttl) return shell.Cached;

        await shell.Lock.WaitAsync(ct);
        try
        {
            if (shell.Cached is not null && DateTimeOffset.UtcNow - shell.FetchedAt < Ttl) return shell.Cached;

            var client = _httpFactory.CreateClient("og-frontend");

            // Bekannte funktionierende URL zuerst, dann die restlichen Kandidaten.
            var ordered = shell.WorkingUrl is null
                ? shell.CandidateUrls
                : shell.CandidateUrls.OrderByDescending(u => u == shell.WorkingUrl).ToList();

            foreach (var url in ordered)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(5));
                    var html = await client.GetStringAsync(url, cts.Token);
                    if (string.IsNullOrWhiteSpace(html)) continue;
                    shell.Cached = html;
                    shell.FetchedAt = DateTimeOffset.UtcNow;
                    shell.WorkingUrl = url;
                    return shell.Cached;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "OG: index.html von {Url} nicht abrufbar.", url);
                }
            }

            _logger.LogWarning("OG: index.html von KEINEM Frontend-Kandidaten abrufbar ({Urls}).",
                string.Join(", ", shell.CandidateUrls));
            return shell.Cached; // ggf. abgelaufen, aber besser als nichts
        }
        finally
        {
            shell.Lock.Release();
        }
    }
}
