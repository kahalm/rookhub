using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Kalender des slowenischen Verbands (SZS) — 78 kuenftige Turniere gegen 7 auf
/// chess-results, und im Rueckblick erscheinen 41 % dort NIE.
/// </summary>
public class SzsDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public SzsDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private SzsDirectorySweepService CreateService(string json,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(_db, new StubClientFactory(new StubHandler(json, status)),
            new GeocodingService(_db), new TestLogger<SzsDirectorySweepService>());

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private static string Row(string name, string eventId = "5784", string? zip = "1410",
        string? place = "Zagorje ob Savi", bool cancelled = false) =>
        $$"""
          {"eventId":"{{eventId}}","name":"{{name}}","date":"{{Soon:yyyy-MM-dd}}",
           "postalCode":{{(zip is null ? "null" : $"\"{zip}\"")}},
           "place":{{(place is null ? "null" : $"\"{place}\"")}},
           "cancelled":{{(cancelled ? "true" : "false")}}}
          """;

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAdded()
    {
        var result = await CreateService($"[{Row("Odprti turnir Buce 2026")}]").RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("sl5784", entry.PublicId);
        Assert.Equal("SLO", entry.Federation);
        Assert.Null(entry.ChessResultsId);
        // Die Liste nennt EINEN Tag; mehrtaegige Turniere stehen dort als eigene Zeile je Tag.
        Assert.Equal(Soon, entry.StartDate);
        Assert.Equal(Soon, entry.EndDate);
    }

    /// <summary>
    /// „PLZ Ort" — dieselbe Form, in der chess-results seine Ortstexte schreibt, und auf die der
    /// PLZ-Weg des Geocoders ausgelegt ist (er verlangt zusaetzlich, dass der Ortsname im Text
    /// vorkommt).
    /// </summary>
    [Theory]
    [InlineData("1410", "Zagorje ob Savi", "1410 Zagorje ob Savi")]
    [InlineData(null, "Zagorje ob Savi", "Zagorje ob Savi")]
    [InlineData("1410", null, "1410")]
    [InlineData(null, null, null)]
    public void LocationOf_PutsThePostalCodeFirst(string? zip, string? place, string? expected) =>
        Assert.Equal(expected, SzsDirectorySweepService.LocationOf(
            new SzsDirectorySweepService.CrawlerSzsEvent("1", "n", Soon, zip, place, false)));

    /// <summary>
    /// Der Sonderwert dieser Quelle: die Postleitzahl steht in der Trefferliste, die Verortung
    /// braucht also keinen Abruf je Turnier — und sie ist der genaueste Weg des Geocoders.
    /// </summary>
    [Fact]
    public async Task RunAsync_GeocodesFromThePostalCodeInTheList()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "SI", PostalCode = "1410", Name = "Zagorje ob Savi",
            NameNormalized = "zagorje ob savi", Lat = 46.13, Lon = 15.00,
            Kind = GeoPlaceKind.PostalCode, Population = 0,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("Odprti turnir")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("1410 Zagorje ob Savi", entry.LocationText);
        Assert.Equal(GeoSource.PostalCode, entry.GeoSource);
        Assert.Equal(46.13, entry.Lat!.Value, 2);
    }

    /// <summary>
    /// Ein ABGESAGTES Turnier wird nicht angelegt — die Quelle kennzeichnet Absagen nur im Namen,
    /// und ein abgesagtes Turnier im Kalender ist schlechter als keins: man faehrt hin.
    /// </summary>
    [Fact]
    public async Task RunAsync_CancelledTournament_IsNotAdded()
    {
        var result = await CreateService($"[{Row("ODPADE; Gurman 2026", cancelled: true)}]").RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Empty(_db.TournamentDirectoryEntries.ToList());
    }

    /// <summary>Und wird ein bereits angelegtes Turnier abgesagt, verschwindet es wieder.</summary>
    [Fact]
    public async Task RunAsync_TournamentCancelledLater_IsRetired()
    {
        await CreateService($"[{Row("Gurman 2026 - 36")}]").RunAsync();
        var own = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(own.RemovedAt);

        var result = await CreateService($"[{Row("ODPADE; Gurman 2026 - 36", cancelled: true)}]").RunAsync();

        Assert.Equal(1, result.Retired);
        await _db.Entry(own).ReloadAsync();
        Assert.NotNull(own.RemovedAt);
    }

    [Fact]
    public async Task RunAsync_KnownTournament_IsOnlyNoted()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1490241", ChessResultsId = "1490241", Federation = "SLO",
            Name = "Odprti turnir Buce 2026", StartDate = Soon, EndDate = Soon,
        });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("Odprti turnir Buce")}]").RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Matched);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.Include(e => e.Sources).ToList());
        Assert.Contains(entry.Sources, s => s.Kind == DirectorySourceKind.SlovenianChessFederation);
    }

    [Fact]
    public async Task RunAsync_TwiceInARow_AddsOnlyOnce()
    {
        var json = $"[{Row("Odprti turnir Buce 2026")}]";
        await CreateService(json).RunAsync();
        var second = await CreateService(json).RunAsync();

        Assert.Equal(0, second.Added);
        Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Single(_db.TournamentDirectorySources.ToList());
    }

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
    }

    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
