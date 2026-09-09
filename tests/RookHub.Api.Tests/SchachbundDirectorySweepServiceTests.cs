using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Turnierdatenbank des Deutschen Schachbunds. Ihr Wert ist nicht die Menge, sondern die ART
/// der Turniere: ein reines Meldesystem ohne Ergebnismeldung, in dem Vereins-Abendturniere,
/// Fernschach, Problemschach und Schach960 stehen.
/// </summary>
public class SchachbundDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public SchachbundDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private SchachbundDirectorySweepService CreateService(string json,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(_db, new StubClientFactory(new StubHandler(json, status)),
            new GeocodingService(_db), new TestLogger<SchachbundDirectorySweepService>());

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private static string Row(string name, string eventId = "ccm-rapidturnier-2026",
        string region = "bayern", string? place = "Caissa Center München, Frankfurter Ring 193a, 80807 München",
        string? timeControl = "12 Minuten + 3 Sekunden/Zug", int? rounds = 5,
        string? system = "swiss", DateOnly? end = null) =>
        $$"""
          {"eventId":"{{eventId}}","name":"{{name}}",
           "startDate":"{{Soon:yyyy-MM-dd}}","endDate":"{{(end ?? Soon):yyyy-MM-dd}}",
           "region":"{{region}}","place":{{Json(place)}},"timeControl":{{Json(timeControl)}},
           "rounds":{{(rounds is null ? "null" : rounds.ToString())}},"system":{{Json(system)}},
           "url":"https://www.schachbund.de/turnierdetails/{{eventId}}.html"}
          """;

    private static string Json(string? value) => value is null ? "null" : $"\"{value}\"";

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAdded()
    {
        var result = await CreateService($"[{Row("CCM Monatliches Rapidturnier")}]").RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("GER", entry.Federation);
        Assert.Equal(5, entry.Rounds);
        Assert.Equal(TournamentSystem.Swiss, entry.System);
        Assert.Equal("12 Minuten + 3 Sekunden/Zug", entry.TimeControlText);
    }

    /// <summary>
    /// Die Kennung ist ein bis zu 51 Zeichen langer Slug, die Spalte fasst 24. Gekuerzt wird
    /// nicht — zwei Turniere zu einem zu machen ist der teuerste Fehler an dieser Stelle.
    /// </summary>
    [Fact]
    public void PublicIdOf_FitsTheColumnAndKeepsLongSlugsApart()
    {
        var a = SchachbundDirectorySweepService.PublicIdOf(
            "ccm-monatliches-rapidturnier-10-september-2026-12-3");
        var b = SchachbundDirectorySweepService.PublicIdOf(
            "ccm-monatliches-rapidturnier-10-oktober-2026-12-3");

        Assert.NotEqual(a, b);
        Assert.True(a.Length <= 24, a);
        Assert.StartsWith("de", a);
        Assert.Equal(a, SchachbundDirectorySweepService.PublicIdOf(
            "ccm-monatliches-rapidturnier-10-september-2026-12-3"));
    }

    /// <summary>
    /// Die Region sagt das Land — aber „europa" und „welt" sagen gar nichts Bestimmtes (zuletzt
    /// standen dort Kreta, Lettland, Suedtirol und ein Kreuzfahrtschiff). Eine falsche
    /// Foederation waere schlechter als keine: sie landete im Laenderfilter unter Deutschland.
    /// </summary>
    [Theory]
    [InlineData("bayern", "GER")]
    [InlineData("nordrhein-westfalen", "GER")]
    [InlineData("schach960", "GER")]
    [InlineData("oesterreich", "AUT")]
    [InlineData("europa", null)]
    [InlineData("welt", null)]
    [InlineData("fernschachbund", null)]
    public void FederationOf_OnlyNamesACountryWhenTheRegionDoes(string region, string? expected) =>
        Assert.Equal(expected, SchachbundDirectorySweepService.FederationOf(region));

    /// <summary>Fernschach und Online nennen im Ortsfeld ihren Server — das ist kein Spielort.</summary>
    [Theory]
    [InlineData("Caissa Center München", true)]
    [InlineData("Online", false)]
    [InlineData("BdF-Server", false)]
    [InlineData("Internet", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasVenue_SeparatesRealPlacesFromServers(string? place, bool expected) =>
        Assert.Equal(expected, SchachbundDirectorySweepService.HasVenue(place));

    [Fact]
    public async Task RunAsync_CorrespondenceChess_GetsNoLocationAndNoPin()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "DE", Name = "Online", NameNormalized = "online",
            Lat = 51.0, Lon = 10.0, Kind = GeoPlaceKind.City, Population = 100,
        });
        await _db.SaveChangesAsync();

        await CreateService(
            $"[{Row("57. Deutsche Fernschachmeisterschaft", region: "fernschachbund", place: "Online")}]")
            .RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(entry.LocationText);
        Assert.Null(entry.Lat);
        Assert.Null(entry.Federation);
    }

    /// <summary>
    /// Die Anschrift traegt bei einem Viertel der Eintraege die Postleitzahl — den genauesten Weg
    /// der Verortung.
    /// </summary>
    [Fact]
    public async Task RunAsync_GeocodesFromThePostalCodeInTheAddress()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "DE", PostalCode = "80807", Name = "München", NameNormalized = "munchen",
            Lat = 48.1799, Lon = 11.5877, Kind = GeoPlaceKind.PostalCode,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("CCM Rapidturnier")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(GeoSource.PostalCode, entry.GeoSource);
        Assert.Equal(48.1799, entry.Lat!.Value, 3);
    }

    /// <summary>
    /// Kennt chess-results das Turnier schon, fuellt diese Quelle nur Luecken — Bedenkzeit und
    /// Rundenzahl fehlen dort oefter, als man denkt.
    /// </summary>
    [Fact]
    public async Task RunAsync_FillsGapsButNeverOverwrites()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1490001", ChessResultsId = "1490001", Name = "Offene Stadtmeisterschaft Nittenau",
            Federation = "GER", StartDate = Soon, EndDate = Soon,
            TimeControlText = "aus chess-results",
        });
        await _db.SaveChangesAsync();

        await CreateService(
            $"[{Row("Offene Stadtmeisterschaft Nittenau", timeControl: "90 Minuten", rounds: 7)}]")
            .RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("aus chess-results", entry.TimeControlText);   // nicht ersetzt
        Assert.Equal(7, entry.Rounds);                              // Luecke gefuellt
    }

    [Fact]
    public async Task RunAsync_NotesWhereTheEntryCameFrom()
    {
        await CreateService($"[{Row("CCM Rapidturnier")}]").RunAsync();

        var source = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(DirectorySourceKind.GermanChessFederation, source.Kind);
        // Der KURZSCHLUESSEL, auch wenn dieser Slug (21 Zeichen) noch gepasst haette: EINE Form je
        // Quelle. Lange Slugs gibt es wirklich — an ihnen scheiterte die Quelle jede Nacht mit
        // „Data too long for column", nach 283 s hoeflichen Crawlens. Lesbar bleibt die Herkunft in
        // der `Url`, und genau dafuer ist sie da.
        Assert.Equal(SchachbundDirectorySweepService.PublicIdOf("ccm-rapidturnier-2026"), source.ExternalId);
        Assert.True(source.ExternalId.Length <= ExternalDirectorySource.MaxExternalIdLength);
        Assert.Equal("https://www.schachbund.de/turnierdetails/ccm-rapidturnier-2026.html", source.Url);
    }

    /// <summary>Derselbe Termin steht in mehreren Regionen — angelegt wird er einmal.</summary>
    [Fact]
    public async Task RunAsync_IsIdempotent()
    {
        var json = $"[{Row("CCM Rapidturnier")}]";
        await CreateService(json).RunAsync();
        var second = await CreateService(json).RunAsync();

        Assert.Equal(0, second.Added);
        Assert.Single(_db.TournamentDirectoryEntries.ToList());
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
