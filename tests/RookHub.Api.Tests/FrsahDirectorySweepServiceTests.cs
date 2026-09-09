using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Kalender des rumaenischen Verbands (FRSah): 31 kuenftige Turniere, rund ein Drittel davon
/// nicht auf chess-results — darunter die kompletten nationalen Mannschaftsligen, die nie eine
/// Swiss-Manager-Datei hochladen. Anders als beim englischen Kalender liefert diese Quelle keine
/// eigenen Koordinaten: die Verortung geht immer ueber das Ortslexikon.
/// </summary>
public class FrsahDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public FrsahDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private FrsahDirectorySweepService CreateService(string json,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(_db, new StubClientFactory(new StubHandler(json, status)),
            new GeocodingService(_db), new TestLogger<FrsahDirectorySweepService>());

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private static string Row(string name, string eventId = "41501",
        string? place = "CATTIA, Strada Institutului 35, Brasov", string? city = null,
        DateOnly? end = null) =>
        $$"""
          {"eventId":"{{eventId}}","name":"{{name}}",
           "startDate":"{{Soon:yyyy-MM-dd}}","endDate":"{{(end ?? Soon):yyyy-MM-dd}}",
           "url":"https://frsah.ro/event/{{eventId}}/",
           "place":{{Json(place)}},"city":{{Json(city)}},"postalCode":null,"country":"Romania"}
          """;

    private static string Json(string? v) => v is null ? "null" : $"\"{v}\"";

    // ----- Anlegen -------------------------------------------------------------

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAdded()
    {
        var result = await CreateService($"[{Row("Cupa Diosig 2026")}]").RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("ro41501", entry.PublicId);
        Assert.Equal("ROU", entry.Federation);
        Assert.Equal(Soon, entry.StartDate);
    }

    /// <summary>
    /// Anders als der englische Kalender liefert diese Quelle KEINE Koordinaten — jede Verortung
    /// laeuft ueber das Ortslexikon.
    /// </summary>
    [Fact]
    public async Task RunAsync_ResolvesTheLocationViaTheGazetteer()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "RO", Name = "Brasov", NameNormalized = "brasov",
            Lat = 45.6427, Lon = 25.5887, Kind = GeoPlaceKind.City, Population = 250_000,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("Cupa Diosig 2026")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(GeoSource.City, entry.GeoSource);
        Assert.Equal(45.6427, entry.Lat!.Value, 3);
    }

    /// <summary>Eine von HAND gesetzte Koordinate bleibt unangetastet.</summary>
    [Fact]
    public async Task RunAsync_LeavesAManualCoordinateAlone()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "ro41501", Name = "Cupa Diosig 2026", Federation = "ROU",
            StartDate = Soon, EndDate = Soon, LocationText = "CATTIA, Strada Institutului 35, Brasov",
            Lat = 1.0, Lon = 2.0, GeoSource = GeoSource.Manual,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("Cupa Diosig 2026")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(GeoSource.Manual, entry.GeoSource);
        Assert.Equal(1.0, entry.Lat!.Value, 4);
    }

    /// <summary>
    /// Die nationalen Mannschaftsligen haben KEINEN festen Austragungsort — das Turnier wird
    /// trotzdem angelegt (nur ohne Ortstext und ohne Pin), es findet ja statt.
    /// </summary>
    [Fact]
    public async Task RunAsync_TournamentWithoutAVenue_IsAddedWithoutLocation()
    {
        var result = await CreateService(
            $"[{Row("Campionatul Național pe echipe – Divizia A", place: null)}]").RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(entry.LocationText);
        Assert.Null(entry.Lat);
    }

    // ----- Zuordnung und Herkunft -----------------------------------------------

    [Fact]
    public async Task RunAsync_RetiresItsOwnEntryOnceTheSearchCatchesUp()
    {
        _db.TournamentDirectoryEntries.AddRange(
            new TournamentDirectoryEntry
            {
                PublicId = "ro41501", Name = "Cupa Diosig 2026", Federation = "ROU",
                StartDate = Soon, EndDate = Soon,
            },
            new TournamentDirectoryEntry
            {
                PublicId = "1490100", ChessResultsId = "1490100", Name = "Cupa Diosig 2026",
                Federation = "ROU", StartDate = Soon, EndDate = Soon,
            });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("Cupa Diosig 2026")}]").RunAsync();

        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.Retired);
        Assert.NotNull(_db.TournamentDirectoryEntries.Single(e => e.PublicId == "ro41501").RemovedAt);
    }

    [Fact]
    public async Task RunAsync_NotesWhereTheEntryCameFrom()
    {
        await CreateService($"[{Row("Cupa Diosig 2026")}]").RunAsync();

        var source = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(DirectorySourceKind.RomanianChessFederation, source.Kind);
        Assert.Equal("41501", source.ExternalId);
        Assert.Equal("https://frsah.ro/event/41501/", source.Url);
    }

    [Fact]
    public async Task RunAsync_CrawlerFailure_Throws() =>
        await Assert.ThrowsAsync<RookHub.Api.Exceptions.CrawlerRequestException>(
            () => CreateService("kaputt", HttpStatusCode.BadGateway).RunAsync());

    [Fact]
    public async Task RunAsync_NoEvents_ReturnsAnEmptyResult()
    {
        var result = await CreateService("[]").RunAsync();

        Assert.Equal(0, result.Read);
        Assert.Empty(_db.TournamentDirectoryEntries.ToList());
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
