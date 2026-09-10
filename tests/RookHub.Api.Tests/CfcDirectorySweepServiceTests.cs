using System.Net;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Ankuendigungskalender der Chess Federation of Canada. Eine einzige Foederation (CAN), 171
/// gemessene kuenftige Turniere gegen 68 auf chess-results — und keine stabile Kennung, weshalb
/// die eigene aus Termin, Ort UND Namen gebildet wird. Anders als in Wales muss der Name hinein:
/// diese Quelle fuehrt keine Anschrift, und zwei Turniere am selben Tag in derselben Stadt sind
/// dort gemessen dreizehn Mal vorgekommen.
/// </summary>
public class CfcDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public CfcDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private CfcDirectorySweepService CreateService(string json,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(_db, new StubClientFactory(new StubHandler(json, status)),
            new GeocodingService(_db), new TestLogger<CfcDirectorySweepService>());

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private static string Row(string name, string? place = "Burlington", string? prov = "ON",
        string? eventId = null, DateOnly? end = null) =>
        $$"""
          {"eventId":"{{eventId ?? $"{Soon:yyyy-MM-dd}|{Soon:yyyy-MM-dd}|{(place ?? "").ToLowerInvariant()}|{name.ToLowerInvariant()}"}}",
           "name":"{{name}}",
           "startDate":"{{Soon:yyyy-MM-dd}}","endDate":"{{(end ?? Soon):yyyy-MM-dd}}",
           "place":{{Json(place)}},"province":{{Json(prov)}},
           "url":"https://www.chess.ca/en/events/"}
          """;

    private static string Json(string? value) => value is null ? "null" : $"\"{value}\"";

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAddedWithProvinceAsState()
    {
        var result = await CreateService($"[{Row("Brantford Rapid Open", place: "Brantford")}]").RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("CAN", entry.Federation);
        Assert.Equal("Brantford Rapid Open", entry.Name);
        Assert.Equal("Brantford", entry.LocationText);
        // Ohne Anschrift ist das Provinz-Kuerzel der einzige Rueckfall auf die Regionsmitte.
        Assert.Equal("ON", entry.State);
        Assert.Null(entry.ChessResultsId);
    }

    /// <summary>
    /// Zwei Turniere am selben Tag in derselben Stadt — gemessen dreizehn Mal im echten
    /// Datensatz. Sie muessen ZWEI Eintraege bleiben.
    /// </summary>
    [Fact]
    public async Task RunAsync_TwoTournamentsSameDaySameCity_StayTwoEntries()
    {
        var json = $"[{Row("Maverick OCC & CYCC Double Qualifiers", place: "Markham")},"
                   + $"{Row("Toronto Youth Circuit", place: "Markham")}]";

        var result = await CreateService(json).RunAsync();

        Assert.Equal(2, result.Added);
        Assert.Equal(2, _db.TournamentDirectoryEntries.Count());
    }

    /// <summary>
    /// Zwei VERSCHIEDENE Quellen-Schluessel duerfen nicht auf dieselbe Kurz-Id fallen, und der
    /// Kurzwert muss in die 24 Zeichen der Spalte passen. Der rohe Schluessel ist deutlich
    /// laenger — daran scheiterten Wales und Deutschland monatelang jede Nacht.
    /// </summary>
    [Fact]
    public void PublicIdOf_FitsTheColumnAndKeepsDifferentKeysApart()
    {
        var a = CfcDirectorySweepService.PublicIdOf("2026-09-20|2026-09-20|markham|maverick occ");
        var b = CfcDirectorySweepService.PublicIdOf("2026-09-20|2026-09-20|markham|toronto youth circuit");

        Assert.NotEqual(a, b);
        Assert.True(a.Length <= 24, a);
        Assert.StartsWith("ca", a);
        Assert.Equal(a, CfcDirectorySweepService.PublicIdOf("2026-09-20|2026-09-20|markham|maverick occ"));
    }

    [Theory]
    [InlineData("Burlington", true)]
    [InlineData("Thornhill / Toronto", true)]
    [InlineData("TBC", false)]
    [InlineData("tba", false)]
    [InlineData("Venue to be announced", false)]
    [InlineData("(details to follow)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasVenue_SeparatesRealPlacesFromPlaceholders(string? place, bool expected) =>
        Assert.Equal(expected, CfcDirectorySweepService.HasVenue(place));

    [Fact]
    public async Task RunAsync_PlaceholderVenue_GetsNoLocationAndNoPin()
    {
        await CreateService($"[{Row("Some Open", place: "TBC")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(entry.LocationText);
        Assert.Null(entry.Lat);
    }

    /// <summary>
    /// Die Quelle fuehrt keine Postleitzahl — verortet wird ueber den Ortsnamen, mit der Provinz
    /// als zusaetzlichem Unterscheider.
    /// </summary>
    [Fact]
    public async Task RunAsync_GeocodesFromTheCityName()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "CA", Name = "Burlington", NameNormalized = "burlington",
            NameTranscribed = "burlington",
            Lat = 43.3255, Lon = -79.7990, Kind = GeoPlaceKind.City, Population = 186000,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("BCC Late Summer Tournament", place: "Burlington")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(43.3255, entry.Lat!.Value, 3);
    }

    /// <summary>
    /// Ein bestehender chess-results-Eintrag zum selben Turnier gewinnt: die Quelle vermerkt nur
    /// ihre Herkunft und legt keinen zweiten Eintrag an.
    /// </summary>
    [Fact]
    public async Task RunAsync_KnownFromChessResults_IsOnlyNoted()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1234567", ChessResultsId = "1234567", Federation = "CAN",
            Name = "Maverick Kingsway Qualifier", LocationText = "Markham",
            StartDate = Soon, EndDate = Soon, FirstSeenAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await CreateService(
            $"[{Row("Maverick Kingsway Qualifier", place: "Markham")}]").RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Matched);
        Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Contains(_db.TournamentDirectorySources.ToList(),
            s => s.Kind == DirectorySourceKind.CanadianChessFederation);
    }

    /// <summary>
    /// <b>Eine bewusst hingenommene Doppelung, festgenagelt damit sie niemanden ueberrascht.</b>
    /// Der gemeinsame Abgleich verlangt ZWEI unterscheidende Woerter; „Brantford Rapid Open"
    /// behaelt nach dem Fuellwortfilter nur „brantford" (rapid und open stehen in der Liste) und
    /// trifft deshalb NIE einen chess-results-Eintrag — auch nicht den identischen. Gemessen am
    /// echten kanadischen Datensatz: <b>23 von 157 Turnieren (15 %)</b> sind so gebaut
    /// („Brantford Rapid Open", „Vancouver Chess Festival #15", „Toronto Junior Chess
    /// Championship").
    ///
    /// <para>Das ist die RICHTIGE Seite des Fehlers: die Regel ist so streng, weil ihre Lockerung
    /// am 2026-09-09 drei echte italienische Turniere einem Bozener Vereinsturnier zugeschlagen
    /// und aus dem Verzeichnis entfernt hat. Zwei Eintraege fuer ein Turnier sind sichtbar und
    /// beide fuer sich richtig; ein verschmolzenes Turnier ist unsichtbar. Wer das aendern will,
    /// aendert die geteilte Regel und braucht dafuer die italienische Gegenprobe.</para>
    /// </summary>
    [Fact]
    public async Task RunAsync_NameWithOneDistinctiveWord_GetsItsOwnEntry()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1234567", ChessResultsId = "1234567", Federation = "CAN",
            Name = "Brantford Rapid Open", LocationText = "Brantford",
            StartDate = Soon, EndDate = Soon, FirstSeenAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("Brantford Rapid Open", place: "Brantford")}]").RunAsync();

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Matched);
        Assert.Equal(2, _db.TournamentDirectoryEntries.Count());
    }

    /// <summary>
    /// Ein Fehler des Crawlers darf NICHT als leerer Kalender durchgehen — sonst zoege der Lauf
    /// jedes kanadische Turnier zurueck.
    /// </summary>
    [Fact]
    public async Task RunAsync_CrawlerError_Throws()
    {
        await Assert.ThrowsAnyAsync<Exception>(
            () => CreateService("boom", HttpStatusCode.BadGateway).RunAsync());
    }

    /// <summary>
    /// Eine Zeile ohne lesbaren Termin gilt trotzdem als GELIEFERT. Sonst haette ein geaendertes
    /// Datumsformat nicht eine Zeile getroffen, sondern alle — und nach zwei Laeufen waere der
    /// ganze kanadische Bestand abgesagt.
    /// </summary>
    [Fact]
    public async Task RunAsync_RowWithoutUsableDate_IsStillCountedAsDelivered()
    {
        var existing = new TournamentDirectoryEntry
        {
            PublicId = CfcDirectorySweepService.PublicIdOf("kaputt"),
            ChessResultsId = null, Federation = "CAN", Name = "Alt",
            StartDate = Soon, EndDate = Soon, FirstSeenAt = DateTime.UtcNow,
        };
        _db.TournamentDirectoryEntries.Add(existing);
        _db.TournamentDirectorySources.Add(new TournamentDirectorySource
        {
            Entry = existing,
            Kind = DirectorySourceKind.CanadianChessFederation,
            ExternalId = CfcDirectorySweepService.PublicIdOf("kaputt"),
            FirstSeenAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var json = """
                   [{"eventId":"kaputt","name":"Alt","startDate":"nicht-lesbar",
                     "endDate":"nicht-lesbar","place":"Toronto","province":"ON","url":null}]
                   """;
        await CreateService(json).RunAsync();

        Assert.Null(_db.TournamentDirectoryEntries.Single().RemovedAt);
    }

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler") };
    }

    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
