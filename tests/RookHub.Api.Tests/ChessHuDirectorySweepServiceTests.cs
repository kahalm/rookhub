using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Kalender des ungarischen Verbands (chess.hu). Sein Ertrag ist VORLAUF: ab November 2026
/// fuehrt er 41 Turniere, wo chess-results 5 kennt; im Rueckblick landen 80 % irgendwann doch
/// dort, 20 % nie.
/// </summary>
public class ChessHuDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public ChessHuDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private ChessHuDirectorySweepService CreateService(string json,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(_db, new StubClientFactory(new StubHandler(json, status)),
            new GeocodingService(_db), new TestLogger<ChessHuDirectorySweepService>());

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private static string Row(string name, string eventId = "74262", string? place = "Siklós",
        bool hasVenue = true, DateOnly? end = null) =>
        $$"""
          {"eventId":"{{eventId}}","name":"{{name}}",
           "startDate":"{{Soon:yyyy-MM-dd}}","endDate":"{{(end ?? Soon):yyyy-MM-dd}}",
           "place":{{(place is null ? "null" : $"\"{place}\"")}},
           "hasVenue":{{(hasVenue ? "true" : "false")}},"fideRated":true}
          """;

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAdded()
    {
        var result = await CreateService($"[{Row("49. Tenkes Kupa Nemzetközi Sakkverseny")}]").RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("hu74262", entry.PublicId);
        Assert.Equal("HUN", entry.Federation);
        Assert.Null(entry.ChessResultsId);
        Assert.Equal("Siklós", entry.LocationText);
    }

    /// <summary>
    /// Die Quelle nennt keine Bedenkzeit, keine Rundenzahl und kein System — und es wird auch
    /// nichts dazuerfunden. Was sie liefert, sind Name, Termin und Ort.
    /// </summary>
    [Fact]
    public async Task RunAsync_LeavesTheFieldsTheSourceDoesNotKnowEmpty()
    {
        await CreateService($"[{Row("Terézváros Open, 2026")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(entry.TimeControlText);
        Assert.Null(entry.Rounds);
        Assert.Equal(TournamentSpeed.Unknown, entry.Speed);
        Assert.Equal(TournamentSystem.Unknown, entry.System);
        Assert.Equal(TournamentKind.Unknown, entry.Kind);
    }

    /// <summary>
    /// Der Ortsname allein genuegt in Ungarn: von 52 verschiedenen Ortsnamen des Kalenders stehen
    /// 46 im Lexikon, und nur drei davon mehrdeutig (innerhalb derselben Stadt). Genau deshalb
    /// wird die Detailseite gar nicht erst geholt.
    /// </summary>
    [Fact]
    public async Task RunAsync_GeocodesFromThePlaceName()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "HU", Name = "Siklós", NameNormalized = "siklos",
            Lat = 45.855, Lon = 18.298, Kind = GeoPlaceKind.City, Population = 9000,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("Tenkes Kupa")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(GeoSource.City, entry.GeoSource);
        Assert.Equal(45.855, entry.Lat!.Value, 3);
    }

    /// <summary>
    /// „Online" und „Helyszín később" („Ort spaeter") stehen im ORTS-Feld, sind aber keine Orte.
    /// Ein solcher Eintrag bekommt weder Ortstext noch Pin — sonst behauptete die Karte einen
    /// Spielort, den niemand kennt.
    /// </summary>
    [Fact]
    public async Task RunAsync_PlaceholderVenue_GetsNoLocationAndNoPin()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "HU", Name = "Online", NameNormalized = "online",
            Lat = 47.5, Lon = 19.0, Kind = GeoPlaceKind.City, Population = 100,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("Sakkmatyi Online Grand Prix", place: "Online", hasVenue: false)}]")
            .RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(entry.LocationText);
        Assert.Null(entry.Lat);
    }

    /// <summary>
    /// Der Regelfall dieser Quelle: sie kennt das Turnier Monate vor chess-results. Holt die
    /// Turniersuche es ein, geht der eigene Eintrag in ihrem auf statt als Dublette stehen zu
    /// bleiben.
    /// </summary>
    [Fact]
    public async Task RunAsync_RetiresItsOwnEntryOnceTheSearchCatchesUp()
    {
        _db.TournamentDirectoryEntries.AddRange(
            new TournamentDirectoryEntry
            {
                PublicId = "hu74262", ChessResultsId = null, Name = "Tenkes Kupa Nemzetkozi",
                Federation = "HUN", StartDate = Soon, EndDate = Soon,
            },
            new TournamentDirectoryEntry
            {
                PublicId = "1490000", ChessResultsId = "1490000", Name = "Tenkes Kupa Nemzetkozi",
                Federation = "HUN", StartDate = Soon, EndDate = Soon,
            });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("Tenkes Kupa Nemzetkozi")}]").RunAsync();

        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.Retired);
        Assert.NotNull(_db.TournamentDirectoryEntries.Single(e => e.PublicId == "hu74262").RemovedAt);
    }

    /// <summary>
    /// Die Jugendklasse kommt aus dem NAMEN — das Jugend-Merkmal der Quelle steht bei 77 von 107
    /// Turnieren auf „ja", darunter ein Gaensefest, und wird deshalb gar nicht erst uebertragen.
    /// </summary>
    [Fact]
    public async Task RunAsync_ClassifiesTheAudienceFromTheName()
    {
        await CreateService($"""
            [{Row("Ifjúsági Rapid Grand Prix Hédervár", eventId: "76246")},
             {Row("Félegyházi Libafesztivál", eventId: "76247")}]
            """).RunAsync();

        var youth = _db.TournamentDirectoryEntries.Single(e => e.PublicId == "hu76246");
        var open = _db.TournamentDirectoryEntries.Single(e => e.PublicId == "hu76247");
        Assert.Equal(TournamentAgeGroups.YouthUnspecified, youth.AgeGroups);
        Assert.Equal(TournamentAgeGroups.None, open.AgeGroups);
    }

    [Fact]
    public async Task RunAsync_NotesWhereTheEntryCameFrom()
    {
        await CreateService($"[{Row("Tenkes Kupa")}]").RunAsync();

        var source = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(DirectorySourceKind.HungarianChessFederation, source.Kind);
        Assert.Equal("74262", source.ExternalId);
    }

    /// <summary>Ein Ausfall des Crawlers darf nicht still zu „keine Turniere" werden.</summary>
    [Fact]
    public async Task RunAsync_CrawlerFailure_Throws() =>
        await Assert.ThrowsAsync<RookHub.Api.Exceptions.CrawlerRequestException>(
            () => CreateService("kaputt", HttpStatusCode.BadGateway).RunAsync());

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
