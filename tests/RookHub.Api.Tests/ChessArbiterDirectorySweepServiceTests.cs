using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Kalender des polnischen Verbands (chessarbiter.com) — die ergiebigste Einzelquelle des
/// Projekts: 611 kuenftige Turniere in EINEM Abruf. Der teure Teil ist die Detailseite, und die
/// wird nur fuer noch unbekannte Turniere geholt.
/// </summary>
public class ChessArbiterDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public ChessArbiterDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private RouteHandler _routes = new();

    private ChessArbiterDirectorySweepService CreateService(
        string listJson, string? detailJson = null, int batchSize = 150)
    {
        _routes = new RouteHandler(listJson, detailJson);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TournamentDirectory:ChessArbiterDetailBatchSize"] = batchSize.ToString(),
        }).Build();

        return new ChessArbiterDirectorySweepService(_db, new StubClientFactory(_routes),
            new GeocodingService(_db), config,
            new TestLogger<ChessArbiterDirectorySweepService>());
    }

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private static string Row(string name, string eventId = "291", string year = "2026",
        string? place = "Sosnowica", string? region = "Lubelskie", string? speed = "klasyczne") =>
        $$"""
          {"year":"{{year}}","eventId":"{{eventId}}","name":"{{name}}",
           "startDate":"{{Soon:yyyy-MM-dd}}","place":{{Json(place)}},"region":{{Json(region)}},
           "speedText":{{Json(speed)}},
           "url":"https://www.chessarbiter.com/turnieje/{{year}}/ti_{{eventId}}"}
          """;

    private static string Detail(string? end = null, string? place = "Sosnowica, Ośrodek",
        string? timeControl = "90' + 30'' na ruch", int? rounds = 9, string? system = "swiss",
        int? players = 12) =>
        $$"""
          {"startDate":"{{Soon:yyyy-MM-dd}}","endDate":{{Json(end ?? Soon.AddDays(4).ToString("yyyy-MM-dd"))}},
           "place":{{Json(place)}},"timeControl":{{Json(timeControl)}},
           "rounds":{{(rounds is null ? "null" : rounds.ToString())}},"system":{{Json(system)}},
           "playerCount":{{(players is null ? "null" : players.ToString())}},"organizer":"LWZSzach"}
          """;

    private static string Json(string? value) => value is null ? "null" : $"\"{value}\"";

    // ----- Liste + Detail ---------------------------------------------------

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAddedWithItsDetailPage()
    {
        var result = await CreateService($"[{Row("Memorial Kowalskiego")}]", Detail()).RunAsync();

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Updated);   // Updated zaehlt die gelesenen Detailseiten
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("pl2026-291", entry.PublicId);
        Assert.Equal("POL", entry.Federation);
        Assert.Equal("Lubelskie", entry.State);
        Assert.Equal(Soon.AddDays(4), entry.EndDate);          // nur die Detailseite kennt es
        Assert.Equal("90' + 30'' na ruch", entry.TimeControlText);
        Assert.Equal(9, entry.Rounds);
        Assert.Equal(TournamentSystem.Swiss, entry.System);
        Assert.StartsWith("Sosnowica,", entry.LocationText);
    }

    /// <summary>
    /// Die Besonderheit dieser Quelle: sie nennt die Teilnehmerzahl schon fuer ein GEPLANTES
    /// Turnier. chess-results und FIDE tun das grundsaetzlich nicht.
    /// </summary>
    [Fact]
    public async Task RunAsync_KeepsThePlayerCountOfAPlannedTournament()
    {
        await CreateService($"[{Row("Memorial")}]", Detail(players: 12)).RunAsync();

        Assert.Equal(12, Assert.Single(_db.TournamentDirectoryEntries.ToList()).PlayerCount);
    }

    /// <summary>
    /// Ohne Detailseite bleibt der Eintrag trotzdem stehen — Name, Termin und Ort stehen schon in
    /// der Liste, und ein halber Eintrag ist besser als keiner.
    /// </summary>
    [Fact]
    public async Task RunAsync_DetailPageUnavailable_KeepsTheListEntry()
    {
        var result = await CreateService($"[{Row("Memorial")}]", detailJson: null).RunAsync();

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Updated);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(Soon, entry.StartDate);
        Assert.Equal(Soon, entry.EndDate);
        Assert.Null(entry.Rounds);
    }

    /// <summary>
    /// Der Detailabruf kostet eine Anfrage je Turnier und ist deshalb gedeckelt. Bei 611
    /// Turnieren fuellt sich der Bestand ueber mehrere Naechte statt in einem Schwall.
    /// </summary>
    [Fact]
    public async Task RunAsync_StopsFetchingDetailsWhenTheBudgetIsSpent()
    {
        var rows = string.Join(",", Enumerable.Range(1, 5)
            .Select(i => Row($"Turniej {i}", eventId: i.ToString())));

        var result = await CreateService($"[{rows}]", Detail(), batchSize: 2).RunAsync();

        Assert.Equal(5, result.Added);
        Assert.Equal(2, result.Updated);
        Assert.Equal(2, _routes.DetailCalls);
    }

    /// <summary>
    /// Und beim naechsten Durchgang wird dieselbe Seite nicht noch einmal geholt: „schon gelesen"
    /// steht in der Adresse des Herkunftsvermerks.
    /// </summary>
    [Fact]
    public async Task RunAsync_FetchesEachDetailPageOnlyOnce()
    {
        var json = $"[{Row("Memorial")}]";
        await CreateService(json, Detail()).RunAsync();

        var second = CreateService(json, Detail());
        var result = await second.RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, _routes.DetailCalls);
    }

    /// <summary>
    /// Bleibt die Detailseite beim ersten Mal stumm, wird sie beim naechsten Durchgang wieder
    /// versucht — ein Netzfehler ist kein Urteil ueber die Seite.
    /// </summary>
    [Fact]
    public async Task RunAsync_RetriesADetailPageThatFailedBefore()
    {
        var json = $"[{Row("Memorial")}]";
        await CreateService(json, detailJson: null).RunAsync();

        var result = await CreateService(json, Detail()).RunAsync();

        Assert.Equal(1, result.Updated);
        Assert.Equal(9, Assert.Single(_db.TournamentDirectoryEntries.ToList()).Rounds);
    }

    // ----- Einordnung -------------------------------------------------------

    [Theory]
    [InlineData("klasyczne", TournamentSpeed.Standard)]
    [InlineData("klasyczne FIDE", TournamentSpeed.Standard)]
    [InlineData("szybkie", TournamentSpeed.Rapid)]
    [InlineData("blitz", TournamentSpeed.Blitz)]
    [InlineData("inne", TournamentSpeed.Unknown)]
    [InlineData(null, TournamentSpeed.Unknown)]
    public void SpeedOf_MapsTheSourcesClass(string? text, TournamentSpeed expected) =>
        Assert.Equal(expected, ChessArbiterDirectorySweepService.SpeedOf(text));

    /// <summary>Die Turnierart bleibt unangetastet — die Quelle sagt nichts darueber.</summary>
    [Fact]
    public async Task RunAsync_LeavesTheTournamentKindUntouched()
    {
        await CreateService($"[{Row("Druzynowe mistrzostwa")}]", Detail()).RunAsync();

        Assert.Equal(TournamentKind.Unknown,
            Assert.Single(_db.TournamentDirectoryEntries.ToList()).Kind);
    }

    /// <summary>Holt die Turniersuche das Turnier ein, geht der eigene Eintrag in ihrem auf.</summary>
    [Fact]
    public async Task RunAsync_RetiresItsOwnEntryOnceTheSearchCatchesUp()
    {
        _db.TournamentDirectoryEntries.AddRange(
            new TournamentDirectoryEntry
            {
                PublicId = "pl2026-291", Name = "Memorial Kowalskiego Warszawa",
                Federation = "POL", StartDate = Soon, EndDate = Soon,
            },
            new TournamentDirectoryEntry
            {
                PublicId = "1499999", ChessResultsId = "1499999",
                Name = "Memorial Kowalskiego Warszawa", Federation = "POL",
                StartDate = Soon, EndDate = Soon,
            });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("Memorial Kowalskiego Warszawa")}]", Detail())
            .RunAsync();

        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.Retired);
        Assert.NotNull(_db.TournamentDirectoryEntries.Single(e => e.PublicId == "pl2026-291").RemovedAt);
        // Und die Detailseite wird fuer ein Turnier, das chess-results schon fuehrt, gar nicht geholt.
        Assert.Equal(0, _routes.DetailCalls);
    }

    [Fact]
    public async Task RunAsync_NotesWhereTheEntryCameFrom()
    {
        await CreateService($"[{Row("Memorial")}]", Detail()).RunAsync();

        var source = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(DirectorySourceKind.PolishChessFederation, source.Kind);
        Assert.Equal("2026/ti_291", source.ExternalId);
        Assert.Equal("https://www.chessarbiter.com/turnieje/2026/ti_291", source.Url);
    }

    [Fact]
    public async Task RunAsync_CrawlerFailure_Throws() =>
        await Assert.ThrowsAsync<RookHub.Api.Exceptions.CrawlerRequestException>(
            () => CreateService("kaputt").RunAsync());

    // ----- Testdoubles ------------------------------------------------------

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
    }

    /// <summary>Zwei Routen, ein Handler: die Liste und die Detailseite.</summary>
    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly string _list;
        private readonly string? _detail;

        public RouteHandler(string list = "[]", string? detail = null)
        {
            _list = list;
            _detail = detail;
        }

        public int DetailCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/detail", StringComparison.Ordinal))
            {
                DetailCalls++;
                return Task.FromResult(_detail is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : Ok(_detail));
            }

            return Task.FromResult(_list == "kaputt"
                ? new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("") }
                : Ok(_list));
        }

        private static HttpResponseMessage Ok(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
