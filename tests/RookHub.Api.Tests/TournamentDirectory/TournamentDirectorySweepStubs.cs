using System.Net;
using System.Text;

namespace RookHub.Api.Tests.TournamentDirectory;

// Nachgebaute Crawler-Antworten fuer die Sweep-Tests (aus TournamentDirectoryServiceTests herausgezogen, 2026-11-17 —
// die Datei lag ueber 1 000 Zeilen, und die Wiederholungs-Tests brauchen dieselben Stubs).

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

    public HttpClient CreateClient(string name) =>
        new(_handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
}

/// <summary>
/// Antwortet auf die Trefferliste JE FOEDERATION mit einer eigenen Liste (fehlt eine: leer)
/// und auf jeden Turnierart-Durchgang mit einer leeren Liste.
/// </summary>
internal sealed class RowsPerFederationHandler(Dictionary<string, string> bodies) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        var fed = System.Text.RegularExpressions.Regex.Match(url, @"fed=([A-Z]{3})").Groups[1].Value;
        var body = url.Contains("&art=", StringComparison.Ordinal) ? "[]" : bodies.GetValueOrDefault(fed, "[]");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}

/// <summary>
/// Antwortet fuer eine bestimmte Foederation mit einem HttpClient-TIMEOUT (also einer
/// TaskCanceledException OHNE dass der Aufrufer abgebrochen haette) und sonst normal.
/// </summary>
internal sealed class TimeoutForFederationHandler : HttpMessageHandler
{
    private readonly string _federation;
    private readonly string _body;

    public TimeoutForFederationHandler(string federation, string body)
    {
        _federation = federation;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.Query.Contains($"fed={_federation}", StringComparison.Ordinal))
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing.");

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_body, Encoding.UTF8, "application/json")
        });
    }
}

/// <summary>
/// Antwortet auf die Trefferliste mit <c>body</c> — und auf jeden TURNIERART-Durchgang
/// (<c>art=0..3</c>) getrennt davon, standardmaessig mit einer leeren Liste.
///
/// <para>Die Trennung ist wesentlich: ohne sie bekaeme jeder Durchgang dieselbe Liste zurueck
/// und der Sweep ordnete JEDES Turnier der zuerst abgefragten Art zu. Die Vorgabe „ueberall
/// leer" heisst „in keiner Art gefunden" und laesst Art und System unangetastet — genau das,
/// was die uebrigen Tests brauchen.</para>
///
/// <para><see cref="TeamBody"/> beantwortet <c>art=2</c> („Rundenturnier fuer
/// Mannschaften"); fuer die uebrigen drei Arten gibt es <see cref="ArtBodies"/>.</para>
/// </summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly string _body;
    private readonly HttpStatusCode _status;

    public StubHandler(string body, HttpStatusCode status)
    {
        _body = body;
        _status = status;
    }

    /// <summary>Antwort je Turnierart; fehlt eine, gilt die leere Liste.</summary>
    public Dictionary<string, string> ArtBodies { get; } = [];

    /// <summary>Bequemer Zugriff auf <c>art=2</c> — der haeufigste Fall in den Tests.</summary>
    public string TeamBody
    {
        get => ArtBodies.TryGetValue("2", out var b) ? b : "[]";
        set => ArtBodies["2"] = value;
    }

    public HttpStatusCode TeamStatus { get; set; } = HttpStatusCode.OK;
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri?.ToString() ?? "";
        Requests.Add(url);

        var art = System.Text.RegularExpressions.Regex.Match(url, @"&art=(\d)");
        if (!art.Success)
        {
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }

        var body = ArtBodies.TryGetValue(art.Groups[1].Value, out var b) ? b : "[]";
        return Task.FromResult(new HttpResponseMessage(TeamStatus)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}
