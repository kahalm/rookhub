using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Kalender des italienischen Verbands (FSI).
///
/// <para>Warum die Quelle zaehlt: Italien faehrt sein Turnierwesen auf Vega/vesus, nicht auf
/// chess-results — von 285 Eintraegen verlinkt KEIN EINZIGER dorthin, eine Namensstichprobe von
/// 15 fand nur 3. Rund vier Fuenftel fehlen dort dauerhaft.</para>
/// </summary>
public class FsiDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public FsiDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private FsiDirectorySweepService CreateService(string json,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(_db, new StubClientFactory(new StubHandler(json, status)),
            new GeocodingService(_db), new TestLogger<FsiDirectorySweepService>());

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(45);

    private static string Row(string name, string eventId = "21750", string? place = "Zaragoza",
        string? province = "Roma", string? timeControl = "90 minuti + 30 secondi",
        int? rounds = 7, string region = "LAZIO") =>
        $$"""
          {"eventId":"{{eventId}}","name":"{{name}}",
           "startDate":"{{Soon:yyyy-MM-dd}}","endDate":"{{Soon.AddDays(2):yyyy-MM-dd}}",
           "region":"{{region}}","province":{{Json(province)}},"place":{{Json(place)}},
           "eventType":"Torneo Elo Italia/FIDE","timeControl":{{Json(timeControl)}},
           "rounds":{{(rounds is null ? "null" : rounds.ToString())}},"note":null}
          """;

    private static string Json(string? value) => value is null ? "null" : $"\"{value}\"";

    private async Task<TournamentDirectoryEntry> AddSearchEntryAsync(
        string tnr, string name, string? timeControl = null, int? rounds = null)
    {
        var entry = new TournamentDirectoryEntry
        {
            PublicId = tnr, ChessResultsId = tnr, Name = name, Federation = "ITA",
            StartDate = Soon, EndDate = Soon.AddDays(2),
            TimeControlText = timeControl, Rounds = rounds,
        };
        _db.TournamentDirectoryEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    /// <summary>
    /// Eine Zeile, deren Termin wir nicht lesen koennen, gilt als GELIEFERT — die Quelle fuehrt sie
    /// ja. Stand das Eintragen erst hinter den Pruefungen, sammelte so eine Zeile Fehlschlaege und
    /// war nach zwei Naechten abgesagt. Und ein geaendertes Datumsformat trifft nicht eine Zeile,
    /// sondern alle: genau der systematische Fall, den die Karenz NICHT abfaengt.
    /// </summary>
    [Fact]
    public async Task RunAsync_UnlesbarerTermin_zaehltAlsGeliefert()
    {
        var gut = Enumerable.Range(1, 11)
            .Select(i => Row($"Torneo Numero {i} di Prova", eventId: $"3000{i}"));
        await CreateService($"[{Row("Torneo Senza Data di Prova", eventId: "39999")},"
                            + string.Join(",", gut) + "]").RunAsync();
        Assert.Equal(12, _db.TournamentDirectoryEntries.Count());

        // Zweiter Lauf: dieselbe Menge, aber die erste Zeile bringt keinen lesbaren Termin mehr.
        var kaputt = """
                     {"eventId":"39999","name":"Torneo Senza Data di Prova","startDate":"31/12/2026",
                      "endDate":null,"region":"LAZIO","province":"Roma","place":"Roma",
                      "eventType":"Torneo Elo Italia/FIDE","timeControl":null,"rounds":null,"note":null}
                     """;
        await CreateService($"[{kaputt}," + string.Join(",", gut) + "]").RunAsync();

        var entry = await _db.TournamentDirectoryEntries.SingleAsync(e => e.PublicId == "it39999");
        Assert.Equal(0, entry.MissedSweeps);
        Assert.Null(entry.LastMissAt);
        Assert.Null(entry.RemovedAt);
    }

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAdded()
    {
        var result = await CreateService($"[{Row("Festival Internazionale Montesilvano")}]").RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("it21750", entry.PublicId);
        Assert.Null(entry.ChessResultsId);
        Assert.Equal("ITA", entry.Federation);
        Assert.Equal(7, entry.Rounds);
        Assert.Equal("90 minuti + 30 secondi", entry.TimeControlText);
        // Aus der Bedenkzeit abgeleitet: 90 Minuten sind Turnierschach.
        Assert.Equal(TournamentSpeed.Standard, entry.Speed);
    }

    /// <summary>
    /// „LAZIO" ist Versalschrift der Quelle, nicht der Name der Region — so stuende es sonst in
    /// der Oberflaeche neben lauter normal geschriebenen Bundeslaendern.
    /// </summary>
    [Theory]
    [InlineData("LAZIO", "Lazio")]
    [InlineData("ALTO ADIGE", "Alto Adige")]
    [InlineData("EMILIA-ROMAGNA", "Emilia-romagna")]
    [InlineData("Lombardia", "Lombardia")]   // schon gemischt: unangetastet
    [InlineData(null, null)]
    public void Titlecase_NormalisesShoutedRegions(string? input, string? expected) =>
        Assert.Equal(expected, FsiDirectorySweepService.Titlecase(input));

    /// <summary>
    /// Die Provinz ist der einzige Unterscheider, den die Quelle mitbringt, und italienische
    /// Ortsnamen sind haeufig mehrfach vergeben — „Marino" gibt es mehrmals, „Marino, Roma" nicht.
    /// Bei gleichem Ort und gleicher Provinz darf der Name aber nicht doppelt dastehen.
    /// </summary>
    [Theory]
    [InlineData("Marino", "Roma", "Marino, Roma")]
    [InlineData("Roma", "Roma", "Roma")]
    [InlineData("Roma", "roma", "Roma")]
    [InlineData(null, "Roma", "Roma")]
    [InlineData("Marino", null, "Marino")]
    [InlineData(null, null, null)]
    public void LocationOf_CombinesPlaceAndProvince(string? place, string? province, string? expected) =>
        Assert.Equal(expected, FsiDirectorySweepService.LocationOf(
            new FsiDirectorySweepService.CrawlerFsiEvent(
                "1", "n", Soon, Soon, null, province, place, null, null, null)));

    /// <summary>
    /// Kennt chess-results das Turnier schon, fuellt die FSI nur LUECKEN. Sie darf einen
    /// vorhandenen Wert nicht ersetzen — sonst entscheidet die Reihenfolge der naechtlichen
    /// Durchgaenge darueber, welche Angabe gilt.
    /// </summary>
    [Fact]
    public async Task RunAsync_KnownTournament_FillsGapsButNeverOverwrites()
    {
        var existing = await AddSearchEntryAsync("1490241", "Festival Internazionale Montesilvano",
            timeControl: "60 min", rounds: null);

        var result = await CreateService($"[{Row("Festival Internazionale Montesilvano")}]").RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Matched);
        await _db.Entry(existing).ReloadAsync();
        Assert.Equal("60 min", existing.TimeControlText);   // NICHT ersetzt
        Assert.Equal(7, existing.Rounds);                   // Luecke gefuellt
    }

    /// <summary>
    /// Der Fall, der sonst ein Duplikat erzeugt: wir legen das Turnier aus dem FSI-Kalender an,
    /// spaeter holt die chess-results-Suche es ein — und dann steht dieselbe Veranstaltung unter
    /// zwei Kennungen.
    /// </summary>
    [Fact]
    public async Task RunAsync_OwnEntryOvertakenByTheSearch_IsRetired()
    {
        var json = $"[{Row("Festival Internazionale Montesilvano")}]";
        await CreateService(json).RunAsync();
        var own = Assert.Single(_db.TournamentDirectoryEntries.ToList());

        await AddSearchEntryAsync("1490241", "Festival Internazionale Montesilvano");

        var result = await CreateService(json).RunAsync();

        Assert.Equal(1, result.Retired);
        await _db.Entry(own).ReloadAsync();
        Assert.NotNull(own.RemovedAt);
    }

    [Fact]
    public async Task RunAsync_TwiceInARow_AddsOnlyOnce()
    {
        var json = $"[{Row("Festival Internazionale Montesilvano")}]";
        await CreateService(json).RunAsync();
        var second = await CreateService(json).RunAsync();

        Assert.Equal(0, second.Added);
        Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Single(_db.TournamentDirectorySources.ToList());
    }

    /// <summary>
    /// Verortet wird ueber „Ort, Provinz". Eine Postleitzahl gibt die Quelle nicht her (0 von
    /// 283), der genaueste Weg des Geocoders greift hier also nie — der Ortsname muss tragen.
    /// </summary>
    [Fact]
    public async Task RunAsync_GeocodesViaPlaceAndProvince()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "IT", Name = "Montesilvano", NameNormalized = "montesilvano",
            Lat = 42.51, Lon = 14.14, Kind = GeoPlaceKind.City, Population = 50000,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("Open Abruzzo", place: "Montesilvano", province: "Pescara")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("Montesilvano, Pescara", entry.LocationText);
        Assert.NotNull(entry.Lat);
        Assert.Equal(42.51, entry.Lat!.Value, 2);
    }

    /// <summary>Publikum und Format stehen auch hier nur im Namen; die Turnierart bleibt offen.</summary>
    [Fact]
    public async Task RunAsync_DerivesAudienceFromTheName()
    {
        await CreateService($"[{Row("Campionato Giovanile U12 femminile")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.True(entry.AgeGroups.HasFlag(TournamentAgeGroups.U12));
        Assert.Equal(TournamentGender.Female, entry.Gender);
        Assert.Equal(TournamentKind.Unknown, entry.Kind);
    }

    [Fact]
    public async Task RunAsync_CrawlerError_Throws() =>
        await Assert.ThrowsAnyAsync<Exception>(() =>
            CreateService("boom", HttpStatusCode.InternalServerError).RunAsync());

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
