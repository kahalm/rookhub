using System.Net;
using System.Text;
using System.Text.Json;
using RookHub.Api.Exceptions;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der EINE Lese-Weg der Hintergrunddienste zum Crawler (Codereview A5-013). Vorher hatte jede
/// Datei ihre eigene Fetch-Methode, und dieselbe Lage (Crawler 503) kam je nach Pfad als
/// <see cref="CrawlerRequestException"/> oder als <see cref="HttpRequestException"/> an — auf dem
/// zweiten Weg ohne Statuscode und Rumpf.
/// </summary>
public class DirectoryCrawlerClientTests
{
    private sealed record Row(string? EventId, string? StartDate);

    [Fact]
    public async Task GetCrawlerJsonAsync_FehlerStatus_WirftCrawlerRequestExceptionMitStatusUndRumpf()
    {
        var factory = new Factory(HttpStatusCode.ServiceUnavailable, """{"message":"Crawler busy"}""");

        var ex = await Assert.ThrowsAsync<CrawlerRequestException>(
            () => factory.GetCrawlerJsonAsync<List<Row>>("/api/x", CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Contains("Crawler busy", ex.ResponseBody);
    }

    [Fact]
    public async Task GetCrawlerJsonAsync_LiestCamelCaseMitWebVorgaben_UeberDenBenanntenClient()
    {
        var factory = new Factory(HttpStatusCode.OK, """[{"eventId":"7","startDate":"2026-10-03"}]""");

        var rows = await factory.GetCrawlerJsonAsync<List<Row>>("/api/fsi-calendar?from=2026-10-01",
            CancellationToken.None);

        var row = Assert.Single(rows!);
        Assert.Equal("7", row.EventId);
        Assert.Equal("2026-10-03", row.StartDate);
        Assert.Equal(TournamentDirectoryService.CrawlerClientName, factory.RequestedName);
        Assert.Equal("/api/fsi-calendar?from=2026-10-01", factory.RequestedPathAndQuery);
    }

    [Fact]
    public async Task GetCrawlerJsonAsync_AbwesendStatus_LiefertNullStattAusnahme()
    {
        var factory = new Factory(HttpStatusCode.NotFound, "nicht da");

        var row = await factory.GetCrawlerJsonAsync<Row>("/api/x", CancellationToken.None,
            absentOn: HttpStatusCode.NotFound);

        Assert.Null(row);
    }

    [Fact]
    public async Task GetCrawlerJsonAsync_AnderesStatusAlsAbwesend_WirftWeiter()
    {
        var factory = new Factory(HttpStatusCode.NotFound, "nicht da");

        await Assert.ThrowsAsync<CrawlerRequestException>(() => factory.GetCrawlerJsonAsync<Row>(
            "/api/x", CancellationToken.None, absentOn: HttpStatusCode.NoContent));
    }

    /// <summary>
    /// Ein leerer Rumpf trotz 200 darf bei den Kalender-Listen NICHT wie eine leere Liste
    /// aussehen — sonst saehe die Verschwunden-Erkennung „alles weg". Nur wer es ausdruecklich
    /// sagt (Spielerkarte, Turnierdetails, Trefferliste), bekommt dafuer <c>null</c>.
    /// </summary>
    [Fact]
    public async Task GetCrawlerJsonAsync_LeererRumpf_ScheitertOhneSchalter_UndIstMitSchalterNull()
    {
        var factory = new Factory(HttpStatusCode.OK, "");

        await Assert.ThrowsAsync<JsonException>(
            () => factory.GetCrawlerJsonAsync<List<Row>>("/api/x", CancellationToken.None));
        Assert.Null(await factory.GetCrawlerJsonAsync<List<Row>>("/api/x", CancellationToken.None,
            emptyAsAbsent: true));
    }

    private sealed class Factory(HttpStatusCode status, string body) : IHttpClientFactory
    {
        public string? RequestedName { get; private set; }
        public string? RequestedPathAndQuery => _handler.PathAndQuery;
        private readonly Handler _handler = new(status, body);

        public HttpClient CreateClient(string name)
        {
            RequestedName = name;
            return new HttpClient(_handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
        }
    }

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? PathAndQuery { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            PathAndQuery = request.RequestUri?.PathAndQuery;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
