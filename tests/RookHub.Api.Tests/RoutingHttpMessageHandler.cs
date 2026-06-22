using System.Net;
using System.Text;

namespace RookHub.Api.Tests;

/// <summary>
/// Test-Handler, der je nach Anfrage-Pfad (Substring-Match, in Reihenfolge) eine andere Antwort liefert.
/// Für Crawler-Proxy-Tests, bei denen mehrere Endpunkte (Detail, Ergebnisse, Crawl-Start) unterschiedlich
/// antworten müssen. Nicht gematchte Pfade → <c>defaultBody</c> (Default "{}", 200). Zählt Aufrufe je Regel
/// (<see cref="Hits"/>) und alle (<see cref="Total"/>).
/// </summary>
public class RoutingHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(string Contains, string Body, HttpStatusCode Status)> _routes = new();
    private readonly string _defaultBody;

    public int Total { get; private set; }
    public Dictionary<string, int> Hits { get; } = new();

    public RoutingHttpMessageHandler(string defaultBody = "{}")
    {
        _defaultBody = defaultBody;
    }

    public RoutingHttpMessageHandler Map(string pathContains, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes.Add((pathContains, body, status));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Total++;
        var path = request.RequestUri?.PathAndQuery ?? string.Empty;
        foreach (var (contains, body, status) in _routes)
        {
            if (path.Contains(contains))
            {
                Hits[contains] = Hits.TryGetValue(contains, out var c) ? c + 1 : 1;
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
            }
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_defaultBody, Encoding.UTF8, "application/json"),
        });
    }
}
