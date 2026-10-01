using System.Net;
using System.Text.Json;
using RookHub.Api.Exceptions;

namespace RookHub.Api.Services;

/// <summary>
/// Der EINE Lese-Weg der Hintergrunddienste (Turniersuche, Verbandskalender, Turnierverlauf,
/// Rundenplan, Spielort-Klaerung) zum Crawler — ueber den benannten Client
/// <see cref="TournamentDirectoryService.CrawlerClientName"/> mit seinem langen Timeout.
///
/// <para><b>Warum.</b> Vorher hatte jede Datei ihre eigene Fetch-Methode (Codereview A5-013): die
/// meisten warfen bei einem Fehlerstatus <see cref="CrawlerRequestException"/>, Turnierverlauf,
/// Rundenplan und Spielort-Klaerung dagegen ueber <c>EnsureSuccessStatusCode</c> eine
/// <see cref="HttpRequestException"/> — dieselbe Lage (Crawler 503) sah je nach Pfad anders aus,
/// und der Statuscode samt Rumpf ging auf dem zweiten Weg verloren. Hier gilt fuer alle: Fehlerstatus
/// → <see cref="CrawlerRequestException"/> mit Status und Rumpf.</para>
///
/// <para>Der Live-Proxy (<see cref="CrawlerProxyService"/>, 30 s) bleibt bewusst getrennt: eine
/// Suche hinter dem Rate-Limiter des Crawlers braucht legitim laenger.</para>
/// </summary>
public static class DirectoryCrawlerClient
{
    /// <summary>Die Web-Vorgaben (camelCase, Gross-/Kleinschreibung egal) — wie der Crawler serialisiert.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// <c>GET path</c> und die Antwort als <typeparamref name="T"/>.
    /// </summary>
    /// <param name="absentOn">Statuscode, der „gibt es nicht" heisst (404 Detailseite weg, 204 kein
    /// Block auf der Seite) — dann <c>default</c> statt einer Ausnahme.</param>
    /// <param name="emptyAsAbsent">Ein leerer Rumpf trotz Erfolg gilt als <c>default</c>. Ohne das
    /// scheitert er am JSON-Lesen — gewollt fuer die Kalender-Listen: „leer geliefert" darf dort
    /// nicht wie „alles verschwunden" aussehen.</param>
    /// <exception cref="CrawlerRequestException">Jeder andere Fehlerstatus.</exception>
    public static async Task<T?> GetCrawlerJsonAsync<T>(
        this IHttpClientFactory factory, string path, CancellationToken ct,
        HttpStatusCode? absentOn = null, bool emptyAsAbsent = false)
    {
        var client = factory.CreateClient(TournamentDirectoryService.CrawlerClientName);
        using var response = await client.GetAsync(path, ct);
        if (absentOn is { } absent && response.StatusCode == absent) return default;

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrawlerRequestException(response.StatusCode, body);

        if (emptyAsAbsent && string.IsNullOrWhiteSpace(body)) return default;
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }
}
