using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Saisonkalender der Welsh Chess Union. Eine einzige Foederation (WLS), 38 gemessene
/// Turniere, keines davon eine Sitzung oder ein Lehrgang — und keine stabile Kennung, weshalb die
/// eigene aus Termin und Anschrift gebildet wird (siehe <see cref="WcuDirectorySweepService.PublicIdOf"/>).
/// </summary>
public class WcuDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public WcuDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private WcuDirectorySweepService CreateService(string json,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(_db, new StubClientFactory(new StubHandler(json, status)),
            new GeocodingService(_db), new TestLogger<WcuDirectorySweepService>());

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private static string Row(string name, string eventId = "2026-11-01|2026-11-01|best western heronston hotel, bridgend",
        string? place = "Best Western Heronston Hotel, Ewenny Road, Bridgend CF355AW",
        DateOnly? end = null) =>
        $$"""
          {"eventId":"{{eventId}}","name":"{{name}}",
           "startDate":"{{Soon:yyyy-MM-dd}}","endDate":"{{(end ?? Soon):yyyy-MM-dd}}",
           "place":{{Json(place)}},
           "url":"https://www.welshchessunion.uk/calendar/"}
          """;

    private static string Json(string? value) => value is null ? "null" : $"\"{value}\"";

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAdded()
    {
        var result = await CreateService($"[{Row("Welsh Championship")}]").RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("WLS", entry.Federation);
        Assert.Equal("Welsh Championship", entry.Name);
        Assert.Equal("Best Western Heronston Hotel, Ewenny Road, Bridgend CF355AW", entry.LocationText);
    }

    /// <summary>
    /// Diese Quelle liefert keine Nummer und keinen Slug — die Kennung des Crawlers ist bereits
    /// ein aus Termin und Anschrift gebildeter Schluessel. Zwei VERSCHIEDENE solcher Schluessel
    /// duerfen nicht auf dieselbe Kurz-Id fallen.
    /// </summary>
    [Fact]
    public void PublicIdOf_FitsTheColumnAndKeepsDifferentKeysApart()
    {
        var a = WcuDirectorySweepService.PublicIdOf(
            "2026-11-01|2026-11-01|best western heronston hotel, bridgend");
        var b = WcuDirectorySweepService.PublicIdOf(
            "2026-11-01|2026-11-01|bridgend ravens rfc, tondu, bridgend");

        Assert.NotEqual(a, b);
        Assert.True(a.Length <= 24, a);
        Assert.StartsWith("wl", a);
        Assert.Equal(a, WcuDirectorySweepService.PublicIdOf(
            "2026-11-01|2026-11-01|best western heronston hotel, bridgend"));
    }

    /// <summary>Ein Turnier ohne feststehende Ausschreibung nennt einen Platzhalter statt einer Anschrift.</summary>
    [Theory]
    [InlineData("Best Western Heronston Hotel, Bridgend CF355AW", true)]
    [InlineData("(More details to follow)", false)]
    [InlineData("TBC", false)]
    [InlineData("tba", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasVenue_SeparatesRealAddressesFromPlaceholders(string? place, bool expected) =>
        Assert.Equal(expected, WcuDirectorySweepService.HasVenue(place));

    [Fact]
    public async Task RunAsync_PlaceholderVenue_GetsNoLocationAndNoPin()
    {
        await CreateService(
            $"[{Row("1st Pembrokeshire Chess Club Junior Festival", place: "(More details to follow)")}]")
            .RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(entry.LocationText);
        Assert.Null(entry.Lat);
    }

    /// <summary>
    /// Das GB-Ortslexikon fuehrt nachgemessen nur Staedte, keine Postleitzahlen — verortet wird
    /// deshalb ueber den Ortsnamen, nicht ueber die britische Postleitzahl im Ortstext.
    /// </summary>
    [Fact]
    public async Task RunAsync_GeocodesFromTheCityName()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "GB", Name = "Bridgend", NameNormalized = "bridgend",
            Lat = 51.5044, Lon = -3.5776, Kind = GeoPlaceKind.City, Population = 40000,
        });
        await _db.SaveChangesAsync();

        await CreateService(
            $"[{Row("Welsh Championship", place: "Best Western Heronston Hotel, Bridgend")}]")
            .RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(51.5044, entry.Lat!.Value, 3);
    }

    [Fact]
    public async Task RunAsync_NotesWhereTheEntryCameFrom()
    {
        await CreateService($"[{Row("Welsh Championship")}]").RunAsync();

        var source = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(DirectorySourceKind.WelshChessUnion, source.Kind);
        // Der KURZSCHLUESSEL, nicht der rohe Quellen-Schluessel: der ist hier 61 Zeichen lang und
        // passt nicht in die Spalte (60). Diese Zusicherung stand vorher auf dem rohen Wert und war
        // gruen — die InMemory-Datenbank prueft keine Spaltenlaengen, gegen MariaDB scheiterte die
        // Quelle dagegen JEDE Nacht mit „Data too long for column".
        // Dieser Schluessel ist mit genau 60 Zeichen haarscharf noch durchgegangen — deshalb fiel
        // der Fehler hier nie auf. Eine Zeile mehr Anschrift, und die Quelle scheiterte.
        var raw = "2026-11-01|2026-11-01|best western heronston hotel, bridgend";
        Assert.Equal(ExternalDirectorySource.MaxExternalIdLength, raw.Length);
        Assert.Equal(WcuDirectorySweepService.PublicIdOf(raw), source.ExternalId);
        Assert.True(source.ExternalId.Length <= ExternalDirectorySource.MaxExternalIdLength);
        Assert.Equal("https://www.welshchessunion.uk/calendar/", source.Url);
    }

    /// <summary>
    /// Kennt chess-results das Turnier schon (Termin + zwei unterscheidende Woerter), wird es
    /// zugeordnet statt neu angelegt. "Championship" allein reicht nicht — das Wort ist als
    /// Fuellwort herausgefiltert (<see cref="FideDirectorySweepService.DistinctiveWords"/>),
    /// deshalb hier ein Name mit zwei echten Unterscheidern ("newport", "congress").
    /// </summary>
    [Fact]
    public async Task RunAsync_MatchesAnExistingChessResultsEntry()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1490001", ChessResultsId = "1490001", Name = "Newport Congress 2026",
            Federation = "WLS", StartDate = Soon, EndDate = Soon.AddDays(3),
        });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("Newport Congress")}]").RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Matched);
        Assert.Single(_db.TournamentDirectoryEntries.ToList());
    }

    /// <summary>Derselbe Durchgang zweimal ausgefuehrt legt das Turnier nicht doppelt an.</summary>
    [Fact]
    public async Task RunAsync_IsIdempotent()
    {
        var json = $"[{Row("Welsh Championship")}]";
        await CreateService(json).RunAsync();
        var second = await CreateService(json).RunAsync();

        Assert.Equal(0, second.Added);
        Assert.Single(_db.TournamentDirectoryEntries.ToList());
    }

    /// <summary>
    /// Zwei Turniere auf demselben Datum (WCPL/WJCPL-Ligastart) mit verschiedener Anschrift werden
    /// zu zwei EIGENSTAENDIGEN Eintraegen — nicht zu einem, das sich beim zweiten Durchgang
    /// gegenseitig ueberschreibt.
    /// </summary>
    [Fact]
    public async Task RunAsync_TwoTournamentsOnTheSameDay_BecomeTwoEntries()
    {
        var wcpl = Row("WCPL and U1850 League Round 1",
            eventId: "2026-11-01|2026-11-01|best western heronston hotel, bridgend",
            place: "Best Western Heronston Hotel, Ewenny Road, Bridgend CF239XF");
        var wjcpl = Row("WJCPL Round 1",
            eventId: "2026-11-01|2026-11-01|bridgend ravens rfc, tondu, bridgend",
            place: "Bridgend Ravens RFC, Brewery Field, Tondu, Bridgend CF314JE");

        var result = await CreateService($"[{wcpl},{wjcpl}]").RunAsync();

        Assert.Equal(2, result.Added);
        Assert.Equal(2, _db.TournamentDirectoryEntries.Count());
    }

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
